using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using GHelper.Overlay;
using GHelper.Platform;

namespace GHelper.App.Views.Overlay;

/// <summary>
/// The hardware overlay (legacy HardwareOverlay form): a borderless, transparent, topmost, click-through, never focused
/// window that draws what <see cref="OverlayModel"/> samples through <see cref="OverlayRenderer"/>.
/// Sampling, game-only visibility and option parsing live in the model; this class owns the window: sizing from the
/// layout, saved position (corner anchor + offsets, per screen), Ctrl+Shift+Alt drag / wheel resize / click to cycle
/// the preset, and the native click-through switch.
/// Create it on the UI thread (<see cref="OverlayHost.Create"/>) and assign it to <c>Program.hardwareOverlay</c>;
/// all <see cref="IHardwareOverlay"/> members may be called from any thread.
/// </summary>
public sealed class HardwareOverlayWindow : Window, IHardwareOverlay, IDisposable
{
    private readonly OverlayModel _model;
    private readonly IOverlayConfigStore _store;
    private readonly OverlayView _view = new();

    private bool _active;
    private bool _dragModeActive;
    private bool _dragging;
    private volatile bool _dragKey;
    private bool _changingScale;
    private PixelPoint _dragCursorStart;
    private PixelPoint _dragWindowStart;
    private OverlayLayoutResult _layout;
    private bool _foregroundHooked;

    public HardwareOverlayWindow() : this(new OverlayModel(), new AppConfigOverlayStore())
    {
    }

    internal HardwareOverlayWindow(OverlayModel model, IOverlayConfigStore store)
    {
        _model = model;
        _store = store;

        Title = "G-Helper overlay";
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        Focusable = false;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _view;

        _layout = _model.Layout;

        _model.Updated += () => OnUi(OnUpdated);
        _model.VisibilityChanged += hidden => OnUi(() => OnVisibilityChanged(hidden));
        _model.SettingsChanged += () => OnUi(OnSettingsChanged);
        _model.ReanchorRequested += () => OnUi(() => { if (_active) RestorePosition(); });

        _view.PointerPressed += OnPointerPressed;
        _view.PointerMoved += OnPointerMoved;
        _view.PointerReleased += OnPointerReleased;
        _view.PointerWheelChanged += OnPointerWheelChanged;

        ScalingChanged += (_, _) => { if (_active) { ApplyLayout(); RestorePosition(); } };
        if (Screens is { } screens)
            screens.Changed += (_, _) => OnUi(() => { if (_active) { ApplyLayout(); RestorePosition(); } });
    }

    // ---- IHardwareOverlay -------------------------------------------------------------------------------

    public void StartOverlay() => OnUi(StartCore);

    public void StopOverlay() => OnUi(StopCore);

    public void RefreshSettings() => OnUi(() =>
    {
        if (!_active) return;
        _model.Refresh(); // raises SettingsChanged: relayout + restore position
    });

    public void SetDragKey(bool down) => OnUi(() =>
    {
        if (down && (!_active || _model.Hidden)) return;
        _dragKey = down;
        if (down != _dragModeActive && !_dragging) ApplyDragMode(down);
    });

    public void SuspendForDisplayOff() => OnUi(() =>
    {
        if (_active) StopCore();
    });

    public void ResumeForDisplayOn() => OnUi(() =>
    {
        if (!_active && ReadEnabled()) StartCore();
    });

    // ---- lifecycle --------------------------------------------------------------------------------------

    private bool ReadEnabled() => _store.GetInt("overlay", 0) == 1;

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private void StartCore()
    {
        _active = true;
        _dragModeActive = false;
        _dragging = false;

        _model.Start();           // options, first sample, "game only" hides through VisibilityChanged
        HookForeground();

        ApplyLayout();
        RestorePosition();
        _view.Frame = OverlayFrame.From(_model, _dragModeActive);

        if (!_model.Hidden) ShowOverlay();
        else HideOverlay();
    }

    private void StopCore()
    {
        _active = false;
        _dragModeActive = false;
        _dragging = false;
        _dragKey = false;
        UnhookForeground();
        _model.Stop();
        HideOverlay();
    }

    private void ShowOverlay()
    {
        if (!IsVisible) Show();
        NativeOverlay.Prepare(this, clickThrough: !_dragModeActive);
    }

    private void HideOverlay()
    {
        if (IsVisible) Hide();
    }

