namespace GHelper.Platform;

public enum PlatformKind { Unknown, Windows, Linux }

/// <summary>
/// Everything OS-specific that G-Helper needs. Core logic only ever talks to these interfaces
/// (directly, or through the thin static facades that keep the legacy API: PowerNative, ScreenNative, ...).
/// Implementations live in GHelper.Platform.Windows / GHelper.Platform.Linux and are registered once at startup
/// by the app head via <see cref="PlatformServices.Register"/>.
/// A service the platform cannot provide must still be returned (never null) and report IsSupported == false /
/// do nothing, so Core never needs null checks.
/// </summary>
public interface IPlatform
{
    PlatformKind Kind { get; }
    ISystemInfo System { get; }
    IAsusBackend Asus { get; }
    IPowerService Power { get; }
    IDisplayService Display { get; }
    IInputService Input { get; }
    IStartupService Startup { get; }
    IAudioService Audio { get; }
    IProcessService Process { get; }
    IVendorServices Vendor { get; }
    ICpuTuning CpuTuning { get; }
    IGpuProvider Gpu { get; }
    ISystemSensors Sensors { get; }
    IFpsMonitorFactory Fps { get; }
}

public static class PlatformServices
{
    private static IPlatform? _current;

    public static IPlatform Current => _current ?? throw new InvalidOperationException("Platform services were not registered");
    public static bool IsRegistered => _current is not null;

    public static void Register(IPlatform platform) => _current = platform;
}
