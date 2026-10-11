using GHelper.Overlay;

namespace GHelper.App.Views.Overlay;

/// <summary>
/// Entry point for the wiring code: <c>Program.hardwareOverlay = OverlayHost.Create();</c> on the UI thread, before
/// <c>Program.Start</c>. The window stays hidden until <see cref="IHardwareOverlay.StartOverlay"/>.
/// </summary>
public static class OverlayHost
{
    public static IHardwareOverlay Create() => new HardwareOverlayWindow();
}
