using GHelper.Platform;
using System.Diagnostics;

namespace GHelper.Gpu.NVidia;

/// <summary>
/// NVIDIA dGPU control on top of NVML (nvml.dll / libnvidia-ml.so.1, see <see cref="NvmlHelper"/>).
/// Replaces the legacy NVAPI implementation: temperature, power, VRAM, utilisation and clock offsets all come from
/// NVML; "is the GPU asleep" is answered by the platform (IGpuProvider.GetDiscretePowerState) so that polling never
/// wakes a powered-down GPU. When NVML is not present the instance is simply not valid.
/// </summary>
public class NvidiaGpuControl : IGpuControl
{

    public static int MaxCoreOffset = AppConfig.Get("max_gpu_core", 250);
    public static int MaxMemoryOffset = AppConfig.Get("max_gpu_memory", 500);

    public static int MinCoreOffset = AppConfig.Get("min_gpu_core", -250);
    public static int MinMemoryOffset = AppConfig.Get("min_gpu_memory", -500);

    public static int MinClockLimit = AppConfig.Get("min_gpu_clock", 400);
    public const int MaxClockLimit = 3000;

    private uint _index;
    private string? _name;

    public NvidiaGpuControl()
    {
        FindInternalDiscreteGpu();

        if (IsValid)
        {
            ApplyModelLimits(FullName);
        }
    }

    /// <summary>Widens the overclock ranges for the big Blackwell / Ada parts (legacy behaviour).</summary>
    public static void ApplyModelLimits(string fullName)
    {
        if (fullName.Contains("5080") || fullName.Contains("5090"))
        {
            MaxCoreOffset = AppConfig.Get("max_gpu_core", 400);
            MaxMemoryOffset = AppConfig.Get("max_gpu_memory", 1000);
            Logger.WriteLine($"NVIDIA GPU: {fullName} ({MaxCoreOffset},{MaxMemoryOffset})");
        }
        if (fullName.Contains("5070 Ti") || fullName.Contains("4080") || fullName.Contains("4090"))
        {
            MaxCoreOffset = AppConfig.Get("max_gpu_core", 300);
            Logger.WriteLine($"NVIDIA GPU: {fullName} ({MaxCoreOffset},{MaxMemoryOffset})");
        }
    }

    /// <summary>
    /// The internal discrete GPU: the first NVML device called "... Laptop GPU" (legacy: NVAPI SystemType.Laptop),
    /// otherwise the first device.
    /// </summary>
    public static int SelectDevice(IReadOnlyList<string?> names)
    {
        for (int i = 0; i < names.Count; i++)
            if (names[i]?.Contains("Laptop", StringComparison.OrdinalIgnoreCase) == true) return i;
        return names.Count > 0 && names[0] is not null ? 0 : -1;
    }

    private void FindInternalDiscreteGpu()
    {
        try
        {
            if (!NvmlHelper.IsAvailable) return;

            int count = NvmlHelper.GetDeviceCount();
            var names = new List<string?>();
            for (int i = 0; i < count; i++) names.Add(NvmlHelper.GetDeviceName((uint)i));

            int selected = SelectDevice(names);
            if (selected < 0) return;

            _index = (uint)selected;
            _name = names[selected];
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            _name = null;
        }
    }

    public bool IsValid => _name != null;

    public bool IsNvidia => IsValid;

    public string FullName => _name ?? "";

    public int? _lastTemp;
    public int _lastTempTime = 0;

    private static bool verboseLog = false;

    private GpuPowerState _lastState = GpuPowerState.Off;
    private long _lastStateTime = -StateCacheMs;
    private const int StateCacheMs = 500;

    private GpuPowerState GetGpuState()
    {
        if (!IsValid) return GpuPowerState.Off;
        if (Environment.TickCount64 - _lastStateTime < StateCacheMs) return _lastState;

        GpuPowerState state;
        try
        {
            state = PlatformServices.IsRegistered ? PlatformServices.Current.Gpu.GetDiscretePowerState() : GpuPowerState.Unknown;
        }
        catch (Exception ex)
        {
            if (verboseLog) Logger.WriteLine($"GPU: {ex.Message}");
            state = GpuPowerState.Unknown;
        }

        // The platform could not tell without touching the device: assume it is on, NVML decides.
        _lastState = state == GpuPowerState.Unknown ? GpuPowerState.Active : state;
        if (verboseLog) Logger.WriteLine($"GPU: {_lastState}");
        _lastStateTime = Environment.TickCount64;
        return _lastState;
    }

