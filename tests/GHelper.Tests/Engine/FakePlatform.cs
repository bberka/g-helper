using GHelper.Gpu;
using GHelper.Platform;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using Xunit;

namespace GHelper.Tests.Engine;

/// <summary>Records DeviceSet calls, serves DeviceGet values from a dictionary (missing id = unsupported = -1).</summary>
public sealed class FakeAsusBackend : IAsusBackend
{
    public string Name => "fake";
    public bool IsConnected { get; set; } = true;

    // background tasks of the engine (mode apply, GPU clocks, ...) touch the fake while tests run: keep everything thread safe
    public readonly System.Collections.Concurrent.ConcurrentDictionary<uint, int> Values = new();
    public readonly System.Collections.Concurrent.ConcurrentDictionary<uint, byte[]> Buffers = new();
    private readonly List<(uint id, int value, byte[]? data, string? log)> _sets = new();

    /// <summary>Snapshot of every DeviceSet seen so far.</summary>
    public (uint id, int value, byte[]? data, string? log)[] Sets { get { lock (_sets) return _sets.ToArray(); } }

    public int DeviceGet(uint deviceId) => Values.TryGetValue(deviceId, out var v) ? v : -1;

    public byte[] DeviceGetBuffer(uint deviceId, uint status = 0) =>
        Buffers.TryGetValue(deviceId, out var b) ? (byte[])b.Clone() : new byte[16];

    public byte[] DeviceGetLarge(uint deviceId, int extraIn = 8, int outSize = 64) => new byte[outSize];

    public int DeviceSet(uint deviceId, int status, string? logName)
    {
        lock (_sets) _sets.Add((deviceId, status, null, logName));
        return 1;
    }

    public int DeviceSet(uint deviceId, byte[] parameters, string? logName)
    {
        lock (_sets) _sets.Add((deviceId, 0, (byte[])parameters.Clone(), logName));
        return 1;
    }

    public int DeviceSetAlternative(uint deviceId, int status) => DeviceSet(deviceId, status, "alt");

    public byte[] DeviceInit() => new byte[16];
    public byte[] DeviceWatchDog() => new byte[16];

    public event Action<int>? AtkEvent;
    public void RaiseAtk(int code) => AtkEvent?.Invoke(code);
    public void StartEventListener() { }
    public void Dispose() { }

    public int[] SetValues(uint id) => Sets.Where(s => s.id == id && s.data is null).Select(s => s.value).ToArray();
    public byte[][] SetBuffers(uint id) => Sets.Where(s => s.id == id && s.data is not null).Select(s => s.data!).ToArray();
    public void ClearSets() { lock (_sets) _sets.Clear(); }
}

/// <summary>Minimal lock protected list: enumeration works on a snapshot.</summary>
public sealed class SafeList<T> : IEnumerable<T>
{
    private readonly List<T> _items = new();

    public void Add(T item) { lock (_items) _items.Add(item); }
    public void Clear() { lock (_items) _items.Clear(); }
    public bool Contains(T item) { lock (_items) return _items.Contains(item); }
    public int Count { get { lock (_items) return _items.Count; } }
    public T this[int index] { get { lock (_items) return _items[index]; } }

