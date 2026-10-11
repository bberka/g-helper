using GHelper.Platform;

namespace GHelper.Platform.Linux;

/// <summary>FPS counters have no portable Linux source (MangoHud / gamescope would have to be integrated).</summary>
public sealed class LinuxFpsMonitorFactory : IFpsMonitorFactory
{
    public bool IsSupported => false;

    public IFpsMonitor? Create() => null;
}

/// <summary>
/// The Linux implementation of every <see cref="IPlatform"/> service, built on the mainline asus-wmi drivers.
///
/// Construction never throws and never touches hardware: every service probes lazily and reports "unsupported" /
/// does nothing on a machine that is not an ASUS laptop (or not Linux at all). All file access goes through
/// <see cref="LinuxContext.Root"/> so the whole platform can be exercised against a fake /sys tree.
/// </summary>
public sealed class LinuxPlatform : IPlatform, IDisposable
{
    private readonly LinuxContext context;
    private readonly Lazy<LinuxAsusBackend> asus;
    private readonly Lazy<LinuxPowerService> power;
    private readonly Lazy<LinuxDisplayService> display;
    private readonly Lazy<LinuxInputService> input;
    private readonly Lazy<LinuxStartupService> startup;
    private readonly Lazy<LinuxAudioService> audio;
    private readonly Lazy<LinuxProcessService> process;
    private readonly Lazy<LinuxVendorServices> vendor;
    private readonly Lazy<LinuxCpuTuning> cpuTuning;
    private readonly Lazy<LinuxGpuProvider> gpu;
    private readonly Lazy<LinuxSystemSensors> sensors;
    private readonly Lazy<LinuxSystemInfo> system;
    private readonly Lazy<LinuxFpsMonitorFactory> fps = new(() => new LinuxFpsMonitorFactory());

    public LinuxPlatform() : this(new LinuxContext())
    {
    }

    public LinuxPlatform(LinuxContext context, LinuxAsusBackendOptions? asusOptions = null)
    {
        this.context = context;

        asus = new(() => new LinuxAsusBackend(context, asusOptions));
        power = new(() => new LinuxPowerService(context, asus.Value.Layout));
        display = new(() => new LinuxDisplayService(context));
        input = new(() => new LinuxInputService(context));
        startup = new(() => new LinuxStartupService(context));
        audio = new(() => new LinuxAudioService(context));
        process = new(() => new LinuxProcessService(context));
        vendor = new(() => new LinuxVendorServices(context, asus.Value.Layout));
        cpuTuning = new(() => new LinuxCpuTuning(context));
        gpu = new(() => new LinuxGpuProvider(context));
        sensors = new(() => new LinuxSystemSensors(context, asus.Value.Layout));
        system = new(() => new LinuxSystemInfo(context, () => display.Value));
    }

    public LinuxContext Context => context;

    public PlatformKind Kind => PlatformKind.Linux;

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

    public void Dispose()
    {
        if (asus.IsValueCreated) asus.Value.Dispose();
        if (power.IsValueCreated) power.Value.Dispose();
        if (input.IsValueCreated) input.Value.Dispose();
        if (audio.IsValueCreated) audio.Value.Dispose();
        if (cpuTuning.IsValueCreated) cpuTuning.Value.Dispose();
        if (gpu.IsValueCreated) gpu.Value.UnloadNative();
    }
}
