using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace GHelper.App.Services;

/// <summary>Per window knobs of <see cref="WindowSizing.Attach"/>.</summary>
public sealed class WindowSizingOptions
{
    /// <summary>Lower bound of the fitted height, evaluated on every fit (side windows: as tall as the main window).</summary>
    public Func<double>? MinHeight { get; init; }

    /// <summary>Called (UI thread) after a new fixed size was applied, with the client size in DIPs.</summary>
    public Action<Size>? Applied { get; init; }
}

/// <summary>
/// One policy for the size of every window of the app: fixed, not resizable, not maximizable, not minimizable.
/// <para>
/// Why: <c>SizeToContent</c> makes the toolkit resize the native window after it was shown, and tiling window managers
/// (i3, sway, Hyprland, bspwm, awesome, KDE / Gnome tiling extensions) and Windows snap (Win+Arrow) then fight over the
/// size. Instead the size is computed once from the content (clamped to the work area) and applied as
/// <c>Width = MinWidth = MaxWidth</c> and <c>Height = MinHeight = MaxHeight</c>, so the X11 WM_NORMAL_HINTS carry
/// min == max (tilers float such windows) and Windows has no sizing frame / maximize / minimize box.
/// </para>
/// <para>
/// The size is recomputed only when the content legitimately changes size (a section appears, a tab is switched): the
/// window then moves to the new fixed size once. Code that assigns <c>Width</c> / <c>Height</c> keeps working (the
/// min / max follow); code that tries to change min / max / SizeToContent / CanResize / maximize is reverted.
/// </para>
/// Use <see cref="Attach"/> from a window's constructor; <see cref="Install"/> applies the same policy to any other
/// window (colour picker, toast, dialogs) the moment it becomes visible.
/// </summary>
public static class WindowSizing
{
    private sealed class State
    {
        public Window Window = null!;
        public bool Attached;
        public bool Locked;
        public bool Applying;
        public bool AutoWidth;
        public bool AutoHeight;
        public bool RefitQueued;
        public bool Refitting;
        public long BurstStart;
        public int BurstCount;
        public double BaseMinWidth, BaseMinHeight;
        public double BaseMaxWidth = double.PositiveInfinity, BaseMaxHeight = double.PositiveInfinity;
        public Size Size;
        public WindowSizingOptions Options = new();
    }

    private static readonly ConditionalWeakTable<Window, State> States = new();
    private static bool _installed;

    /// <summary>Margin kept free around a window that had to be shrunk to the work area (DIPs are px / scale).</summary>
    private const double ScreenMargin = WindowPlacement.Margin;

    /// <summary>Title bar + borders assumed before the platform reported the real frame (decorated windows).</summary>
    private const double AssumedFrameHeight = 40;

    /// <summary>Switch the policy off (diagnostics / comparing against the toolkit's own SizeToContent).</summary>
    internal static bool Enabled { get; set; } = true;

    /// <summary>
    /// Apply the policy to every window of the application when it becomes visible. Idempotent. The visibility change
    /// happens before the toolkit lays the window out and creates the native frame, so there is no resize after show.
    /// </summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;

