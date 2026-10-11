using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GHelper.Platform.Windows.Sensors;

/// <summary>
/// OS half of legacy HardwareControl: thermal zone CPU temperature, CPU load (GetSystemTimes), RAM, battery
/// (battery class IOCTLs + CallNtPowerInformation + WMI static data) and the "Energy Meter" CPU power counters.
/// ASUS ACPI temperatures / fans are read by Core through <c>Program.acpi</c>.
/// </summary>
public sealed class WindowsSystemSensors : ISystemSensors
{
    private static readonly string[] PowerCounterInstances =
        ["Apu Power", "RAPL_Package0_PKG", "CPU Power", "Socket Power", "Current Socket Power"];

    private const int CpuPowerMaxReadErrors = 3;

    private readonly object sync = new();

    // CPU temperature
    private PerformanceCounter? cpuTempCounter;
    private bool cpuTempCounterFailed;

    // CPU usage
    private long lastIdle, lastKernel, lastUser, lastTick;
    private bool usageBaseline;

    // CPU power
    private PerformanceCounter? cpuPowerCounter;
    private bool cpuPowerCounterFailed;
    private bool cpuPowerInitStarted;
    private int cpuPowerReadErrors;
    private List<PerformanceCounter>? coreCounters;
    private EnergyChannel? cpuChannel, apuChannel;

    // battery
    private long lastBatteryRead;
    private BatteryNative.BATTERY_STATUS? lastBatteryStatus;
    private double? designCapacityWh;
    private bool designCapacityRead;

    // --- CPU temperature ----------------------------------------------------------------------

