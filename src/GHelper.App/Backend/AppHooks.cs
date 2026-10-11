using GHelper.Overlay;

namespace GHelper.App.Backend;

/// <summary>
/// Extension points other parts of the application plug into before Core starts.
/// </summary>
public static class AppHooks
{
    /// <summary>
    /// Creates the on-screen hardware overlay (drawing half of <c>IHardwareOverlay</c>). Called once on a worker thread
    /// right before <c>Program.Start</c>, unless <c>Program.hardwareOverlay</c> was assigned already. Null = no overlay.
    /// </summary>
    public static Func<IHardwareOverlay?>? CreateHardwareOverlay { get; set; }
}
