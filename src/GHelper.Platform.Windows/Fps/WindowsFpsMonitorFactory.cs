namespace GHelper.Platform.Windows.Fps;

/// <summary>ETW based FPS monitor factory. The native structures use 64 bit layouts, so 32 bit processes are unsupported.</summary>
public sealed class WindowsFpsMonitorFactory : IFpsMonitorFactory
{
    public bool IsSupported => OperatingSystem.IsWindows() && Environment.Is64BitProcess;

    public IFpsMonitor? Create()
    {
        if (!IsSupported) return null;
        return new EtwFpsMonitor();
    }
}
