using Avalonia.Media.Imaging;
using DrawingColor = System.Drawing.Color;

namespace GHelper.App.Backend;

/// <summary>
/// The few toolkit operations <see cref="MainBackend"/> needs besides the view model. Kept behind an interface so the
/// logic can be driven by tests without any window.
/// </summary>
public interface IMainShell
{
    /// <summary>The main window uses this to tell the legacy "60Hz" button text / tooltips the panel's minimum rate.</summary>
    void SetMinRate(int minRate);

    /// <summary>
    /// Open the colour picker. <paramref name="changed"/> is called for every committed colour and with the original
    /// colour again when the dialog is cancelled (legacy RColorPicker.ColorChanged).
    /// </summary>
    void ShowColorPicker(DrawingColor initial, bool allowRandom, Action<DrawingColor> changed);

    /// <summary>True when the side window registered under <paramref name="name"/> is open (WindowRegistry).</summary>
    bool IsSideWindowOpen(string name);

    void CloseSideWindow(string name);

    /// <summary>Icon from the app assets by legacy resource key (<c>icons8_maus_48</c>); null when unknown.</summary>
    Bitmap? LoadIcon(string resourceKey);
}

/// <summary>Shell without a window: every operation is a no-op. Used by tests and before the window exists.</summary>
public sealed class NullMainShell : IMainShell
{
    public static readonly NullMainShell Instance = new();

    public void SetMinRate(int minRate) { }
    public void ShowColorPicker(DrawingColor initial, bool allowRandom, Action<DrawingColor> changed) { }
    public bool IsSideWindowOpen(string name) => false;
    public void CloseSideWindow(string name) { }
    public Bitmap? LoadIcon(string resourceKey) => null;
}
