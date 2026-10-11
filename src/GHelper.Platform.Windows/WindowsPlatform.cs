using GHelper.Platform.Windows.Asus;
using GHelper.Platform.Windows.Audio;
using GHelper.Platform.Windows.Display;
using GHelper.Platform.Windows.Fps;
using GHelper.Platform.Windows.Gpu;
using GHelper.Platform.Windows.Input;
using GHelper.Platform.Windows.Pawn;
using GHelper.Platform.Windows.Power;
using GHelper.Platform.Windows.Processes;
using GHelper.Platform.Windows.Sensors;
using GHelper.Platform.Windows.Startup;
using GHelper.Platform.Windows.Vendor;

namespace GHelper.Platform.Windows;

/// <summary>
/// Windows implementation of <see cref="IPlatform"/>. Every service is created lazily on first use and does no OS work
/// in its constructor, so this type can be instantiated on any OS (unit tests, cross builds); the Windows-only members
/// of each service are additionally guarded with <c>OperatingSystem.IsWindows()</c>.
/// Register once at start up: <c>PlatformServices.Register(new WindowsPlatform())</c>.
/// </summary>
public sealed class WindowsPlatform : IPlatform, IDisposable
{
    private readonly Lazy<WindowsSystemInfo> system = new(() => new WindowsSystemInfo());
    private readonly Lazy<WindowsAsusBackend> asus = new(() => new WindowsAsusBackend());
    private readonly Lazy<WindowsPowerService> power = new(() => new WindowsPowerService());
    private readonly Lazy<WindowsDisplayService> display;
    private readonly Lazy<WindowsInputService> input = new(() => new WindowsInputService());
    private readonly Lazy<WindowsStartupService> startup;
    private readonly Lazy<WindowsAudioService> audio = new(() => new WindowsAudioService());
    private readonly Lazy<WindowsProcessService> process;
    private readonly Lazy<WindowsVendorServices> vendor;
    private readonly Lazy<WindowsCpuTuning> cpuTuning = new(() => new WindowsCpuTuning());
    private readonly Lazy<WindowsGpuProvider> gpu = new(() => new WindowsGpuProvider());
    private readonly Lazy<WindowsSystemSensors> sensors = new(() => new WindowsSystemSensors());
    private readonly Lazy<WindowsFpsMonitorFactory> fps = new(() => new WindowsFpsMonitorFactory());

    public WindowsPlatform()
    {
        process = new(() => new WindowsProcessService(system.Value));
        display = new(() => new WindowsDisplayService(() => process.Value));
        startup = new(() => new WindowsStartupService(system.Value, () => process.Value));
        vendor = new(() => new WindowsVendorServices(system.Value, process.Value));
    }

    public PlatformKind Kind => PlatformKind.Windows;

    public ISystemInfo System => system.Value;
    public IAsusBackend Asus => asus.Value;
    public IPowerService Power => power.Value;
    public IDisplayService Display => display.Value;
    public IInputService Input => input.Value;
    public IStartupService Startup => startup.Value;
    public IAudioService Audio => audio.Value;
    public IProcessService Process => process.Value;
    public IVendorServices Vendor => vendor.Value;
    public ICpuTuning CpuTuning => cpuTuning.Value;
    public IGpuProvider Gpu => gpu.Value;
    public ISystemSensors Sensors => sensors.Value;
    public IFpsMonitorFactory Fps => fps.Value;

    /// <summary>Stops listeners, hooks and native handles of every service that was created.</summary>
    public void Dispose()
    {
        if (power.IsValueCreated) power.Value.Dispose();
        if (input.IsValueCreated) input.Value.Dispose();
        if (cpuTuning.IsValueCreated) cpuTuning.Value.Dispose();
        if (asus.IsValueCreated) asus.Value.Dispose();
        if (fps.IsValueCreated) fps.Value.Dispose();
        if (process.IsValueCreated) process.Value.ReleaseSingleInstance();
    }
}
