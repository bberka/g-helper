using GHelper.Gpu;

namespace GHelper.Platform;

/// <summary>Run-on-login, plus the legacy "charge" one-shot, plus "run elevated at logon" (Windows task scheduler).</summary>
public interface IStartupService
{
    bool IsSupported { get; }
    bool IsScheduled();
    void Schedule();
    void UnSchedule();
    /// <summary>Re-register elevated startup (Windows). Linux: no-op.</summary>
    void ReScheduleAdmin();
    /// <summary>Validate and repair the autostart entry (path moved, wrong user, ...).</summary>
    void StartupCheck();
    void ScheduleCharge();
    void UnscheduleCharge();
}

public interface IAudioService
{
    bool IsSupported { get; }
    /// <summary>Toggles default capture device mute; returns the new muted state.</summary>
    bool ToggleMicMute();
    bool IsMicMuted();
    bool IsMuted();
    void SubscribeMute(Action<bool> onMuteChange);

    /// <summary>
    /// Loopback capture of the default render device (Aura "audio visualizer"). Callback gets mono float samples
    /// in -1..1 and the sample rate. Returns null when unsupported. Dispose to stop.
    /// </summary>
    IDisposable? StartLoopbackCapture(Action<float[], int> onSamples);
}

/// <summary>Process + elevation + single instance helpers (legacy ProcessHelper / RestrictedProcessHelper / MemoryHelper).</summary>
public interface IProcessService
{
    /// <summary>
    /// Returns false if another instance already owns the single-instance lock. When this process is the second
    /// instance it signals the first (which runs <paramref name="onSecondInstanceStarted"/>).
    /// </summary>
    bool AcquireSingleInstance(string name, Action onSecondInstanceStarted);
    void ReleaseSingleInstance();

    /// <summary>Restart this app with admin/root rights (UAC prompt / pkexec).</summary>
    void RunAsAdmin(string? param = null, bool force = false);
    void RunAsRestrictedUser(string command);

    void KillByName(string name);
    void SetPriority(System.Diagnostics.ProcessPriorityClass priorityClass = System.Diagnostics.ProcessPriorityClass.Normal);
    /// <summary>Run a command, return stdout (legacy RunCMD).</summary>
    string RunCommand(string name, string args, string? directory = null, int timeoutMs = 0);
    void OpenUrl(string url);
    void OpenFolder(string path);
    /// <summary>Trim working set (legacy MemoryHelper). No-op elsewhere.</summary>
    void TrimMemory();
}

/// <summary>OEM software the legacy app fights with (Armoury Crate services on Windows; asusd/supergfxd on Linux).</summary>
public interface IVendorServices
{
    bool IsAsusOptimizationRunning();
    bool IsArmouryRunning();
    bool IsOSDRunning();
    int GetRunningCount();
    void StopAsusServices();
    void StartAsusServices();
    void StopOnStartup();
    void KillSmartDisplayControl();
    void RunArmouryUninstaller();

    /// <summary>Windows Dynamic Lighting / Keystone settings (no-ops on Linux).</summary>
    bool IsDynamicLightingSupported { get; }
    bool IsDynamicLightingEnabled();
    void SetDynamicLighting(bool enabled);
    void OpenDynamicLightingSettings();
    bool IsKeystoneEnabled();
    void SetKeystoneEnabled(bool enabled);

    /// <summary>Human readable GPU device problem (Windows CM_Get_DevNode_Status), null if healthy/unknown.</summary>
    string? GetGpuError();

    // --- additive (platform-windows) -----------------------------------------------------------

    /// <summary>One time probe that makes Windows create its Dynamic Lighting device keys (legacy DynamicLightingHelper.Init).</summary>
    void InitDynamicLighting() { }

    /// <summary>
    /// Drive Windows Dynamic Lighting for all devices (legacy DynamicLightingHelper.SetEffect).
    /// effect: 0 solid, 1 breathing, 2 rainbow, 4 wave, 5 wheel, 6 gradient.
    /// </summary>
    void SetDynamicLightingEffect(int effect, System.Drawing.Color? color = null, System.Drawing.Color? color2 = null, int? brightness = null, int? speed = null) { }
}

