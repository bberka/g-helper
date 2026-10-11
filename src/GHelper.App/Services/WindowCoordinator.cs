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

        // the fixed size is computed here, once, from the content and clamped to the work area (the content scrolls
        // instead of the window growing past the screen; legacy hid the tips on small screens)
        var client = WindowSizing.Fit(_main);

        // outer size when the platform knows it (title bar + borders), otherwise the client size
        var frame = _main.FrameSize is { } f && f.Height >= client.Height ? f : client;
        var size = new PixelSize((int)Math.Ceiling(frame.Width * scale), (int)Math.Ceiling(frame.Height * scale));

        _main.Position = WindowPlacement.NearTaskbar(screen.Bounds, screen.WorkingArea, size, UiSettings.IsAlly);
    }

    /// <summary>
    /// The main window got a new fixed size while visible (a section appeared / disappeared): keep it inside the work
    /// area (it grows downwards, like the legacy auto sized form, but never past the taskbar) and re-fit and re-align
    /// the side windows, which are as tall as the main window.
    /// </summary>
    internal void OnMainSizeApplied()
    {
        if (!_main.IsVisible) return;

        var screen = _main.Screens.ScreenFromWindow(_main) ?? _main.Screens.Primary;
        if (screen is not null)
        {
            double scale = screen.Scaling;
            var frame = _main.FrameSize ?? new Size(_main.Width, _main.Height);
            int heightPx = (int)Math.Ceiling(frame.Height * scale);

            var area = screen.WorkingArea;
            int maxTop = area.Bottom - WindowPlacement.Margin - heightPx;
            int top = Math.Max(area.Y, Math.Min(_main.Position.Y, maxTop));
            if (top != _main.Position.Y) _main.Position = new PixelPoint(_main.Position.X, top);
        }

        foreach (var side in _side.ToList())
        {
            if (!side.IsVisible) continue;
            WindowSizing.Fit(side);
            PositionSideWindow(side);
        }
    }

    /// <summary>
    /// Port of the FormPosition() of the legacy side forms: line the window up with the main window (same top when it
    /// is smaller, bottom aligned when taller) and put it left of it (right of it when the taskbar is on the left).
    /// Works on the fixed sizes, so it can run before the window is shown.
    /// </summary>
    public void PositionSideWindow(Window side)
    {
        var screen = _main.Screens.ScreenFromWindow(_main) ?? _main.Screens.Primary;
        if (screen is null) return;

        double scale = screen.Scaling;
        int mainTop = _main.Position.Y;
        int mainHeight = (int)(ClientSize(_main).Height * scale);
        var sideSize = ClientSize(side);
        int width = (int)Math.Ceiling(sideSize.Width * scale);
        int height = (int)Math.Ceiling(sideSize.Height * scale);

        // side windows are fitted to at least the main window's height (WindowSizingOptions.MinHeight), so the
        // "smaller" case of the legacy code is the same top and the same height
        int top = height > mainHeight
            ? Math.Max(screen.WorkingArea.Y, mainTop + mainHeight - height)
            : mainTop;

        side.Position = new PixelPoint(GetSideWindowLeft(width), top);
    }

    /// <summary>Left edge (pixels) for a side window of the given pixel width (legacy GetSideWindowLeft).</summary>
    public int GetSideWindowLeft(int widthPx)
    {
        var screen = _main.Screens.ScreenFromWindow(_main) ?? _main.Screens.Primary;
        if (screen is null) return _main.Position.X;

        double scale = screen.Scaling;
        var main = ClientSize(_main);
        var frame = new PixelRect(_main.Position, new PixelSize((int)(main.Width * scale), (int)(main.Height * scale)));
        return WindowPlacement.SideWindowLeft(screen.WorkingArea, frame, widthPx);
    }

    /// <summary>The fixed client size of a window (its layout bounds when it has none yet).</summary>
    private static Size ClientSize(Window w) =>
        new(double.IsNaN(w.Width) ? w.Bounds.Width : w.Width, double.IsNaN(w.Height) ? w.Bounds.Height : w.Height);
}