    // The platform may tell about foreground changes (alt-tab); the model also polls once per second.
    private void HookForeground()
    {
        if (_foregroundHooked || !_model.GameOnly) return;
        try
        {
            PlatformServices.Current.Fps.ForegroundChanged += OnForegroundChanged;
            _foregroundHooked = true;
        }
        catch
        {
            // no platform / not supported: polling is enough
        }
    }

    private void UnhookForeground()
    {
        if (!_foregroundHooked) return;
        _foregroundHooked = false;
        try { PlatformServices.Current.Fps.ForegroundChanged -= OnForegroundChanged; } catch { }
    }

    private void OnForegroundChanged() => OnUi(() => { if (_active) _model.OnForegroundChanged(); });

    // ---- model events (UI thread) -----------------------------------------------------------------------

    private void OnUpdated()
    {
        if (!_active) return;

        if (IsVisible)
        {
            DetectDragMode();
            NativeOverlay.KeepOnTop(this);
        }

        var frame = OverlayFrame.From(_model, _dragModeActive);
        if (frame.Layout != _layout)
        {
            ApplyLayout(frame.Layout);
            RestorePosition();
        }
        _view.Frame = frame;
    }

    private void OnSettingsChanged()
    {
        if (!_active || _changingScale) return;
        ApplyLayout();
        RestorePosition();
        _view.Frame = OverlayFrame.From(_model, _dragModeActive);
    }

    private void OnVisibilityChanged(bool hidden)
    {
        if (!_active) return;
        if (hidden)
        {
            HideOverlay();
            return;
        }

        ApplyLayout();
        RestorePosition();
        _view.Frame = OverlayFrame.From(_model, _dragModeActive);
        ShowOverlay();
    }

    // ---- size / position --------------------------------------------------------------------------------

    private double CurrentScaling()
    {
        if (IsVisible) return RenderScaling;
        return TargetScreen()?.Scaling ?? 1.0;
    }

    private void ApplyLayout(OverlayLayoutResult? layout = null)
    {
        _layout = layout ?? _model.Layout;
        var (w, h) = OverlayGeometry.ToDips(new System.Drawing.Size(_layout.Width, _layout.Height), CurrentScaling());
        Width = w;
        Height = h;
    }

    private System.Drawing.Size PixelSize => new(_layout.Width, _layout.Height);

    private Avalonia.Platform.Screen? TargetScreen()
    {
        var screens = Screens;
        if (screens is null) return null;

        string? name = _store.GetString("overlay_screen");
        if (!string.IsNullOrEmpty(name))
            foreach (var s in screens.All)
                if (string.Equals(s.DisplayName, name, StringComparison.OrdinalIgnoreCase)) return s;

        return screens.Primary ?? screens.All.FirstOrDefault();
    }

    private static System.Drawing.Rectangle ToRect(PixelRect r) => new(r.X, r.Y, r.Width, r.Height);

    private void RestorePosition()
    {
        var screen = TargetScreen();
        if (screen is null) return;

        int anchor = _store.GetInt("overlay_anchor", -1);
        int offsetX = _store.GetInt("overlay_offset_x", OverlayLayout.MarginFromEdge);
        int offsetY = _store.GetInt("overlay_offset_y", OverlayLayout.MarginFromEdge);

        // never positioned: primary screen, top left
        var bounds = anchor < 0 && Screens?.Primary is { } primary ? primary.Bounds : screen.Bounds;
        var p = OverlayLayout.RestorePosition(ToRect(bounds), PixelSize, anchor, offsetX, offsetY);
        Position = new PixelPoint(p.X, p.Y);
    }

    private void SavePosition()
    {
        var size = PixelSize;
        var center = new PixelPoint(Position.X + size.Width / 2, Position.Y + size.Height / 2);
        var screen = Screens?.ScreenFromPoint(center) ?? TargetScreen();
        if (screen is null) return;

        var (anchor, offsetX, offsetY) = OverlayLayout.ToAnchor(ToRect(screen.Bounds), new System.Drawing.Point(Position.X, Position.Y), size);
        _store.Set("overlay_screen", screen.IsPrimary ? "" : screen.DisplayName ?? "");
        _store.Set("overlay_anchor", anchor);
        _store.Set("overlay_offset_x", offsetX);
        _store.Set("overlay_offset_y", offsetY);
    }

    // ---- drag mode / pointer ----------------------------------------------------------------------------