    public IEnumerator<T> GetEnumerator()
    {
        T[] snapshot;
        lock (_items) snapshot = _items.ToArray();
        return ((IEnumerable<T>)snapshot).GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class FakeSystemInfo : ISystemInfo
{
    public string Model = "ROG Zephyrus G14 GA402RJ_GA402RJ";

    public PlatformKind Kind { get; set; } = PlatformKind.Linux;
    public string OsDescription => "Fake OS";
    public string ConfigDirectory { get; set; } = "";
    public string SharedConfigDirectory { get; set; } = "";
    public string GetModel() => Model;
    public (string Bios, string ModelShort) GetBiosAndModel() => ("GA402RJ.320", "GA402RJ");
    public string GetSerialNumber() => " SN12345 ";
    public string? GetCpuName() => "Fake CPU";
    public bool IsElevated { get; set; }
    public bool IsRunningAsSystem => false;
    public bool? IsDarkTheme() => true;
    public bool IsDesktop => false;
    public bool? IsOledPanel() => false;
}

public sealed class FakePower : IPowerService, IPowerModeCatalog
{
    public readonly SafeList<string> Calls = new();

    public PowerSource Source = PowerSource.Battery;
    public int CpuBoost, Aspm;
    public int LidAction = 1;
    public bool BatterySaver;

    public IReadOnlyDictionary<string, string>? Catalog;
    IReadOnlyDictionary<string, string> IPowerModeCatalog.PowerModes => Catalog!;

    public string GetPowerMode() => "balanced";
    public void SetPowerMode(string scheme) => Calls.Add("SetPowerMode:" + scheme);
    public void SetPowerMode(int mode) => Calls.Add("SetPowerModeInt:" + mode);
    public string GetDefaultPowerMode(int mode) => "mode" + mode;
    public void SetPowerPlan(string scheme) => Calls.Add("SetPowerPlan:" + scheme);
    public int GetCPUBoost() => CpuBoost;
    public void SetCPUBoost(int boost = 0) { CpuBoost = boost; Calls.Add("SetCPUBoost:" + boost); }
    public int GetASPM() => Aspm;
    public void SetASPM(int status = 0) { Aspm = status; Calls.Add("SetASPM:" + status); }
    public void SetBalancedASPM(int status = 0) => Calls.Add("SetBalancedASPM:" + status);
    public void SetConnectivityInStandby(int ac = 0, int dc = 0) => Calls.Add($"SetConnectivityInStandby:{ac},{dc}");
    public int GetLidAction(bool ac) => LidAction;
    public void SetLidAction(int action, bool acOnly = false) { LidAction = action; Calls.Add($"SetLidAction:{action},{acOnly}"); }
    public int GetHibernateAfter() => 0;
    public void SetHibernateAfter(int minutes) => Calls.Add("SetHibernateAfter:" + minutes);
    public bool GetBatterySaverStatus() => BatterySaver;
    public PowerSource GetPowerSource() => Source;
    public bool IsLidClosed() => false;

    public event Action<PowerSource>? PowerSourceChanged;
    public event Action? Suspending;
    public event Action? Resumed;
    public event Action<bool>? LidChanged;
    public event Action<bool>? DisplayStateChanged;
    public event Action<bool>? SessionLockChanged;
    public event Action<bool>? BatterySaverChanged;
    public event Action? UserPreferencesChanged;

    public void Sleep() => Calls.Add("Sleep");
    public void LockWorkstation() => Calls.Add("Lock");

    public void RaiseSource(PowerSource s) => PowerSourceChanged?.Invoke(s);
    public void RaiseSuspending() => Suspending?.Invoke();
    public void RaiseResumed() => Resumed?.Invoke();
    public void RaiseLid(bool closed) => LidChanged?.Invoke(closed);
    public void RaiseDisplay(bool on) => DisplayStateChanged?.Invoke(on);
    public void RaiseSession(bool locked) => SessionLockChanged?.Invoke(locked);
    public void RaiseSaver(bool active) => BatterySaverChanged?.Invoke(active);
    public void RaisePrefs() => UserPreferencesChanged?.Invoke();

    public bool HasSubscribers => PowerSourceChanged is not null || Suspending is not null || Resumed is not null || LidChanged is not null;
}

public sealed class FakeDisplay : IDisplayService, IDisplaySettingsEvents
{
    public bool External;
    public bool? Touch;
    public bool? LastTouchSet;

    public bool IsExternalDisplayConnected(bool log = false) => External;
    public string? FindLaptopScreen(bool log = false) => "eDP-1";
    public int GetMaxRefreshRate(string? screen) => 120;
    public int GetRefreshRate(string? screen) => 60;
    public int SetRefreshRate(string screen, int frequency) => 1;
    public int GetBrightness() => 50;
    public void SetBrightness(int brightness) { }
    public int AdjustBrightness(int delta) => 50 + delta;
    public bool GetHdrStatus(out bool acm, bool log = true) { acm = false; return false; }
    public bool SupportsGammaRamp => false;
    public void SetGammaRamp(string? screen, ushort[] red, ushort[] green, ushort[] blue) { }
    public bool IsColorProfileInstalled(string profileName) => false;
    public Task InstallColorProfile(string profileName, byte[] iccData) => Task.CompletedTask;
    public bool? GetTouchscreenStatus() => Touch;
    public void SetTouchscreenStatus(bool enabled) => LastTouchSet = enabled;

    public event Action? DisplaySettingsChanged;
    public void RaiseSettingsChanged() => DisplaySettingsChanged?.Invoke();
}

public sealed class FakeInput : IInputService
{
    public bool Supported = true;
    public bool Open;
    public int Toggles;

    public void KeyPress(GHelper.Input.Keys key) { }
    public void KeyKeyPress(GHelper.Input.Keys key, GHelper.Input.Keys key2) { }
    public void KeyKeyKeyPress(GHelper.Input.Keys key, GHelper.Input.Keys key2, GHelper.Input.Keys key3, int sleep = 1, int interSleep = 0) { }
    public void KeyKeyKeyKeyPress(GHelper.Input.Keys key, GHelper.Input.Keys key2, GHelper.Input.Keys key3, GHelper.Input.Keys key4, int sleep = 1) { }
    public void MouseWheel(int delta) { }
    public IDisposable RegisterHotkey(HotkeyModifiers modifiers, GHelper.Input.Keys key, Action handler) => new Noop();
    public IDisposable ListenKeys(Action<GHelper.Input.Keys, bool> handler) => new Noop();
    public bool OnScreenKeyboardSupported => Supported;
    public void ToggleOnScreenKeyboard() { Toggles++; Open = !Open; }
    public bool IsOnScreenKeyboardOpen() => Open;
    public void Dispose() { }

    private sealed class Noop : IDisposable { public void Dispose() { } }
}

public sealed class FakeStartup : IStartupService
{
    public readonly SafeList<string> Calls = new();
    public bool IsSupported => true;
    public bool IsScheduled() => false;
    public void Schedule() => Calls.Add("Schedule");
    public void UnSchedule() => Calls.Add("UnSchedule");
    public void ReScheduleAdmin() => Calls.Add("ReScheduleAdmin");
    public void StartupCheck() => Calls.Add("StartupCheck");
    public void ScheduleCharge() => Calls.Add("ScheduleCharge");
    public void UnscheduleCharge() => Calls.Add("UnscheduleCharge");
}

public sealed class FakeAudio : IAudioService
{
    public bool Muted, MicMuted, Throw;
    public bool IsSupported => true;
    public bool ToggleMicMute() { MicMuted = !MicMuted; return MicMuted; }
    public bool IsMicMuted() => Throw ? throw new InvalidOperationException("no device") : MicMuted;
    public bool IsMuted() => Throw ? throw new InvalidOperationException("no device") : Muted;
    public void SubscribeMute(Action<bool> onMuteChange) { if (Throw) throw new InvalidOperationException("no device"); onMuteChange(Muted); }
    public IDisposable? StartLoopbackCapture(Action<float[], int> onSamples) => null;
}

public sealed class FakeProcess : IProcessService
{
    public readonly SafeList<string> Calls = new();
    public int AcquireFailures;
    public int AcquireCalls;
    public Action? SecondInstanceCallback;

    public bool AcquireSingleInstance(string name, Action onSecondInstanceStarted)
    {
        AcquireCalls++;
        SecondInstanceCallback = onSecondInstanceStarted;
        if (AcquireFailures > 0) { AcquireFailures--; return false; }
        return true;
    }

    public void ReleaseSingleInstance() => Calls.Add("Release");
    public void RunAsAdmin(string? param = null, bool force = false) => Calls.Add($"RunAsAdmin:{param}:{force}");
    public void RunAsRestrictedUser(string command) => Calls.Add("Restricted:" + command);
    public void KillByName(string name) => Calls.Add("Kill:" + name);
    public void SetPriority(ProcessPriorityClass priorityClass = ProcessPriorityClass.Normal) => Calls.Add("Priority:" + priorityClass);
    public string RunCommand(string name, string args, string? directory = null, int timeoutMs = 0) { Calls.Add($"Run:{name} {args}"); return "out"; }
    public void OpenUrl(string url) => Calls.Add("OpenUrl:" + url);
    public void OpenFolder(string path) => Calls.Add("OpenFolder:" + path);
    public void TrimMemory() => Calls.Add("Trim");
}

public sealed class FakeVendor : IVendorServices, IDynamicLightingEffects, IAsusChargeLimitSync
{
    public readonly SafeList<string> Calls = new();
    public int RunningCount;
    public bool DynamicSupported, DynamicEnabled;
    public string? GpuError;

    public bool IsAsusOptimizationRunning() => RunningCount > 0;
    public bool IsArmouryRunning() => RunningCount > 0;
    public bool IsOSDRunning() => false;
    public int GetRunningCount() => RunningCount;
    public void StopAsusServices() { Calls.Add("StopAsusServices"); RunningCount = 0; }
    public void StartAsusServices() { Calls.Add("StartAsusServices"); RunningCount = 3; }
    public void StopOnStartup() => Calls.Add("StopOnStartup");
    public void KillSmartDisplayControl() => Calls.Add("KillSmartDisplay");
    public void RunArmouryUninstaller() => Calls.Add("Uninstaller");
    public bool IsDynamicLightingSupported => DynamicSupported;
    public bool IsDynamicLightingEnabled() => DynamicEnabled;
    public void SetDynamicLighting(bool enabled) { Calls.Add("DynamicLighting:" + enabled); DynamicEnabled = enabled; }
    public void OpenDynamicLightingSettings() => Calls.Add("DynamicLightingSettings");
    public bool IsKeystoneEnabled() => false;
    public void SetKeystoneEnabled(bool enabled) => Calls.Add("Keystone:" + enabled);
    public string? GetGpuError() => GpuError;

    public (int effect, Color? c1, Color? c2, int? brightness, int? speed)? LastEffect;
    public void SetEffect(int effect, Color? color, Color? color2, int? brightness, int? speed) => LastEffect = (effect, color, color2, brightness, speed);

    public int? ChargingRate;
    public void SetChargingRate(int value) => ChargingRate = value;
}

public sealed class FakeCpuTuning : ICpuTuning, ICpuFamilyInfo
{
    public bool Supported = true;
    public bool InitOk = true;
    public int InitCalls;
    public readonly SafeList<string> Calls = new();
    public int Result = 1;
    public float? IntelPower;

    public string? CpuFamily { get; set; }
    public string? CpuCodeName { get; set; }

    public bool IsSupported => Supported;
    public bool IsAmd => false;
    public bool IsIntel => true;
    public bool Initialize() { InitCalls++; return InitOk; }
    public bool CanSetTDP() => true;
    public bool CanSetCoAll() => true;
    public bool CanSetThm() => true;
    public bool SetAllLimits(int stapmW, int fastW, int slowW) { Calls.Add($"Limits:{stapmW},{fastW},{slowW}"); return true; }
    public int SetCoAll(int value) { Calls.Add("CoAll:" + value); return Result; }
    public int SetCoPer(int core, int value) { Calls.Add($"CoPer:{core},{value}"); return Result; }
    public int SetCoGfx(int value) { Calls.Add("CoGfx:" + value); return Result; }
    public int SetThm(int celsius) { Calls.Add("Thm:" + celsius); return Result; }
    public (int stapmW, int fastW, int slowW)? GetPowerLimits() => (25, 35, 30);
    public float? GetIntelPackagePower() => IntelPower;
    public void Dispose() { }
}

public sealed class FakeGpuControl : IGpuControl
{
    public bool IsNvidia { get; set; }
    public bool IsValid { get; set; } = true;
    public string FullName { get; set; } = "Fake GPU";
    public int? Temp, Use;
    public float? Power;
    public (long usedMb, long totalMb)? Vram;
    public bool Disposed;
    public bool Killed;

    public int? GetCurrentTemperature() => Temp;
    public int? GetGpuUse() => Use;
    public (long usedMb, long totalMb)? GetVramInfo() => Vram;
    public float? GetGpuPower() => Power;
    public void KillGPUApps() => Killed = true;
    public void Dispose() => Disposed = true;
}

public sealed class FakeGpuProvider : IGpuProvider
{
    public IGpuControl? Discrete;
    public int Unloads;
    public IGpuControl? CreateDiscrete() => Discrete;
    public IGpuControl? CreateAmdApu() => null;
    public bool InitIntelVram() => false;
    public void SetIntelVramIndex(int index) { }
    public void UnloadNative() => Unloads++;
}

public sealed class FakeSensors : ISystemSensors, IApuEnergySensors
{
    public float? CpuTemp, CpuUsage, CpuPower;
    public (int percent, int usedMb)? Ram;
    public BatteryState? Battery;
    public int? FanRpm;
    public int ResetCalls;

    public float? GetCpuTemp() => CpuTemp;
    public float? GetCpuUsage() => CpuUsage;
    public float? GetCpuPower() => CpuPower;
    public void ResetCpuPowerCounter() => ResetCalls++;
    public (int percent, int usedMb)? GetRamInfo() => Ram;
    public BatteryState? GetBatteryState() => Battery;
    public int? GetFanRpm(int fanIndex) => FanRpm;

    public float? ApuPower, CpuChannel, CoresPower;
    public float? SampleApuPower() => ApuPower;
    public float? SampleCpuPower() => CpuChannel;
    public float? SampleCoresPower() => CoresPower;
}

public sealed class FakeFps : IFpsMonitorFactory
{
    public bool IsSupported => false;
    public IFpsMonitor? Create() => null;
}

public sealed class FakeUiHost : IUiHost
{
    public readonly SafeList<string> Refreshes = new();
    public readonly SafeList<(string text, string? icon)> Toasts = new();
    public readonly SafeList<(string name, int arg)> Windows = new();
    public readonly SafeList<string> Alerts = new();
    public bool ConfirmAnswer;
    public int Quits;
    public int Confirms;

    public void Post(Action action) => action();
    public void Toast(string text, string? iconKey = null) => Toasts.Add((text, iconKey));
    public bool Confirm(string title, string text) { Confirms++; return ConfirmAnswer; }
    public void Alert(string title, string text) => Alerts.Add(title + ": " + text);
    public void ToggleSettings(bool checkForFocus = true, bool trayClick = false) { }
    public void Refresh(string area) { lock (Refreshes) Refreshes.Add(area); }
    public void Quit() => Quits++;
    public void ShowWindow(string name, int arg = 0) => Windows.Add((name, arg));
}

/// <summary>Fully fake IPlatform. Also implements the optional capability interfaces on the platform object.</summary>
public sealed class FakePlatform : IPlatform, IDriverInventory, ISessionEndingEvents
{
    public readonly FakeSystemInfo SystemInfo = new();
    public readonly FakeAsusBackend AsusBackend = new();
    public readonly FakePower PowerService = new();
    public readonly FakeDisplay DisplayService = new();
    public readonly FakeInput InputService = new();
    public readonly FakeStartup StartupService = new();
    public readonly FakeAudio AudioService = new();
    public readonly FakeProcess ProcessService = new();
    public readonly FakeVendor VendorService = new();
    public readonly FakeCpuTuning Tuning = new();
    public readonly FakeGpuProvider GpuProvider = new();
    public readonly FakeSensors SensorService = new();
    public readonly FakeFps FpsFactory = new();

    public PlatformKind Kind => SystemInfo.Kind;
    public ISystemInfo System => SystemInfo;
    public IAsusBackend Asus => AsusBackend;
    public IPowerService Power => PowerService;
    public IDisplayService Display => DisplayService;
    public IInputService Input => InputService;
    public IStartupService Startup => StartupService;
    public IAudioService Audio => AudioService;
    public IProcessService Process => ProcessService;
    public IVendorServices Vendor => VendorService;
    public ICpuTuning CpuTuning => Tuning;
    public IGpuProvider Gpu => GpuProvider;
    public ISystemSensors Sensors => SensorService;
    public IFpsMonitorFactory Fps => FpsFactory;

    // IDriverInventory (optional capability)
    public List<InstalledDriver> Installed = new();
    public string? McuVersion, FirmwareVersion;
    public Dictionary<string, string> Staged = new();
    public HashSet<string> Packages = new();

    public List<InstalledDriver> BuildInventory() => Installed;
    public string? GetMcuFirmwareVersion() => McuVersion;
    public string? GetFirmwareVersion(string title) => FirmwareVersion;
    public Dictionary<string, string> BuildStagedVersions(HashSet<string> hardwareIds, CancellationToken token) => Staged;
    public HashSet<string> GetInstalledStorePackages() => Packages;

    // ISessionEndingEvents
    public event Action? SessionEnding;
    public void RaiseSessionEnding() => SessionEnding?.Invoke();
}

/// <summary>
/// The Core engine is made of statics (AppConfig, Program, HardwareControl, PlatformServices ...), so engine tests share one
/// fake platform for the whole test run and serialize through the "Engine" collection.
/// </summary>
public sealed class EngineFixture : IDisposable
{
    public static readonly FakePlatform Platform;
    public static readonly FakeUiHost Ui = new();
    public static readonly string ConfigDir;

    static EngineFixture()
    {
        ConfigDir = Path.Combine(Path.GetTempPath(), "ghelper-engine-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ConfigDir);

        Platform = new FakePlatform();
        Platform.SystemInfo.ConfigDirectory = ConfigDir;
        Platform.SystemInfo.SharedConfigDirectory = Path.Combine(ConfigDir, "shared");

        PlatformServices.Register(Platform);
        Program.Ui = Ui;
        Program.acpi = new AsusACPI(Platform.AsusBackend);
    }

    public EngineFixture()
    {
        // make sure registration happened (static ctor) and start every class from a clean fake state
        Reset();
    }

    public static void Reset()
    {
        // let background work of the previous test (mode apply tasks) finish before the shared fakes are wiped
        try { TestStatics.Get<Task>(typeof(GHelper.Mode.ModeControl), "_modeTask")?.Wait(8000); } catch { }

        // AppConfig is one process wide dictionary: every test starts from an empty config
        lock (TestStatics.Get<object>(typeof(AppConfig), "configLock")!)
            TestStatics.Get<Dictionary<string, object>>(typeof(AppConfig), "config")!.Clear();

        PlatformServices.Register(Platform);
        Program.Ui = Ui;
        Program.acpi = new AsusACPI(Platform.AsusBackend);

        // the model / BIOS strings are cached once per process: re-read them from this fake platform (another suite's
        // platform may have been the first one AppConfig saw)
        typeof(AppConfig).GetMethod("ResetModelCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, null);

        Platform.AsusBackend.Values.Clear();
        Platform.AsusBackend.Buffers.Clear();
        Platform.AsusBackend.ClearSets();
        Platform.AsusBackend.IsConnected = true;
        Platform.PowerService.Calls.Clear();
        Platform.PowerService.Source = PowerSource.Battery;
        Platform.PowerService.Catalog = null;
        Platform.ProcessService.Calls.Clear();
        Platform.ProcessService.AcquireFailures = 0;
        Platform.ProcessService.AcquireCalls = 0;
        Platform.VendorService.Calls.Clear();
        Platform.VendorService.RunningCount = 0;
        Platform.StartupService.Calls.Clear();
        Platform.Tuning.Calls.Clear();
        Platform.Tuning.Supported = true;
        Platform.Tuning.InitOk = true;
        Platform.Tuning.CpuFamily = null;
        Platform.SystemInfo.IsElevated = false;
        Platform.SystemInfo.Kind = PlatformKind.Linux;
        Platform.SensorService.CpuTemp = null;
        Platform.SensorService.CpuUsage = null;
        Platform.SensorService.CpuPower = null;
        Platform.SensorService.Ram = null;
        Platform.SensorService.FanRpm = null;
        Platform.SensorService.Battery = null;
        Platform.GpuProvider.Discrete = null;
        Platform.Installed = new();

        Ui.Refreshes.Clear();
        Ui.Toasts.Clear();
        Ui.Windows.Clear();
        Ui.Alerts.Clear();
        Ui.ConfirmAnswer = false;
        Ui.Quits = 0;
        Ui.Confirms = 0;

        Program.currentSource = Program.PowerSource.Battery;
        Program.usbcProfile = false;
        Program.DetachPlatformEvents();

        TestStatics.Set(typeof(GHelper.Mode.ModeControl), "_smu", null);
    }

    public void Dispose() { }
}


[CollectionDefinition("Engine")]
public class EngineCollection : ICollectionFixture<EngineFixture> { }

/// <summary>Reflection helpers for private static state (Core exposes no test hooks).</summary>
internal static class TestStatics
{
    public static void Set(Type type, string field, object? value)
    {
        var f = type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new MissingFieldException(type.FullName, field);
        f.SetValue(null, value);
    }

    public static T? Get<T>(Type type, string field)
    {
        var f = type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new MissingFieldException(type.FullName, field);
        return (T?)f.GetValue(null);
    }
}
