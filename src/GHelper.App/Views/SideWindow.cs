using Avalonia.Controls;
using GHelper.App.Services;

namespace GHelper.App.Views;

/// <summary>
/// Base class for the windows that open next to the main window (fans, extra, matrix, updates ...; legacy RForm
/// children owned by SettingsForm). Registers itself with the <see cref="WindowCoordinator"/> so hiding the main window
/// closes it and focus checks include it, and lines itself up beside the main window when it opens.
/// Fixed size (see <see cref="WindowSizing"/>: tiling window managers and Windows snap must not resize it), no taskbar
/// button, follows the "topmost" option.
/// </summary>
public class SideWindow : Window
{
    public SideWindow()
    {
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = UiSettings.Topmost;

        WindowSizing.Attach(this, new WindowSizingOptions
        {
            // legacy FormPosition: at least as tall as the main window
            MinHeight = MainWindowHeight,
            // the content changed size (a section appeared ...): stay lined up with the main window
            Applied = _ => { if (IsVisible) AttachToMain(); },
        });

        Opened += (_, _) => AttachToMain();
    }

    private static double MainWindowHeight() =>
        App.Instance?.Windows?.Main is { IsVisible: true, Height: > 0 } main ? main.Height : 0;

    /// <summary>Position next to the main window; call again after the content size changed.</summary>
    public void AttachToMain()
    {
        var windows = App.Instance?.Windows;
        if (windows is null) return;

        windows.Register(this);
        windows.PositionSideWindow(this);
    }
}
