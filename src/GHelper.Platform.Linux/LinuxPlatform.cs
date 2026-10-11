using GHelper.Platform;

namespace GHelper.Platform.Linux;

/// <summary>
/// STUB created by the scaffold. The platform-linux task replaces every NotImplemented member with the
/// implementation lifted from the legacy app/ tree (see tasks/plan.md).
/// </summary>
public sealed class LinuxPlatform : IPlatform
{
    public PlatformKind Kind => PlatformKind.Linux;
    public ISystemInfo System => throw new NotImplementedException();
    public IAsusBackend Asus => throw new NotImplementedException();
    public IPowerService Power => throw new NotImplementedException();
    public IDisplayService Display => throw new NotImplementedException();
    public IInputService Input => throw new NotImplementedException();
    public IStartupService Startup => throw new NotImplementedException();
    public IAudioService Audio => throw new NotImplementedException();
    public IProcessService Process => throw new NotImplementedException();
    public IVendorServices Vendor => throw new NotImplementedException();
    public ICpuTuning CpuTuning => throw new NotImplementedException();
    public IGpuProvider Gpu => throw new NotImplementedException();
    public ISystemSensors Sensors => throw new NotImplementedException();
    public IFpsMonitorFactory Fps => throw new NotImplementedException();
}
