using Avalonia.Controls;
using GHelper.App.Services;

namespace GHelper.App.Views;

/// <summary>
/// Base class for the windows that open next to the main window (fans, extra, matrix, updates ...; legacy RForm
/// children owned by SettingsForm). Registers itself with the <see cref="WindowCoordinator"/> so hiding the main window
/// closes it and focus checks include it, and lines itself up beside the main window when it opens.
/// Fixed size, no taskbar button, follows the "topmost" option.
/// </summary>
public class SideWindow : Window
{
    public SideWindow()
    {
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = UiSettings.Topmost;
        Opened += (_, _) => AttachToMain();
    }

    /// <summary>Position next to the main window; call again after the content size changed.</summary>
    public void AttachToMain()
    {
        var windows = App.Instance?.Windows;
        if (windows is null) return;

        windows.Register(this);
        windows.PositionSideWindow(this);
    }
}