    /// <summary>
    /// Tick: engage drag mode only when the cursor already is over the overlay (so Ctrl+Shift+Alt used in a game does not
    /// toggle it). Needs a global probe, otherwise only the hardware drag key (<see cref="SetDragKey"/>) switches it on.
    /// </summary>
    private void DetectDragMode()
    {
        if (_dragging) return;
        bool keys = _dragKey;

        if (!keys && NativeOverlay.TryProbe(this, out var cursor, out bool dragKeys) && dragKeys)
        {
            var rect = new PixelRect(Position, new PixelSize(_layout.Width, _layout.Height));
            keys = rect.Contains(cursor);
        }

        if (keys != _dragModeActive) ApplyDragMode(keys);
    }

    private void ApplyDragMode(bool on)
    {
        _dragModeActive = on;
        NativeOverlay.SetClickThrough(this, !on);
        _view.Cursor = new Cursor(on ? StandardCursorType.Hand : StandardCursorType.Arrow);

        if (_model.BackgroundAlpha < OverlayRenderer.DragMinAlpha)
            _view.Frame = OverlayFrame.From(_model, on);
    }

    private bool AreDragKeysDown(KeyModifiers modifiers)
    {
        if (_dragKey) return true;
        if (NativeOverlay.TryProbe(this, out _, out bool keys)) return keys;
        const KeyModifiers all = KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt;
        return (modifiers & all) == all;
    }

    private PixelPoint ScreenPoint(PointerEventArgs e) => _view.PointToScreen(e.GetPosition(_view));

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(_view).Properties;

        if (props.IsMiddleButtonPressed && AreDragKeysDown(e.KeyModifiers))
        {
            ApplyScale(100);
            e.Handled = true;
            return;
        }

        if (!props.IsLeftButtonPressed || !_dragModeActive) return;

        _dragging = true;
        _dragCursorStart = ScreenPoint(e);
        _dragWindowStart = Position;
        e.Pointer.Capture(_view);
        _view.Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;

        var cursor = ScreenPoint(e);
        var proposed = new System.Drawing.Point(
            _dragWindowStart.X + cursor.X - _dragCursorStart.X,
            _dragWindowStart.Y + cursor.Y - _dragCursorStart.Y);

        var screen = Screens?.ScreenFromPoint(cursor) ?? TargetScreen();
        var p = screen is null ? proposed : OverlayGeometry.ClampDrag(ToRect(screen.Bounds), proposed, PixelSize);
        Position = new PixelPoint(p.X, p.Y);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);

        var up = ScreenPoint(e);
        bool click = OverlayGeometry.IsClick(
            new System.Drawing.Point(_dragCursorStart.X, _dragCursorStart.Y), new System.Drawing.Point(up.X, up.Y));

        if (click)
        {
            // click on the overlay: next preset (Light, Default, Full, Complete)
            Position = _dragWindowStart;
            _model.CycleMode();
        }
        else
        {
            SavePosition();
        }

        if (!_dragKey && !AreDragKeysDown(e.KeyModifiers))
            ApplyDragMode(false);
        else
            _view.Cursor = new Cursor(StandardCursorType.Hand);

        e.Handled = true;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0 || !AreDragKeysDown(e.KeyModifiers)) return;
        ApplyScale(OverlayGeometry.ScaleAfterWheel(_model.ScalePercent, e.Delta.Y > 0 ? 1 : -1));
        e.Handled = true;
    }

    /// <summary>Resize by option percent, keeping the edge nearest to a screen corner fixed, and remember the position.</summary>
    private void ApplyScale(int percent)
    {
        var screen = Screens?.ScreenFromPoint(new PixelPoint(Position.X + _layout.Width / 2, Position.Y + _layout.Height / 2)) ?? TargetScreen();
        var oldSize = PixelSize;
        var oldLocation = new System.Drawing.Point(Position.X, Position.Y);

        _changingScale = true;
        try { _model.ApplyScale(percent); }
        finally { _changingScale = false; }

        ApplyLayout();
        var location = oldLocation;
        if (screen is not null)
            location = OverlayGeometry.KeepNearestEdge(ToRect(screen.Bounds), oldLocation, oldSize, PixelSize);
        Position = new PixelPoint(location.X, location.Y);
        _view.Frame = OverlayFrame.From(_model, _dragModeActive);
        SavePosition();
    }

    // ---- preview (tests, design time) -------------------------------------------------------------------

    /// <summary>Show a prepared frame without sampling anything.</summary>
    internal void Preview(OverlayFrame frame, double renderScaling = 1.0)
    {
        _layout = frame.Layout;
        var (w, h) = OverlayGeometry.ToDips(PixelSize, renderScaling);
        Width = w;
        Height = h;
        _view.Frame = frame;
        if (!IsVisible) Show();
    }

    public void Dispose()
    {
        UnhookForeground();
        _model.Dispose();
    }
}
