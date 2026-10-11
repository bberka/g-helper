using GHelper;
using GHelper.Battery;
using GHelper.Fan;
using GHelper.Gpu;
using GHelper.Gpu.AMD;
using GHelper.Gpu.NVidia;
using GHelper.Helpers;
using GHelper.Platform;
using System.Diagnostics;

/// <summary>
/// Sensor aggregation (legacy HardwareControl). OS specific readings come from <see cref="ISystemSensors"/>,
/// GPU values from <see cref="IGpuControl"/> (created by <see cref="IGpuProvider"/>), fans / ACPI temperatures from Program.acpi.
/// Battery capacities keep the legacy unit (mWh); <see cref="BatteryState"/> carries Wh.
/// </summary>
public static class HardwareControl
{

    public static IGpuControl? GpuControl;

    public static float? cpuTemp = -1;
    public static float? gpuTemp = -1;

    public static float? cpuPower;
    public static float? gpuPower;

    /// <summary>Battery power in W. Legacy sign: positive while charging, negative while discharging.</summary>
    public static decimal? batteryRate = 0;
    public static decimal batteryHealth = -1;
    public static decimal batteryCapacity = -1;

    public static decimal? designCapacity;
    public static decimal? fullCapacity;
    public static decimal? chargeCapacity;

    public static string? batteryCharge;

    public static string? cpuFan;
    public static string? gpuFan;
    public static string? midFan;

    public static int? cpuFanRPM;
    public static int? gpuFanRPM;

    public static int? gpuUse;

    public static int? cpuUsage;
    public static int? gpuUsage;
    public static int? vramUsage;
    public static int? ramUsage;
    public static int? vramUsedMb;
    public static int? ramUsedMb;

    // Set by the overlay so ReadSensorsOverlay skips sensors that won't be
    // displayed in the current mode (fan ACPI calls and CPU usage counter).
    public static bool readFans;
    public static bool readUsage;
    public static bool readMemory;
    public static bool readPower;
    public static bool readBattery;

    static long lastUpdate;

    static bool isPZ13 = AppConfig.IsPZ13();
    static bool isAlly = AppConfig.IsAlly();
    static bool isAMDiGPU = AppConfig.IsAMDiGPU();

    static bool _chargeWatt = AppConfig.Is("charge_watt");

    private static ISystemSensors Sensors => PlatformServices.Current.Sensors;

    public static bool chargeWatt
    {
        get
        {
            return _chargeWatt;
        }
        set
        {
            AppConfig.Set("charge_watt", value ? 1 : 0);
            _chargeWatt = value;
        }
    }

    private static int GetGpuUse()
    {
        try
        {
            int? gpuUse = GpuControl?.GetGpuUse();
            Logger.WriteLine("GPU usage: " + GpuControl?.FullName + " " + gpuUse + "%");
            if (gpuUse is not null) return (int)gpuUse;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
        }

        return 0;
    }

    static long _lastBatteryRead;

    private static decimal ToMilliWattHours(double wh) => (decimal)(wh * 1000);

    // remaining energy in mWh (legacy RemainingCapacity); BatteryState only carries percent + full capacity
    private static decimal RemainingMilliWattHours(BatteryState state)
    {
        if (state.FullChargeCapacityWh is not > 0) return 0;
        return ToMilliWattHours(state.Percent / 100.0 * state.FullChargeCapacityWh.Value);
    }