        Visual.IsVisibleProperty.Changed.AddClassHandler<Window>((window, e) =>
        {
            if (e.NewValue is true && !IsToolkitInternal(window)) Attach(window);
        });
    }

    /// <summary>
    /// Windows the toolkit creates for itself (the Windows tray icon menu popup, ...) size and position themselves and
    /// must not be touched: locking the size of Avalonia's TrayPopupRoot made the tray context menu unusable.
    /// A plain <see cref="Window"/> or any subclass from this application is ours.
    /// </summary>
    internal static bool IsToolkitInternal(Window window) => IsToolkitInternal(window.GetType());

    internal static bool IsToolkitInternal(Type type)
    {
        if (type == typeof(Window)) return false;

        string? assembly = type.Assembly.GetName().Name;
        return assembly is not null && assembly.StartsWith("Avalonia.", StringComparison.Ordinal);
    }

    /// <summary>Whether <paramref name="window"/> currently has its size fixed by this policy.</summary>
    public static bool IsFixed(Window window) => States.TryGetValue(window, out var s) && s.Locked;

    /// <summary>
    /// Put <paramref name="window"/> under the policy (idempotent). Static window properties are set now, the size is
    /// computed when the window becomes visible (or immediately if it already is).
    /// </summary>
    public static void Attach(Window window, WindowSizingOptions? options = null)
    {
        if (!Enabled) return;

        var s = States.GetValue(window, w => new State { Window = w });
        if (options is not null) s.Options = options;
        if (s.Attached)
        {
            if (window.IsVisible && !s.Locked) Fit(window);
            return;
        }

        s.Attached = true;
        LockStaticProperties(window);

        window.PropertyChanged += (_, e) => OnPropertyChanged(s, e);
        window.LayoutUpdated += (_, _) => OnLayoutUpdated(s);

        if (window.IsVisible) Fit(window);
    }

    /// <summary>
    /// Compute the size from the content now (clamped to the work area) and apply it as the fixed size. Returns the
    /// client size in DIPs. Safe to call repeatedly; used when the content size changed or before positioning.
    /// </summary>
    public static Size Fit(Window window)
    {
        if (!Enabled) return new Size(window.Width, window.Height);

        var s = States.GetValue(window, w => new State { Window = w });
        if (!s.Attached) Attach(window);
        return Refit(s);
    }

    /// <summary>Apply an explicit client size (DIPs) as the fixed size of the window.</summary>
    public static void Fix(Window window, double width, double height)
    {
        var s = States.GetValue(window, w => new State { Window = w });
        if (!s.Attached) Attach(window);
        s.AutoWidth = s.AutoHeight = false;
        Apply(s, width, height);
    }

    // ---- sizing --------------------------------------------------------------------------------------------

    private static void LockStaticProperties(Window w)
    {
        w.CanResize = false;
        w.CanMaximize = false;
        w.CanMinimize = false;
        if (w.WindowState is WindowState.Maximized or WindowState.FullScreen) w.WindowState = WindowState.Normal;
    }

    private static Size Refit(State s)
    {
        // layout passes caused by the probe below are not content changes
        bool was = s.Refitting;
        s.Refitting = true;
        try { return RefitCore(s); }
        finally { s.Refitting = was; }
    }

    private static Size RefitCore(State s)
    {
        var w = s.Window;

        if (!s.Locked)
        {
            s.BaseMinWidth = Finite(w.MinWidth);
            s.BaseMinHeight = Finite(w.MinHeight);
            s.BaseMaxWidth = w.MaxWidth;
            s.BaseMaxHeight = w.MaxHeight;
            s.AutoWidth = double.IsNaN(w.Width) || w.SizeToContent.HasFlag(SizeToContent.Width);
            s.AutoHeight = double.IsNaN(w.Height) || w.SizeToContent.HasFlag(SizeToContent.Height);
        }

        double width = w.Width, height = w.Height;
        Control? probe = null;
        Size previousMeasure = default;

        if ((s.AutoWidth || s.AutoHeight) && w.Content is Control content)
        {
            w.ApplyTemplate();

            var pad = w.Padding;
            double padW = pad.Left + pad.Right, padH = pad.Top + pad.Bottom;
            double constraintW = s.AutoWidth ? double.PositiveInfinity : Math.Max(0, width - padW);

            // the natural size of the content: measured against an unconstrained height (what SizeToContent does)
            // (what the layout pass hands the content: the client area minus the window padding)
            probe = content;
            previousMeasure = new Size(Math.Max(0, w.ClientSize.Width - padW), Math.Max(0, w.ClientSize.Height - padH));
            content.Measure(new Size(constraintW, double.PositiveInfinity));

            if (s.AutoWidth) width = content.DesiredSize.Width + padW;
            if (s.AutoHeight) height = content.DesiredSize.Height + padH;
        }

        if (double.IsNaN(width) || double.IsInfinity(width)) width = Math.Max(s.BaseMinWidth, 300);
        if (double.IsNaN(height) || double.IsInfinity(height)) height = Math.Max(s.BaseMinHeight, 200);

        width = Math.Max(width, s.BaseMinWidth);
        height = Math.Max(height, s.BaseMinHeight);
        if (s.Options.MinHeight is { } minHeight) height = Math.Max(height, Finite(minHeight()));

        // never bigger than the work area (the content scrolls / is cropped instead) or than the window's own limits
        var available = AvailableSize(w);
        width = Math.Min(Math.Min(width, s.BaseMaxWidth), available.Width);
        height = Math.Min(Math.Min(height, s.BaseMaxHeight), available.Height);

        double scale = Scaling(w);
        width = Math.Ceiling(width * scale - 1e-6) / scale;
        height = Math.Ceiling(height * scale - 1e-6) / scale;

        bool changed = !s.Locked || Math.Abs(width - s.Size.Width) > 0.01 || Math.Abs(height - s.Size.Height) > 0.01;

        if (changed)
        {
            Apply(s, width, height);
        }
        else if (probe is not null && previousMeasure.Width > 0 && previousMeasure.Height > 0)
        {
            // unchanged: put the content's measure cache back to what the layout pass left, so probing never perturbs
            // the real layout. Moving the content's desired size back and forth asks for a layout pass: run it now,
            // while Refitting is set, so its LayoutUpdated is not mistaken for a content change (that would loop).
            probe.Measure(previousMeasure);
            if (w.IsVisible) w.UpdateLayout();
        }

        return s.Size;
    }

    private static void Apply(State s, double width, double height)
    {
        var w = s.Window;
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        s.Applying = true;
        try
        {
            w.SizeToContent = SizeToContent.Manual;
            LockStaticProperties(w);
            SetAxis(w, Layout.Width, width);
            SetAxis(w, Layout.Height, height);
        }
        finally
        {
            s.Applying = false;
        }

        s.Locked = true;
        s.Size = new Size(width, height);

        s.Options.Applied?.Invoke(s.Size);
    }

    private enum Layout
    {
        Width,
        Height,
    }

    /// <summary>Value, minimum and maximum of one axis to the same number, never with min above max in between.</summary>
    private static void SetAxis(Window w, Layout axis, double value)
    {
        if (axis == Layout.Width)
        {
            if (value >= w.MaxWidth) { w.MaxWidth = value; w.MinWidth = value; }
            else { w.MinWidth = value; w.MaxWidth = value; }
            w.Width = value;
        }
        else
        {
            if (value >= w.MaxHeight) { w.MaxHeight = value; w.MinHeight = value; }
            else { w.MinHeight = value; w.MaxHeight = value; }
            w.Height = value;
        }
    }

    private static double Finite(double v) => double.IsNaN(v) || double.IsInfinity(v) ? 0 : v;

    private static double Scaling(Window w)
    {
        double scale = w.RenderScaling;
        return scale > 0 && !double.IsNaN(scale) ? scale : 1;
    }

    /// <summary>Largest client size (DIPs) that still fits the work area of the screen the window opens on.</summary>
    internal static Size AvailableSize(Window w)
    {
        var main = App.Instance?.Windows?.Main;
        var screen = (main is { IsVisible: true } && !ReferenceEquals(main, w) ? main.Screens.ScreenFromWindow(main) : null)
                     ?? w.Screens.ScreenFromWindow(w)
                     ?? w.Screens.Primary
                     ?? w.Screens.All.FirstOrDefault();
        if (screen is null) return new Size(double.PositiveInfinity, double.PositiveInfinity);

        double scale = screen.Scaling > 0 ? screen.Scaling : 1;
        var area = screen.WorkingArea;
        var frame = FrameExtra(w);

        double width = area.Width / scale - 2 * ScreenMargin / scale - frame.Width;
        double height = area.Height / scale - 2 * ScreenMargin / scale - frame.Height;
        return new Size(Math.Max(300, width), Math.Max(300, height));
    }

    /// <summary>What the window manager adds around the client area (title bar, borders).</summary>
    private static Size FrameExtra(Window w)
    {
        if (w.FrameSize is { } frame && w.ClientSize.Width > 0 && w.ClientSize.Height > 0)
            return new Size(Math.Max(0, frame.Width - w.ClientSize.Width), Math.Max(0, frame.Height - w.ClientSize.Height));

        return w.WindowDecorations == WindowDecorations.None ? default : new Size(0, AssumedFrameHeight);
    }

    // ---- keeping it fixed ----------------------------------------------------------------------------------

    private static void OnPropertyChanged(State s, AvaloniaPropertyChangedEventArgs e)
    {
        var w = s.Window;

        if (e.Property == Visual.IsVisibleProperty)
        {
            if (e.NewValue is true && !s.Locked) Fit(w);
            return;
        }

        if (!s.Locked || s.Applying) return;

        if (e.Property == Layoutable.WidthProperty)
        {
            // code that sizes the window itself (toast text, overlay layout): the limits follow the new value
            if (e.NewValue is double v && !double.IsNaN(v) && Math.Abs(v - s.Size.Width) > 0.01)
            {
                s.AutoWidth = false;
                Apply(s, v, s.Size.Height);
            }
            else if (e.NewValue is double nan && double.IsNaN(nan))
            {
                s.AutoWidth = true;
                QueueRefit(s);
            }
        }
        else if (e.Property == Layoutable.HeightProperty)
        {
            if (e.NewValue is double v && !double.IsNaN(v) && Math.Abs(v - s.Size.Height) > 0.01)
            {
                s.AutoHeight = false;
                Apply(s, s.Size.Width, v);
            }
            else if (e.NewValue is double nan && double.IsNaN(nan))
            {
                s.AutoHeight = true;
                QueueRefit(s);
            }
        }
        else if (e.Property == Layoutable.MinWidthProperty || e.Property == Layoutable.MaxWidthProperty ||
                 e.Property == Layoutable.MinHeightProperty || e.Property == Layoutable.MaxHeightProperty)
        {
            // somebody else tried to loosen the limits (screen height caps set after open ...): keep the fixed size
            if (!Matches(w, s.Size)) Apply(s, s.Size.Width, s.Size.Height);
        }
        else if (e.Property == Window.SizeToContentProperty)
        {
            if (e.NewValue is SizeToContent stc && stc != SizeToContent.Manual)
            {
                // "size to content again": a request to fit, performed as a single fixed size change
                s.AutoWidth |= stc.HasFlag(SizeToContent.Width);
                s.AutoHeight |= stc.HasFlag(SizeToContent.Height);
                s.Applying = true;
                try { w.SizeToContent = SizeToContent.Manual; }
                finally { s.Applying = false; }
                QueueRefit(s);
            }
        }
        else if (e.Property == Window.CanResizeProperty || e.Property == Window.CanMaximizeProperty ||
                 e.Property == Window.CanMinimizeProperty)
        {
            if (e.NewValue is true) LockStaticProperties(w);
        }
        else if (e.Property == Window.WindowStateProperty)
        {
            if (e.NewValue is WindowState.Maximized or WindowState.FullScreen) w.WindowState = WindowState.Normal;
        }
    }

    private static bool Matches(Window w, Size size) =>
        w.MinWidth == size.Width && w.MaxWidth == size.Width && w.MinHeight == size.Height && w.MaxHeight == size.Height;

    private static void OnLayoutUpdated(State s)
    {
        if (!s.Locked || s.Applying || s.Refitting) return;

        if (!s.AutoWidth && !s.AutoHeight) return;
        QueueRefit(s);
    }

    private static void QueueRefit(State s)
    {
        if (s.RefitQueued) return;
        s.RefitQueued = true;

        Dispatcher.UIThread.Post(() =>
        {
            s.RefitQueued = false;
            if (!s.Window.IsVisible) return; // closed / hidden in the meantime
            if (s.Applying) return;

            // safety net against a layout that never settles: at most 20 re-fits per second per window
            long now = Environment.TickCount64;
            if (now - s.BurstStart > 1000) { s.BurstStart = now; s.BurstCount = 0; }
            if (++s.BurstCount > 20) return;

            try { Refit(s); }
            catch (Exception ex) { AppLog.Write("WindowSizing: " + ex.Message); }
        }, DispatcherPriority.Loaded);
    }
}
