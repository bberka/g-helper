using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace GHelper.App.Controls;

/// <summary>
/// Custom drawn slider (port of app/UI/Slider.cs): thin bar, round thumb that grows on hover/press, optional
/// list of supported values (tick marks + keyboard jumping), exponential and vertical variants.
/// Derives from RangeBase so UI Automation gets a range-value peer; set AutomationProperties.Name for the
/// accessible name (e.g. "Battery Charge Limit: 80%").
/// </summary>
public class RSlider : RangeBase
{
    private const double InnerNormal = 12.0 / 22.0;
    private const double InnerHover = 16.0 / 22.0;
    private const double InnerPressed = 10.0 / 22.0;

    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<RSlider, double>(nameof(Step), 1);

    public static readonly StyledProperty<bool> ExponentialProperty =
        AvaloniaProperty.Register<RSlider, bool>(nameof(Exponential));

    public static readonly StyledProperty<bool> VerticalProperty =
        AvaloniaProperty.Register<RSlider, bool>(nameof(Vertical));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<RSlider, IBrush?>(nameof(AccentBrush));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<RSlider, IBrush?>(nameof(TrackBrush));

    /// <summary>Colour of the thumb ring (legacy: buttonMain).</summary>
    public static readonly StyledProperty<IBrush?> ThumbBrushProperty =
        AvaloniaProperty.Register<RSlider, IBrush?>(nameof(ThumbBrush));

    public static readonly StyledProperty<IBrush?> TickBrushProperty =
        AvaloniaProperty.Register<RSlider, IBrush?>(nameof(TickBrush));

    private double _innerScale = InnerNormal;
    private double _innerTarget = InnerNormal;
    private double _tickAlpha;
    private double _tickTarget;
    private DispatcherTimer? _timer;
    private double? _dragPos;
    private bool _moving;
    private bool _snapping;

    static RSlider()
    {
        FocusableProperty.OverrideDefaultValue<RSlider>(true);
        AffectsRender<RSlider>(ValueProperty, MinimumProperty, MaximumProperty, VerticalProperty, ExponentialProperty,
            AccentBrushProperty, TrackBrushProperty, ThumbBrushProperty, TickBrushProperty);
    }

    public RSlider()
    {
        Minimum = 0;
        Maximum = 100;
        Value = 50;
        Height = 20;
    }

    public double Step { get => GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public bool Exponential { get => GetValue(ExponentialProperty); set => SetValue(ExponentialProperty, value); }
    public bool Vertical { get => GetValue(VerticalProperty); set => SetValue(VerticalProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? ThumbBrush { get => GetValue(ThumbBrushProperty); set => SetValue(ThumbBrushProperty, value); }
    public IBrush? TickBrush { get => GetValue(TickBrushProperty); set => SetValue(TickBrushProperty, value); }

    /// <summary>When not empty only these values can be selected (legacy Slider.supportedValues, e.g. 60,65..80,100).</summary>
    public IList<int> SupportedValues { get; set; } = new List<int>();

    /// <summary>Raised when the user finished a drag or key press (legacy MouseUp/KeyUp hooks used to apply the value).</summary>
    public event EventHandler? ValueCommitted;

    protected override AutomationPeer OnCreateAutomationPeer() => new RSliderAutomationPeer(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Round to Step like the legacy setter.
        if (change.Property == ValueProperty && !_snapping && Step > 0)
        {
            double v = Value;
            double snapped = Math.Round(v / Step) * Step;
            snapped = Math.Clamp(snapped, Minimum, Maximum);
            if (Math.Abs(snapped - v) > 1e-9)
            {
                _snapping = true;
                try { SetCurrentValue(ValueProperty, snapped); }
                finally { _snapping = false; }
            }
        }
    }

    private (double radius, Rect bar) Geometry()
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (Vertical)
        {
            double r = 0.4 * w;
            double bw = w * 0.15;
            return (r, new Rect((w - bw) / 2, r, bw, Math.Max(0, h - 2 * r)));
        }
        else
        {
            double r = 0.4 * h;
            double bh = h * 0.15;
            return (r, new Rect(r, (h - bh) / 2, Math.Max(0, w - 2 * r), bh));
        }
    }

    private double Fraction(double value)
    {
        double min = Minimum, max = Maximum;
        if (max <= min) return 0;
        double f = Exponential && min > 0
            ? Math.Log(value / min) / Math.Log(max / min)
            : (value - min) / (max - min);
        return Math.Clamp(f, 0, 1);
    }

    public override void Render(DrawingContext ctx)
    {
        var (radius, bar) = Geometry();
        var track = TrackBrush ?? Brushes.Gray;
        var accent = AccentBrush ?? Brushes.DodgerBlue;
        var thumb = ThumbBrush ?? Brushes.White;

        // transparent hit area
        ctx.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));

        double frac = Fraction(Value);

        if (Vertical)
        {
            double thumbY = _dragPos ?? (bar.Y + bar.Height * (1 - frac));
            double cx = bar.X + bar.Width / 2;
            ctx.DrawRectangle(track, null, bar);
            ctx.DrawRectangle(accent, null, new Rect(bar.X, thumbY, bar.Width, Math.Max(0, bar.Bottom - thumbY)));
            ctx.DrawEllipse(thumb, null, new Point(cx, thumbY), radius, radius);
            ctx.DrawEllipse(accent, null, new Point(cx, thumbY), _innerScale * radius, _innerScale * radius);
            return;
        }

        double thumbX = _dragPos ?? (bar.X + bar.Width * frac);
        double cy = bar.Y + bar.Height / 2;

        ctx.DrawRectangle(track, null, bar);
        ctx.DrawRectangle(accent, null, new Rect(bar.X, bar.Y, Math.Max(0, thumbX - bar.X), bar.Height));

        if (_tickAlpha >= 1 && SupportedValues.Count > 0)
        {
            Color c = ChromeHelper.ColorOf(TickBrush, Colors.Gray);
            var mark = new SolidColorBrush(ChromeHelper.WithAlpha(c, _tickAlpha));
            double tickW = Math.Max(1, bar.Height / 4);
            double tickH = 0.75 * bar.Height;
            double gap = 0.5 * bar.Height;
            foreach (int value in SupportedValues)
            {
                double x = bar.X + bar.Width * Fraction(value) - tickW / 2;
                ctx.DrawRectangle(mark, null, new Rect(x, bar.Y - gap - tickH, tickW, tickH));
                ctx.DrawRectangle(mark, null, new Rect(x, bar.Bottom + gap, tickW, tickH));
            }
        }

        ctx.DrawEllipse(thumb, null, new Point(thumbX, cy), radius, radius);
        ctx.DrawEllipse(accent, null, new Point(thumbX, cy), _innerScale * radius, _innerScale * radius);
    }