    public int? ReadCurrentTemperature(bool log = false)
    {
        if (!IsValid) return null;

        int? temp = NvmlHelper.GetTemperature(_index);

        if (log || verboseLog) Logger.WriteLine($"GPU Temp: {temp}C");
        return temp;
    }

    private Task<int?>? _readTask;

    public int? GetCurrentTemperature()
    {
        if (!IsValid) return null;

        var state = GetGpuState();
        if (state == GpuPowerState.Off) return null;

        if ((_readTask?.IsCompleted ?? true) && (state == GpuPowerState.Active || ShouldRefresh()))
        {
            _readTask = Task.Run(() =>
            {
                var temp = ReadCurrentTemperature();
                if (temp is not null)
                {
                    _lastTemp = temp;
                    _lastTempTime = Environment.TickCount;
                }
                return temp;
            });
        }

        _readTask?.Wait(500);

        return _lastTemp;
    }

    private bool ShouldRefresh()
    {
        const int minInterval = 5_000;
        const int maxInterval = 120_000;
        const float deltaMin = 5f;
        const float deltaMax = 20f;

        if (_lastTemp is null) return true;

        float? cpuTemp = HardwareControl.GetCPUTemp();
        if (cpuTemp is null) return false;

        var delta = _lastTemp.Value - cpuTemp.Value;

        if (delta < deltaMin) return false;

        var t = Math.Clamp((delta - deltaMin) / (deltaMax - deltaMin), 0f, 1f);
        var interval = (int)(maxInterval - t * (maxInterval - minInterval));

        var refresh = Environment.TickCount > _lastTempTime + interval;
        if (verboseLog) Logger.WriteLine($"GPU Temp Refresh Interval: {interval}ms {refresh}");

        return refresh;
    }

    public void Dispose()
    {
        _name = null;
        _readTask = null;
        NvmlHelper.Shutdown();
    }

