using Avalonia;
using Avalonia.Controls;
using GHelper.App.Views;

namespace GHelper.App.Services;

/// <summary>
/// Owns show / hide / focus / placement of the main window and the side windows that belong to it
/// (legacy Program.SettingsToggle + SettingsForm.HideAll / ShowAll / HasAnyFocus / PositionNearTaskbar /
/// GetSideWindowLeft). Side windows (fans, extra, matrix ...) register themselves with <see cref="Register"/>.
/// </summary>
public sealed class WindowCoordinator
{
    private readonly MainWindow _main;
    private readonly List<Window> _side = new();
    private long _lastLostFocus;

    /// <summary>Called after the windows were hidden (legacy: MemoryHelper.TrimAfter).</summary>
    public event Action? Hidden;

    /// <summary>Called whenever the main window became visible (legacy VisibleChanged: sensors timer, update check).</summary>
    public event Action? Shown;

    public WindowCoordinator(MainWindow main)
    {
        _main = main;
        _main.Deactivated += (_, _) => _lastLostFocus = Environment.TickCount64;
    }

    public MainWindow Main => _main;

    public bool IsVisible => _main.IsVisible;

    public IReadOnlyList<Window> SideWindows => _side;

    /// <summary>Track a window so HideAll closes it and focus checks include it.</summary>
    public void Register(Window window)
    {
        if (_side.Contains(window)) return;
        _side.Add(window);
        window.Closed += (_, _) => _side.Remove(window);
    }

    /// <summary>Port of HasAnyFocus: the main window, a side window, or (tray click) a focus loss within 300 ms.</summary>
    public bool HasAnyFocus(bool lostFocusCheck = false)
    {
        if (_main.IsActive) return true;
        if (_side.Any(w => w.IsActive)) return true;
        return lostFocusCheck && Math.Abs(Environment.TickCount64 - _lastLostFocus) < 300;
    }

    /// <summary>Port of Program.SettingsToggle.</summary>
    public void Toggle(bool checkForFocus = true, bool trayClick = false)
    {
        if (_main.IsVisible)
        {
            // If the window is not on top this just focuses it; pressing the tray icon again hides it.
            if (checkForFocus && !HasAnyFocus(trayClick) && !UiSettings.Topmost)
                ShowAll();
            else
                HideAll();
            return;
        }

        _main.WindowState = WindowState.Normal;
        PositionNearTaskbar();
        _main.Show();
        ShowAll();
        PositionNearTaskbar();
        Shown?.Invoke();
    }

    /// <summary>Closes all side windows and hides the main one.</summary>
    public void HideAll()
    {
        foreach (var w in _side.ToList()) w.Close();
        _side.Clear();
        _main.Hide();
        Hidden?.Invoke();
    }

    /// <summary>Brings the main window to the front with focus (legacy ShowAll).</summary>
    public void ShowAll()
    {
        _main.Topmost = true; // jump above everything, then fall back to the configured state
        _main.Activate();
        _main.Topmost = UiSettings.Topmost;
    }

    public void PositionNearTaskbar()
    {
        var screen = _main.Screens.Primary ?? _main.Screens.ScreenFromWindow(_main) ?? _main.Screens.All.FirstOrDefault();
        if (screen is null) return;

        double scale = screen.Scaling;
        _main.Measure(new Size(_main.Width, double.PositiveInfinity));
        double h = _main.IsVisible && _main.Bounds.Height > 0 ? _main.Bounds.Height : _main.DesiredSize.Height;
        var size = new PixelSize((int)Math.Ceiling(_main.Width * scale), (int)Math.Ceiling(h * scale));

        _main.Position = WindowPlacement.NearTaskbar(screen.Bounds, screen.WorkingArea, size, UiSettings.IsAlly);
    }

    /// <summary>Left edge (pixels) for a side window of the given pixel width (legacy GetSideWindowLeft).</summary>
    public int GetSideWindowLeft(int widthPx)
    {
        var screen = _main.Screens.ScreenFromWindow(_main) ?? _main.Screens.Primary;
        if (screen is null) return _main.Position.X;

        double scale = screen.Scaling;
        var frame = new PixelRect(_main.Position, new PixelSize((int)(_main.Bounds.Width * scale), (int)(_main.Bounds.Height * scale)));
        return WindowPlacement.SideWindowLeft(screen.WorkingArea, frame, widthPx);
    }
}