    private void Animate(double target)
    {
        _innerTarget = target;
        _tickTarget = target == InnerNormal ? 0 : 120;
        _timer ??= new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _innerScale += (_innerTarget - _innerScale) * 0.3;
        _tickAlpha += (_tickTarget - _tickAlpha) * 0.3;
        if (Math.Abs(_innerTarget - _innerScale) < 0.01 && Math.Abs(_tickTarget - _tickAlpha) < 1)
        {
            _innerScale = _innerTarget;
            _tickAlpha = _tickTarget;
            _timer?.Stop();
        }
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private double ValueAtPoint(Point p)
    {
        var (_, bar) = Geometry();
        if (Vertical)
        {
            double y = Math.Clamp(p.Y, bar.Y, bar.Bottom);
            _dragPos = _moving ? y : null;
            double t = bar.Height > 0 ? 1 - (y - bar.Y) / bar.Height : 0;
            return Math.Round(Minimum + t * (Maximum - Minimum));
        }
        else
        {
            double x = Math.Clamp(p.X, bar.X, bar.Right);
            _dragPos = _moving ? x : null;
            double t = bar.Width > 0 ? (x - bar.X) / bar.Width : 0;
            return Math.Round(Exponential && Minimum > 0
                ? Minimum * Math.Pow(Maximum / Minimum, t)
                : Minimum + t * (Maximum - Minimum));
        }
    }

    private void Pick(Point p)
    {
        double v = ValueAtPoint(p);
        if (SupportedValues.Count > 0)
        {
            int nearest = SupportedValues.OrderBy(s => Math.Abs(s - v)).First();
            v = nearest;
        }
        Value = Math.Clamp(v, Minimum, Maximum);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !IsEnabled) return;

        Focus();
        e.Pointer.Capture(this);
        Animate(InnerPressed);
        _moving = true;
        Pick(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_moving) Pick(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_moving) return;
        _moving = false;
        _dragPos = null;
        e.Pointer.Capture(null);
        Animate(new Rect(Bounds.Size).Contains(e.GetPosition(this)) ? InnerHover : InnerNormal);
        InvalidateVisual();
        ValueCommitted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (!_moving) Animate(InnerHover);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_moving) Animate(InnerNormal);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        bool up = e.Key is Key.Right or Key.Up;
        bool down = e.Key is Key.Left or Key.Down;
        if (!up && !down) return;

        double step = Step > 0 ? Step : 1;
        double v = Value;
        if (SupportedValues.Count > 0)
        {
            v = up
                ? SupportedValues.Where(s => s > v).DefaultIfEmpty((int)v).Min()
                : SupportedValues.Where(s => s < v).DefaultIfEmpty((int)v).Max();
        }
        else
        {
            v = up ? Math.Min(Maximum, v + step) : Math.Max(Minimum, v - step);
        }

        Value = v;
        ValueCommitted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}

/// <summary>UI Automation: exposes <see cref="RSlider"/> as a slider with a range value.</summary>
internal sealed class RSliderAutomationPeer : RangeBaseAutomationPeer
{
    public RSliderAutomationPeer(RSlider owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

    protected override string GetClassNameCore() => nameof(RSlider);
}