    public float? GetCpuTemp()
    {
        if (!OperatingSystem.IsWindows()) return null;

        // Snapdragon based ProArt PZ13 exposes the thermal zone through WMI only
        if (AppConfig.IsPZ13())
        {
            double wmi = GetCpuTempWmi();
            return wmi > 0 ? (float)wmi : null;
        }

        if (cpuTempCounterFailed) return null;

        try
        {
            cpuTempCounter ??= new PerformanceCounter("Thermal Zone Information", "Temperature", @"\_TZ.THRM", true);
            float temp = cpuTempCounter.NextValue() - 273;
            return temp > 0 && temp < 150 ? temp : null;
        }
        catch (Exception)
        {
            cpuTempCounterFailed = true;
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static double GetCpuTempWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI",
                @"SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature WHERE InstanceName = 'ACPI\\QCOM0C5A\\1_0'");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    double tempKelvin = Convert.ToDouble(obj["CurrentTemperature"]);
                    return (tempKelvin / 10) - 273.15;
                }
            }
        }
        catch (Exception)
        {
            // not available
        }

        return -1;
    }

    // --- CPU usage ----------------------------------------------------------------------------

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    public float? GetCpuUsage()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (!GetSystemTimes(out long idle, out long kernel, out long user)) return null;

        lock (sync)
        {
            return ComputeUsage(idle, kernel, user, Environment.TickCount64);
        }
    }

    private float? ComputeUsage(long idle, long kernel, long user, long now)
    {
        // first read, or resumed after a long pause: capture the baseline and skip this tick
        if (!usageBaseline || now - lastTick > 2000)
        {
            lastIdle = idle; lastKernel = kernel; lastUser = user; lastTick = now;
            usageBaseline = true;
            return null;
        }

        long deltaIdle = idle - lastIdle;
        long deltaTotal = (kernel - lastKernel) + (user - lastUser); // kernel time includes idle

        lastIdle = idle; lastKernel = kernel; lastUser = user; lastTick = now;

        return UsagePercent(deltaIdle, deltaTotal);
    }

    /// <summary>CPU load from idle / total (kernel + user) deltas, 0..100.</summary>
    public static float UsagePercent(long deltaIdle, long deltaTotal)
    {
        if (deltaTotal <= 0) return 0;
        return Math.Clamp((int)Math.Round((1.0 - (double)deltaIdle / deltaTotal) * 100), 0, 100);
    }

    // --- RAM ----------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public (int percent, int usedMb)? GetRamInfo()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status)) return null;

        int usedMb = (int)((status.ullTotalPhys - status.ullAvailPhys) / (1024 * 1024));
        return ((int)status.dwMemoryLoad, usedMb);
    }

    // --- battery ------------------------------------------------------------------------------

    public BatteryState? GetBatteryState()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            return ReadBattery();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Battery Reading: " + ex.Message);
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private BatteryState? ReadBattery()
    {
        var system = BatteryNative.GetSystemBatteryState();
        bool haveSystemPower = Power.PowrProf.GetSystemPowerStatus(out var power);

        bool present = system?.BatteryPresent ?? (haveSystemPower && (power.BatteryFlag & 128) == 0);
        if (!present && system is null && !haveSystemPower) return null;

        long now = Environment.TickCount64;
        if (lastBatteryStatus is null || now - lastBatteryRead >= 5000)
        {
            lastBatteryRead = now;
            var task = Task.Run(BatteryNative.QueryBatteryStatus);
            lastBatteryStatus = task.Wait(1000) ? task.Result : null;
        }

        var status = lastBatteryStatus;

        double? fullWh = system is { MaxCapacity: > 0 } ? system.Value.MaxCapacity / 1000.0 : null;
        double? remainingWh = status is not null ? status.Value.Capacity / 1000.0 : null;

        double percent = BatteryMath.Percent(remainingWh, fullWh, haveSystemPower ? power.BatteryLifePercent : (byte)255);

        bool onAc = system?.AcOnLine ?? (haveSystemPower && power.ACLineStatus == 1);
        bool charging = system?.Charging ?? (haveSystemPower && (power.BatteryFlag & 8) != 0);

        double? rateW = status is not null ? status.Value.Rate / 1000.0 : null;
        double? voltage = status is not null && status.Value.Voltage > 0 && status.Value.Voltage != -1 ? status.Value.Voltage / 1000.0 : null;

        return new BatteryState(present, charging, onAc, percent, rateW, ReadDesignCapacityWh(), fullWh, voltage);
    }

    [SupportedOSPlatform("windows")]
    private double? ReadDesignCapacityWh()
    {
        if (designCapacityRead) return designCapacityWh;

        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"root\WMI"), new ObjectQuery("SELECT DesignedCapacity FROM BatteryStaticData"));
            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
            {
                using (obj)
                {
                    decimal mWh = Convert.ToDecimal(obj["DesignedCapacity"]);
                    if (mWh > 0) designCapacityWh = (double)mWh / 1000.0;
                }
            }

            designCapacityRead = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Design Capacity Reading: " + ex.Message);
        }

        return designCapacityWh;
    }

    // --- CPU power ----------------------------------------------------------------------------

    public float? GetCpuPower()
    {
        if (!OperatingSystem.IsWindows()) return null;

        InitCpuPowerAsync();

        var counter = cpuPowerCounter;
        if (cpuPowerCounterFailed || counter == null) return null;

        try
        {
            float mW = counter.NextValue();
            if (mW > 0) return mW / 1000f;
        }
        catch
        {
            // counter became invalid (e.g. after a fullscreen game exits on Intel): allow a few re-init attempts,
            // then give up so we don't spawn a task every second forever. ResetCpuPowerCounter gives a fresh chance.
            counter.Dispose();
            cpuPowerCounter = null;
            if (++cpuPowerReadErrors >= CpuPowerMaxReadErrors) cpuPowerCounterFailed = true;
            else
            {
                cpuPowerCounterFailed = false;
                cpuPowerInitStarted = false;
            }
        }

        return null;
    }

    public void ResetCpuPowerCounter()
    {
        cpuPowerReadErrors = 0;
        cpuPowerCounterFailed = false;
    }

    private void InitCpuPowerAsync()
    {
        lock (sync)
        {
            if (cpuPowerInitStarted) return;
            cpuPowerInitStarted = true;
        }

        Task.Run(InitCpuPower);
    }

    [SupportedOSPlatform("windows")]
    private void InitCpuPower()
    {
        bool amdIGpu = AppConfig.IsAMDiGPU();

        if (amdIGpu && cpuChannel is null)
        {
            try
            {
                apuChannel = new EnergyChannel("Apu Power");
                cpuChannel = new EnergyChannel("CPU Power");
                Logger.WriteLine("Power source: Energy Meter APU/CPU channels");
            }
            catch { }
        }

        if (amdIGpu && coreCounters is null)
        {
            try
            {
                var names = new PerformanceCounterCategory("Energy Meter").GetInstanceNames();
                var cores = new List<PerformanceCounter>();
                foreach (var name in names.Where(n => n.EndsWith("_CORE")))
                {
                    var counter = new PerformanceCounter("Energy Meter", "Power", name, true);
                    counter.NextValue();
                    cores.Add(counter);
                }

                if (cores.Count > 0)
                {
                    coreCounters = cores;
                    Logger.WriteLine($"CPU Power source: {cores.Count} RAPL cores");
                }
            }
            catch { }
        }

        // cached instance name first: skips the category enumeration (1-2 s on a cold perflib cache)
        string? cached = AppConfig.GetString("cpu_power_counter");
        if (!string.IsNullOrEmpty(cached))
        {
            try
            {
                var counter = new PerformanceCounter("Energy Meter", "Power", cached, true);
                counter.NextValue();
                cpuPowerCounter = counter;
                Logger.WriteLine("CPU Power source (cached): " + cached);
                return;
            }
            catch
            {
                AppConfig.Set("cpu_power_counter", "");
            }
        }

        try
        {
            var instances = new PerformanceCounterCategory("Energy Meter").GetInstanceNames();

            foreach (var name in PowerCounterInstances)
            {
                if (!instances.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

                var counter = new PerformanceCounter("Energy Meter", "Power", name, true);
                counter.NextValue();
                cpuPowerCounter = counter;
                AppConfig.Set("cpu_power_counter", name);
                Logger.WriteLine("CPU Power source: " + name);
                return;
            }

            cpuPowerCounterFailed = true;
        }
        catch
        {
            cpuPowerCounterFailed = true;
        }
    }

    public float? GetApuPower()
    {
        InitCpuPowerAsync();
        return apuChannel?.Sample();
    }

    public float? GetCpuCoresPower()
    {
        InitCpuPowerAsync();

        float? channel = cpuChannel?.Sample();
        if (channel is not null) return channel;

        var counters = coreCounters;
        if (counters is null) return null;

        try
        {
            float mW = 0;
            foreach (var counter in counters) mW += counter.NextValue();
            return mW > 0 ? mW / 1000f : null;
        }
        catch
        {
            coreCounters = null;
            return null;
        }
    }

    public int? GetFanRpm(int fanIndex) => null; // fans come from the ASUS interface on Windows

    // Energy raw = cumulative picowatt-hours, Time raw = ms of the provider's ~1 Hz measurement clock
    [SupportedOSPlatform("windows")]
    private sealed class EnergyChannel
    {
        private readonly PerformanceCounter energy;
        private readonly PerformanceCounter time;
        private long rawEnergy, rawTime;
        private float watts;
        private int stale;

        public EnergyChannel(string instance)
        {
            energy = new PerformanceCounter("Energy Meter", "Energy", instance, true);
            time = new PerformanceCounter("Energy Meter", "Time", instance, true);
            rawEnergy = energy.RawValue;
            rawTime = time.RawValue;
        }

        public float? Sample()
        {
            try
            {
                long e = energy.RawValue, t = time.RawValue;
                if (t != rawTime)
                {
                    watts = BatteryMath.EnergyChannelWatts(e - rawEnergy, t - rawTime);
                    rawEnergy = e;
                    rawTime = t;
                    stale = 0;
                }
                else if (++stale >= 5)
                {
                    return null;
                }

                return watts > 0 ? watts : null;
            }
            catch
            {
                return null;
            }
        }
    }
}

/// <summary>Pure arithmetic of the battery / energy meter readings.</summary>
internal static class BatteryMath
{
    /// <summary>
    /// remaining / full capacity in percent (max 100); falls back to the OS reported integer percentage
    /// (255 = unknown) and then 0.
    /// </summary>
    public static double Percent(double? remainingWh, double? fullWh, byte osPercent)
    {
        if (remainingWh is > 0 && fullWh is > 0) return Math.Min(100, remainingWh.Value / fullWh.Value * 100);
        return osPercent <= 100 ? osPercent : 0;
    }

    /// <summary>Energy meter delta (picowatt-hours) over a time delta (ms) to watts.</summary>
    public static float EnergyChannelWatts(long deltaEnergy, long deltaTimeMs) =>
        deltaTimeMs == 0 ? 0f : deltaEnergy * 3.6e-6f / deltaTimeMs;
}
