using Xunit;
using System.Reflection;
using GHelper.Gpu;
using GHelper.Platform;
using PawnIO;

namespace GHelper.Tests.Hw;

/// <summary>Interface fake that returns defaults and lets a test override single members by name.</summary>
public class StubProxy : DispatchProxy
{
    public Dictionary<string, Func<object?[]?, object?>> Handlers { get; } = new();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null) return null;
        if (Handlers.TryGetValue(targetMethod.Name, out var handler)) return handler(args);

        var rt = targetMethod.ReturnType;
        if (rt == typeof(void)) return null;
        if (rt == typeof(string)) return "";
        if (rt == typeof(Task)) return Task.CompletedTask;
        if (rt.IsArray) return Array.CreateInstance(rt.GetElementType()!, 0);
        if (rt.IsValueType) return Activator.CreateInstance(rt);
        return null;
    }

    public static T Create<T>(Action<StubProxy>? configure = null) where T : class
    {
        T proxy = DispatchProxy.Create<T, StubProxy>();
        configure?.Invoke((StubProxy)(object)proxy);
        return proxy;
    }
}

public sealed class FakeSystemInfo : ISystemInfo
{
    public string Model = "";
    public PlatformKind Kind => PlatformKind.Linux;
    public string OsDescription => "test";
    public string ConfigDirectory { get; } = Path.Combine(Path.GetTempPath(), "ghelper-tests-hw", Guid.NewGuid().ToString("N"));
    public string SharedConfigDirectory => ConfigDirectory;
    public string GetModel() => Model;
    public (string Bios, string ModelShort) GetBiosAndModel() => ("", Model);
    public string GetSerialNumber() => "";
    public string? CpuName;
    public string? GetCpuName() => CpuName;
    public bool IsElevated { get; set; }
    public bool IsRunningAsSystem => false;
    public bool? IsDarkTheme() => null;
    public bool IsDesktop => false;
    public bool? IsOledPanel() => null;
}

public sealed class FakeProcessService : IProcessService
{
    public readonly List<string> Killed = new();
    public readonly List<(string name, string args)> Commands = new();
    public string RunCommandResult = "";
    public int TrimCount;

    public bool AcquireSingleInstance(string name, Action onSecondInstanceStarted) => true;
    public void ReleaseSingleInstance() { }
    public void RunAsAdmin(string? param = null, bool force = false) { }
    public void RunAsRestrictedUser(string command) { }
    public void KillByName(string name) => Killed.Add(name);
    public void SetPriority(System.Diagnostics.ProcessPriorityClass priorityClass = System.Diagnostics.ProcessPriorityClass.Normal) { }
    public string RunCommand(string name, string args, string? directory = null, int timeoutMs = 0)
    {
        Commands.Add((name, args));
        return RunCommandResult;
    }
    public void OpenUrl(string url) { }
    public void OpenFolder(string path) { }
    public void TrimMemory() => TrimCount++;
}

public sealed class FakeFpsMonitor : IFpsMonitor
{
    public double Fps;
    public int TargetPid { get; set; } = -1;
    public bool Started, Disposed;
    public void Start(int targetPid = -1) => Started = true;
    public void Stop() { }
    public double SampleFps() => Fps;
    public void Dispose() => Disposed = true;
}

public sealed class FakeFpsFactory : IFpsMonitorFactory
{
    public bool IsSupported { get; set; } = true;
    public int ForegroundPid;
    public readonly List<FakeFpsMonitor> Created = new();

    public IFpsMonitor? Create()
    {
        var monitor = new FakeFpsMonitor();
        Created.Add(monitor);
        return monitor;
    }

    public int GetForegroundProcessId() => ForegroundPid;
}

public sealed class FakeGpuProvider : IGpuProvider
{
    public GpuPowerState PowerState = GpuPowerState.Unknown;
    public IAmdGpuDriver? AmdDriver;
    public bool IntelVramAvailable;
    public int[] Sizes = [];
    public int Index;
    public int? LastIntelIndex;

    public IGpuControl? CreateDiscrete() => null;
    public IGpuControl? CreateAmdApu() => null;
    public bool InitIntelVram() => IntelVramAvailable;
    public void SetIntelVramIndex(int index) => LastIntelIndex = index;
    public void UnloadNative() { }
    public int[] IntelVramSizes => Sizes;
    public int IntelVramIndex => Index;
    public GpuPowerState GetDiscretePowerState() => PowerState;
    public IAmdGpuDriver? CreateAmdDriver() => AmdDriver;
}

public sealed class FakeCpuTuning : ICpuTuning
{
    public IPawnIo? Io;
    public bool IsSupported => Io is not null;
    public bool IsAmd => true;
    public bool IsIntel => false;
    public bool Initialize() => true;
    public bool CanSetTDP() => false;
    public bool CanSetCoAll() => false;
    public bool CanSetThm() => false;
    public bool SetAllLimits(int stapmW, int fastW, int slowW) => false;
    public int SetCoAll(int value) => -1;
    public int SetCoPer(int core, int value) => -1;
    public int SetCoGfx(int value) => -1;
    public int SetThm(int celsius) => -1;
    public (int stapmW, int fastW, int slowW)? GetPowerLimits() => null;
    public float? GetIntelPackagePower() => null;
    public void Dispose() { }
    public IPawnIo? CreatePawnIo() => Io;
}

public sealed class FakePlatform : IPlatform
{
    public PlatformKind Kind => PlatformKind.Linux;
    public FakeSystemInfo SystemInfo { get; } = new();
    public FakeProcessService ProcessService { get; } = new();
    public FakeFpsFactory FpsFactory { get; } = new();
    public FakeGpuProvider GpuProvider { get; } = new();
    public FakeCpuTuning Tuning { get; } = new();
    public IDisplayService DisplayService { get; set; } = StubProxy.Create<IDisplayService>();
    public IPowerService PowerService { get; set; } = StubProxy.Create<IPowerService>();

    public ISystemInfo System => SystemInfo;
    public IAsusBackend Asus { get; } = StubProxy.Create<IAsusBackend>();
    public IPowerService Power => PowerService;
    public IDisplayService Display => DisplayService;
    public IInputService Input { get; } = StubProxy.Create<IInputService>();
    public IStartupService Startup { get; } = StubProxy.Create<IStartupService>();
    public IAudioService Audio { get; } = StubProxy.Create<IAudioService>();
    public IProcessService Process => ProcessService;
    public IVendorServices Vendor { get; } = StubProxy.Create<IVendorServices>();
    public ICpuTuning CpuTuning => Tuning;
    public IGpuProvider Gpu => GpuProvider;
    public ISystemSensors Sensors { get; } = StubProxy.Create<ISystemSensors>();
    public IFpsMonitorFactory Fps => FpsFactory;
}

/// <summary>
/// One process-wide fake platform for the hardware tests (AppConfig needs a registered platform). Tests that
/// touch its recorders must be in the "Hw platform" collection so they do not run concurrently.
/// </summary>
public static class HwTestPlatform
{
    public static readonly FakePlatform Instance = Create();

    private static FakePlatform Create()
    {
        var platform = new FakePlatform();
        PlatformServices.Register(platform);
        return platform;
    }

    /// <summary>Make sure our fake is the registered platform (other test classes may have replaced it).</summary>
    public static FakePlatform Ensure()
    {
        var platform = Instance;
        if (!PlatformServices.IsRegistered || !ReferenceEquals(PlatformServices.Current, platform))
            PlatformServices.Register(platform);
        return platform;
    }
}

[CollectionDefinition("Hw platform", DisableParallelization = true)]
public class HwPlatformCollection { }