/// <summary>Ryzen SMU / Intel MSR access (legacy Pawn folder: PawnIO driver on Windows, ryzen_smu + msr module on Linux).</summary>
public interface ICpuTuning : IDisposable
{
    bool IsSupported { get; }
    bool IsAmd { get; }
    bool IsIntel { get; }
    bool Initialize();

    bool CanSetTDP();
    bool CanSetCoAll();
    bool CanSetThm();
    bool SetAllLimits(int stapmW, int fastW, int slowW);
    int SetCoAll(int value);
    int SetCoPer(int core, int value);
    int SetCoGfx(int value);
    int SetThm(int celsius);
    (int stapmW, int fastW, int slowW)? GetPowerLimits();
    float? GetIntelPackagePower();

    /// <summary>
    /// The kernel driver that backs this service is present (Windows: PawnIO installed even when access is denied;
    /// Linux: ryzen_smu / msr module loaded). Legacy ModeControl.IsPawnInstalled. Defaults to <see cref="IsSupported"/>.
    /// </summary>
    bool IsDriverInstalled => IsSupported;

    /// <summary>
    /// AMD SMU family after <see cref="Initialize"/> ("Raven", "Renoir", "Mobile", "Matisse", "Raphael", "StrixPoint",
    /// "StrixHalo", ...) or null. Legacy ModeControl decides the periodic re-apply from it (Renoir/Mobile temp limit,
    /// Raphael PPT).
    /// </summary>
    string? SmuFamily => null;
}

/// <summary>GPU telemetry backends (NVAPI/NVML/ADL/amdgpu hwmon). IGpuControl is the Core interface from the legacy app.</summary>
public interface IGpuProvider
{
    /// <summary>First usable discrete (else integrated) GPU, null if none.</summary>
    IGpuControl? CreateDiscrete();
    /// <summary>AMD iGPU/APU control (OLED power optimisation, ...). Null if none.</summary>
    IGpuControl? CreateAmdApu();
    /// <summary>Intel dedicated VRAM index helper (legacy IntelVram). False if unavailable.</summary>
    bool InitIntelVram();
    void SetIntelVramIndex(int index);
    void UnloadNative();

    // --- additive (platform-windows): legacy IntelVram state ------------------------------------
    bool IntelVramSupported => false;
    /// <summary>Selectable dedicated-VRAM sizes in GB (legacy IntelVram.Sizes).</summary>
    int[] IntelVramSizes => [];
    /// <summary>Current selection: 0 = automatic, n = IntelVramSizes[n - 1].</summary>
    int IntelVramIndex => 0;
}

public readonly record struct BatteryState(
    bool Present, bool Charging, bool OnAc, double Percent, double? DischargeRateW,
    double? DesignCapacityWh, double? FullChargeCapacityWh, double? Voltage);

/// <summary>CPU/RAM/battery sensors (the OS specific half of legacy HardwareControl).</summary>
public interface ISystemSensors
{
    float? GetCpuTemp();
    float? GetCpuUsage();
    float? GetCpuPower();
    void ResetCpuPowerCounter();
    (int percent, int usedMb)? GetRamInfo();
    BatteryState? GetBatteryState();
    /// <summary>Fan RPM exposed by the OS (hwmon). Null when only the ASUS interface can provide it.</summary>
    int? GetFanRpm(int fanIndex);

    // --- additive (platform-windows): legacy HardwareControl "Energy Meter" channels ---------------

    /// <summary>Whole APU package power in watts from the OS energy meter ("Apu Power" channel), null when unavailable.</summary>
    float? GetApuPower() => null;

    /// <summary>CPU cores power in watts ("CPU Power" channel, else the sum of the per-core RAPL counters), null when unavailable.</summary>
    float? GetCpuCoresPower() => null;
}

public interface IFpsMonitor : IDisposable
{
    void Start(int targetPid = -1);
    void Stop();
    double SampleFps();

    /// <summary>
    /// Additive: foreground process id to count frames for, 0 = nothing / pause, -1 = any. Legacy EtwFpsMonitor.TargetPid.
    /// </summary>
    int TargetPid { get => -1; set { } }
}

public interface IFpsMonitorFactory
{
    bool IsSupported { get; }
    IFpsMonitor? Create();
}