    private static readonly HashSet<string> _systemProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "dwm", "csrss", "winlogon", "services", "lsass", "smss", "wininit",
        "svchost", "fontdrvhost", "igfxem", "igfxhk", "igfxext",
        "nvcontainer", "nvdisplay.container", "nvsettings", "nvspcaps64",
        "nvsphelper64", "nvwmi64", "nvcplui", "atieclxx", "atiesrxx",
        "explorer", "taskhostw", "sihost", "runtimebroker", "shellexperiencehost",
        "searchhost", "startmenuexperiencehost", "textinputhost",
        "applicationframehost", "systemsettings", "dllhost", "conhost",
        "audiodg", "ctfloader", "spoolsv", "wlanext", "msdtc",
        // Linux session / compositor / driver helper processes
        "xorg", "x", "xwayland", "gnome-shell", "kwin_wayland", "kwin_x11", "plasmashell", "mutter",
        "sway", "systemd", "nvidia-persistenced", "nvidia-powerd", "nvidia-settings",
    };

    public static bool IsSystemProcess(string processName) => _systemProcessNames.Contains(processName);

    public void KillGPUApps()
    {
        if (!IsValid) return;

        int currentPid = Environment.ProcessId;

        try
        {
            foreach (int pid in NvmlHelper.GetProcessIds(_index))
            {
                if (pid == currentPid || pid <= 0) continue;

                try
                {
                    using Process process = Process.GetProcessById(pid);
                    if (process.SessionId == 0) continue;
                    if (IsSystemProcess(process.ProcessName)) continue;

                    Logger.WriteLine("Kill:" + process.ProcessName);
                    try
                    {
                        process.Kill();
                        Logger.WriteLine($"Stopped: {process.ProcessName}");
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine($"Failed to stop: {process.ProcessName} {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }
    }


    public bool GetClocks(out int core, out int memory)
    {
        try
        {
            var temp = ReadCurrentTemperature(true); // Force wake up GPU for clock reading

            if (!NvmlHelper.GetClockOffsets(out core, out memory, _index))
            {
                Logger.WriteLine("GET GPU CLOCKS: not supported by the NVIDIA driver");
                core = memory = 0;
                return false;
            }

            Logger.WriteLine($"GET GPU CLOCKS: {core}, {memory}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteLine("GET GPU CLOCKS:" + ex.Message);
            core = memory = 0;
            return false;
        }

    }


    private static bool RunCommand(string name, string args, int timeoutMs = 0)
    {
        try
        {
            PlatformServices.Current.Process.RunCommand(name, args, null, timeoutMs);
            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.ToString());
            return false;
        }

    }

    public int GetMaxGPUCLock()
    {
        int limit = NvmlHelper.LockedClockMhz;
        Logger.WriteLine("GET CLOCK LIMIT: " + limit);
        return limit;
    }


    public int SetMaxGPUClock(int clock)
    {

        if (clock < MinClockLimit || clock >= MaxClockLimit) clock = 0;

        int _clockLimit = GetMaxGPUCLock();

        // NVML has no getter for a clock lock: -1 = none / set by someone else. A reset is applied once per session.
        if (clock > 0 && _clockLimit == clock) return 0;
        if (clock == 0 && _clockLimit < 0 && _resetApplied) return 0;

        int rc = NvmlHelper.SetLockedClock(clock, _index);
        if (rc != 0)
        {
            // not elevated / old driver: nvidia-smi does the same through the driver's own privileged path
            if (clock > 0) RunCommand("nvidia-smi", $"-lgc 0,{clock}");
            else RunCommand("nvidia-smi", "-rgc");
            NvmlHelper.LockedClockMhz = clock > 0 ? clock : -1;
        }

        _resetApplied = clock == 0;
        return 1;
    }

    private static bool _resetApplied;

    private static bool CanManageServices() =>
        PlatformServices.IsRegistered
        && PlatformServices.Current.Kind == PlatformKind.Windows
        && PlatformServices.Current.System.IsElevated;

    public static void RestartNvContainer()
    {
        if (!CanManageServices()) return;
        RunCommand("powershell", @"Restart-Service -Name 'NvContainerLocalSystem' -Force", 30000);
    }

    public static void RestartNVService()
    {
        if (!CanManageServices()) return;
        RunCommand("powershell", @"Restart-Service -Name 'NVDisplay.ContainerLocalSystem' -Force", 30000);
        RunCommand("powershell", @"Restart-Service -Name 'NvContainerLocalSystem' -Force", 30000);
    }

    public static void StopNVService()
    {
        if (!CanManageServices()) return;
        RunCommand("powershell", @"Stop-Service -Name 'NvContainerLocalSystem' -Force", 30000);
        RunCommand("powershell", @"Stop-Service -Name 'NVDisplay.ContainerLocalSystem' -Force", 30000);
    }

    public int SetClocks(int core, int memory)
    {

        if (core < MinCoreOffset || core > MaxCoreOffset) return 0;
        if (memory < MinMemoryOffset || memory > MaxMemoryOffset) return 0;

        GetClocks(out int currentCore, out int currentMemory);

        // Nothing to set
        if (Math.Abs(core - currentCore) < 5 && Math.Abs(memory - currentMemory) < 5) return 0;

        Logger.WriteLine($"SET GPU CLOCKS: {core}, {memory}");
        int rc = NvmlHelper.SetClockOffsets(core, memory, _index);
        if (rc != 0)
        {
            Logger.WriteLine("SET GPU CLOCKS: NVML error " + rc);
            return -1;
        }

        return 1;
    }

    public int? GetGpuUse()
    {
        if (!IsValid) return null;
        if (GetGpuState() != GpuPowerState.Active) return null;

        return NvmlHelper.GetGpuUtilization(_index);
    }


    public float? GetGpuPower()
    {
        if (!IsValid) return null;
        var state = GetGpuState();
        if (state == GpuPowerState.Off)
        {
            NvmlHelper.Shutdown();
            return null;
        }
        if (state != GpuPowerState.Active) return 0f;
        return NvmlHelper.GetGpuPower(_index) ?? 0f;
    }

    public (long usedMb, long totalMb)? GetVramInfo()
    {
        if (!IsValid) return null;
        if (GetGpuState() != GpuPowerState.Active) return null;
        return NvmlHelper.GetMemoryInfo(_index);
    }

}