    public static void ReadBatteryState()
    {
        var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        if (isAlly)
        {
            try
            {
                decimal? discharge = Program.acpi.GetBatteryDischarge();
                if (discharge is not null)
                {
                    batteryRate = Math.Abs(discharge.Value) < 1.5m ? 0 : discharge;

                    // Capacity from cached power manager state is sufficient
                    var batteryState = Sensors.GetBatteryState();
                    if (batteryState.HasValue)
                    {
                        chargeCapacity = RemainingMilliWattHours(batteryState.Value);

                        if (fullCapacity is null or 0 && batteryState.Value.FullChargeCapacityWh > 0)
                            fullCapacity = ToMilliWattHours(batteryState.Value.FullChargeCapacityWh!.Value);
                    }
                    FormatBatteryCharge(batteryState);
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Battery Reading: " + ex.Message);
            }
        }

        if (Math.Abs(now - _lastBatteryRead) < 5000)
        {
            FormatBatteryCharge(null);
            return;
        }
        _lastBatteryRead = now;

        batteryRate = 0;
        chargeCapacity = 0;
        BatteryState? state = null;

        try
        {
            var statusTask = Task.Run(Sensors.GetBatteryState);
            state = statusTask.Wait(1000) ? statusTask.Result : null;

            if (state.HasValue)
            {
                chargeCapacity = RemainingMilliWattHours(state.Value);
                if (state.Value.DischargeRateW is double rate && rate != 0)
                    batteryRate = (decimal)rate;

                // Full capacity doesn't change at runtime, only need it once
                if (fullCapacity is null or 0 && state.Value.FullChargeCapacityWh > 0)
                    fullCapacity = ToMilliWattHours(state.Value.FullChargeCapacityWh!.Value);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Battery Reading: " + ex.Message);
        }

        FormatBatteryCharge(state);
    }

    private static void FormatBatteryCharge(BatteryState? state)
    {
        if (fullCapacity > 0 && chargeCapacity > 0)
        {
            batteryCapacity = Math.Min(100, (decimal)chargeCapacity / (decimal)fullCapacity * 100);
            if (batteryCapacity > 99 && BatteryControl.chargeFull) BatteryControl.UnSetBatteryLimitFull();
            batteryCharge = chargeWatt
                ? Math.Round((decimal)chargeCapacity / 1000, 1) + "Wh"
                : Math.Round(batteryCapacity, 1) + "%";
        }
        else if (state is { Present: true, Percent: > 0 } s)
        {
            // only a percentage is known (no energy values)
            batteryCapacity = Math.Min(100, (decimal)s.Percent);
            if (batteryCapacity > 99 && BatteryControl.chargeFull) BatteryControl.UnSetBatteryLimitFull();
            batteryCharge = Math.Round(batteryCapacity, 1) + "%";
        }
    }

    public static void ReadDesignCapacity()
    {
        if (designCapacity > 0) return;

        try
        {
            var state = Sensors.GetBatteryState();
            if (state?.DesignCapacityWh > 0)
                designCapacity = ToMilliWattHours(state.Value.DesignCapacityWh!.Value);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Design Capacity Reading: " + ex.Message);
        }
    }

    public static void RefreshBatteryHealth()
    {
        if (designCapacity is null) ReadDesignCapacity();
        if (fullCapacity is null or 0) ReadBatteryState();

        if (designCapacity is null || fullCapacity is null || designCapacity == 0 || fullCapacity == 0)
        {
            batteryHealth = -1;
            return;
        }

        decimal health = (decimal)fullCapacity / (decimal)designCapacity;
        Logger.WriteLine("Design Capacity: " + designCapacity + "mWh, Full Charge Capacity: " + fullCapacity + "mWh, Health: " + health + "%");
        batteryHealth = health * 100;
    }


    public static float? GetCPUTemp()
    {
        var last = DateTimeOffset.Now.ToUnixTimeSeconds();
        if (Math.Abs(last - lastUpdate) < 2) return cpuTemp;
        lastUpdate = last;

        // PZ13 (Snapdragon) has no ACPI temperature: the platform reads its thermal zone
        if (isPZ13)
        {
            try { cpuTemp = Sensors.GetCpuTemp() ?? -1; } catch { cpuTemp = -1; }
            return cpuTemp;
        }

        cpuTemp = Program.acpi.DeviceGet(AsusACPI.Temp_CPU);

        if (cpuTemp < 0) try
        {
            // OS thermal zone fallback (Windows: "Thermal Zone Information" counter, Linux: hwmon)
            float? osTemp = Sensors.GetCpuTemp();
            if (osTemp is not null) cpuTemp = osTemp;
        }
        catch (Exception)
        {
            //Debug.WriteLine("Failed reading CPU temp :" + ex.Message);
        }


        return cpuTemp;
    }

    public static float? GetGPUTemp()
    {
        try
        {
            gpuTemp = GpuControl?.GetCurrentTemperature();

        }
        catch (Exception)
        {
            gpuTemp = -1;
            //Debug.WriteLine("Failed reading GPU temp :" + ex.Message);
        }

        if (gpuTemp is null || gpuTemp < 0 || gpuTemp >= 125)
        {
            int acpiTemp = Program.acpi.DeviceGet(AsusACPI.Temp_GPU);
            gpuTemp = (acpiTemp > 0 && acpiTemp < 125) ? acpiTemp : null;
        }

        if (isAMDiGPU && gpuTemp is null)
            try { gpuTemp = AmdApu()?.GetiGpuSensors().temp; } catch { }

        return gpuTemp;
    }

    private static int _cpuPowerNullTicks;

    /// <summary>Kept for source compatibility: the platform sensors initialise their counters lazily.</summary>
    public static void InitCPUPowerAsync() { }

    public static float? GetCPUPower()
    {
        try { return Sensors.GetCpuPower(); }
        catch { return null; }
    }

    public static void ResetCPUPowerCounter()
    {
        try { Sensors.ResetCpuPowerCounter(); } catch { }
    }

    public static int? GetCPUUsage()
    {
        float? usage = Sensors.GetCpuUsage();
        if (usage is null) return null;
        return Math.Clamp((int)Math.Round(usage.Value), 0, 100);
    }

    public static (int percent, int usedMb)? GetRAMInfo() => Sensors.GetRamInfo();


    private static IGpuControl? _amdApuControl;
    private static bool _amdApuPowerFailed;

    /// <summary>
    /// AMD iGPU / APU control (OLED power optimisation, iGPU sensors). Same instance as <see cref="GpuControl"/> when the
    /// discrete GPU is AMD; otherwise created through <see cref="IGpuProvider.CreateAmdApu"/>.
    /// </summary>
    public static AmdGpuControl? AmdApu()
    {
        if (GpuControl is AmdGpuControl amd) return amd;
        _amdApuControl ??= PlatformServices.Current.Gpu.CreateAmdApu();
        return _amdApuControl as AmdGpuControl;
    }

    private static float? GetAmdApuPower()
    {
        if (_amdApuPowerFailed || !PawnIO.CpuInfo.IsAMD) return null;
        try
        {
            int? power = AmdApu()?.GetiGpuPower();
            return power > 0 ? power : null;
        }
        catch
        {
            _amdApuPowerFailed = true;
            return null;
        }
    }

    private static bool _intelMsrPowerFailed;
    private static bool _intelMsrReady;

    private static float? GetIntelMsrPower()
    {
        if (_intelMsrPowerFailed || PawnIO.CpuInfo.IsAMD) return null;
        try
        {
            var tuning = PlatformServices.Current.CpuTuning;
            if (!_intelMsrReady)
            {
                if (!tuning.IsSupported || !tuning.Initialize())
                {
                    _intelMsrPowerFailed = true;
                    Logger.WriteLine("Intel MSR: PawnIO/IntelMSR module unavailable (not installed?)");
                    return null;
                }
                _intelMsrReady = true;
                Logger.WriteLine("CPU Power source: Intel RAPL MSR");
            }
            float? power = tuning.GetIntelPackagePower();
            return power > 0 ? power : null;
        }
        catch (Exception ex)
        {
            _intelMsrPowerFailed = true;
            Logger.WriteLine("Intel MSR power read failed: " + ex.Message);
            return null;
        }
    }

    public static float? GetGPUPower()
    {
        try
        {
            float? power = GpuControl?.GetGpuPower();
            if (power is not null) return power.Value;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Failed reading GPU power: " + ex.Message);
        }

        return null;
    }

    // Fan value in 100 RPM units like AsusACPI.GetFan; falls back to the OS (hwmon) reading when ASUS has none
    private static int GetFanValue(AsusFan device)
    {
        int value = Program.acpi.GetFan(device);
        if (value >= 0) return value;

        try
        {
            int? rpm = Sensors.GetFanRpm((int)device);
            if (rpm is > 0) return rpm.Value / 100;
        }
        catch { }

        return value;
    }

    public static void ReadSensors(bool log = false)
    {
        gpuUse = -1;

        if (Program.acpi is null) return;

        cpuFan = FanSensorControl.FormatFan(AsusFan.CPU, GetFanValue(AsusFan.CPU));
        gpuFan = FanSensorControl.FormatFan(AsusFan.GPU, GetFanValue(AsusFan.GPU));
        midFan = FanSensorControl.FormatFan(AsusFan.Mid, GetFanValue(AsusFan.Mid));

        cpuTemp = GetCPUTemp();
        gpuTemp = GetGPUTemp();

        if (log) Logger.WriteLine($"Temps: {cpuTemp} {gpuTemp} {cpuFan} {gpuFan} {midFan}");

        ReadBatteryState();
    }

    // Lightweight sensor read used by the overlay timer - skips battery health, WMI and design capacity
    public static void ReadSensorsOverlay()
    {
        if (Program.acpi is null) return;

        if (readFans)
        {
            int cpu = GetFanValue(AsusFan.CPU);
            int gpu = GetFanValue(AsusFan.GPU);
            cpuFanRPM = cpu >= 0 ? cpu * 100 : null;
            gpuFanRPM = gpu >= 0 ? gpu * 100 : null;
        }
        else
        {
            cpuFanRPM = null;
            gpuFanRPM = null;
        }

        cpuTemp = GetCPUTemp();
        gpuTemp = GetGPUTemp();

        if (readUsage)
        {
            cpuUsage = GetCPUUsage();
            try { gpuUsage = GpuControl?.GetGpuUse(); } catch { gpuUsage = null; }
            if (isAMDiGPU && gpuUsage is null)
                try { gpuUsage = AmdApu()?.GetiGpuSensors().use; } catch { }
        }
        else
        {
            cpuUsage = null;
            gpuUsage = null;
        }

        if (readMemory)
        {
            var ram = GetRAMInfo();
            ramUsage = ram?.percent;
            ramUsedMb = ram?.usedMb;

            try
            {
                var vram = GpuControl?.GetVramInfo() ?? (isAMDiGPU ? AmdApu()?.GetVramInfo() : null);
                if (vram is { } v && v.totalMb > 0)
                {
                    vramUsedMb = (int)v.usedMb;
                    vramUsage = (int)Math.Clamp(v.usedMb * 100 / v.totalMb, 0, 100);
                }
                else { vramUsedMb = null; vramUsage = null; }
            }
            catch { vramUsedMb = null; vramUsage = null; }
        }
        else
        {
            ramUsage = null;
            ramUsedMb = null;
            vramUsage = null;
            vramUsedMb = null;
        }

        if (readPower)
        {
            float? newCpu = GetCPUPower() ?? GetIntelMsrPower() ?? GetAmdApuPower();
            float? iGpuPower = null;

            if (isAMDiGPU && GpuControl is null)
            {
                var energy = PlatformCapabilities.GetOptional<IApuEnergySensors>();

                float? apu = energy?.SampleApuPower();
                if (apu > 0) newCpu = apu;
                float? cores = energy?.SampleCpuPower() ?? energy?.SampleCoresPower();
                if (cores > 0 && newCpu > cores)
                {
                    iGpuPower = newCpu - cores;
                    newCpu = cores;
                }
                else
                    try
                    {
                        var sensors = AmdApu()?.GetiGpuSensors();
                        if (sensors is { } s)
                        {
                            if (s.gfxPower > 0) iGpuPower = s.gfxPower;
                            if (s.cpuPower > 0) newCpu = s.cpuPower;
                            else if (s.asicPower > s.gfxPower) newCpu = s.asicPower - s.gfxPower;
                        }
                    }
                    catch { }
            }

            if (newCpu > 0)
            {
                cpuPower = newCpu;
                _cpuPowerNullTicks = 0;
            }
            else
            {
                if (++_cpuPowerNullTicks >= 5)
                    cpuPower = null;
            }

            gpuPower = iGpuPower ?? GetGPUPower();
        }
        else
        {
            cpuPower = null;
            gpuPower = null;
        }

        if (readBattery) ReadBatteryState();
    }

    public static double GetBatteryChargePercentage()
    {
        try
        {
            var state = Sensors.GetBatteryState();
            if (state is { Present: true }) return state.Value.Percent;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Battery Percentage Reading: " + ex.Message);
        }
        return 0;
    }

    public static bool IsUsedGPU(int threshold = 10)
    {
        if (GetGpuUse() > threshold)
        {
            Thread.Sleep(1000);
            return (GetGpuUse() > threshold);
        }
        return false;
    }


    public static NvidiaGpuControl? GetNvidiaGpuControl()
    {
        if (GpuControl is { IsNvidia: true } && GpuControl is NvidiaGpuControl nv)
            return nv;
        else
            return null;
    }

    public static void DisposeGpuControl()
    {
        bool wasNvidia = GpuControl?.IsNvidia == true;
        GpuControl?.Dispose();
        GpuControl = null;
        if (wasNvidia)
        {
            // NVML shutdown / NVAPI unload (legacy NvmlHelper.Shutdown + UnloadNvAPI)
            try { PlatformServices.Current.Gpu.UnloadNative(); }
            catch (Exception ex) { Logger.WriteLine("GPU unload: " + ex.Message); }
        }
    }

    public static void RecreateGpuControlWithDelay(int delay = 5)
    {
        // Re-enabling the discrete GPU takes a bit of time,
        // so a simple workaround is to refresh again after that happens
        Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(delay));
            RecreateGpuControl();
        });
    }

    public static async Task RecreateGpuControlWithRetry(int retries, int delay)
    {
        for (int i = 0; i < retries; i++)
        {
            RecreateGpuControl();
            if (GpuControl is not null) break;
            await Task.Delay(TimeSpan.FromSeconds(delay));
        }
    }

    public static void RecreateGpuControl()
    {
        if (AppConfig.NoGpu()) return;
        try
        {
            DisposeGpuControl();

            // Provider order matches legacy: NVIDIA first, then AMD
            IGpuControl? gpuControl = PlatformServices.Current.Gpu.CreateDiscrete();

            if (gpuControl is not null && gpuControl.IsValid)
            {
                GpuControl = gpuControl;
                if (!gpuControl.IsNvidia && gpuControl.FullName.Contains("6850M")) AppConfig.Set("xgm_special", 1);
                Logger.WriteLine(GpuControl.FullName);
                return;
            }

            gpuControl?.Dispose();

            Logger.WriteLine("dGPU not found");
            GpuControl = null;


        }
        catch (Exception ex)
        {
            Logger.WriteLine("Can't connect to GPU " + ex.Message);
        }
    }


    public static void KillGPUApps()
    {

        List<string> tokill = new() { "EADesktop", "epicgameslauncher", "ASUSSmartDisplayControl" };

        foreach (string kill in tokill) ProcessHelper.KillByName(kill);

        if (AppConfig.Is("kill_gpu_apps") && GpuControl is not null)
        {
            GpuControl.KillGPUApps();
        }
    }

    public static void Dispose()
    {
        _intelMsrReady = false;
    }
}
