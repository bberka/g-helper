using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

namespace GHelper.App.Controls;

/// <summary>One control point of a fan curve: temperature (X, degrees C) and fan speed (Y, percent).</summary>
public sealed class FanCurvePoint
{
    public FanCurvePoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>
/// Draggable fan curve editor replacing the DataVisualization chart of the legacy Fans form (RChart):
/// a poly line with circular handles, axes with custom labels, drag one point (neighbours are pushed along so the
/// curve stays monotonic), Shift+drag moves the whole curve vertically, arrow keys / Tab / Home / End for keyboard use.
/// Feed it with <see cref="SetPoints"/>; observe <see cref="CurveChanged"/> (live) and <see cref="CurveCommitted"/>
/// (mouse up / key press: persist here, like legacy Chart_Save).
/// </summary>
public class FanCurveChart : Control
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<FanCurveChart, string?>(nameof(Title));
    public static readonly StyledProperty<double> XMinProperty = AvaloniaProperty.Register<FanCurveChart, double>(nameof(XMin), 20);
    public static readonly StyledProperty<double> XMaxProperty = AvaloniaProperty.Register<FanCurveChart, double>(nameof(XMax), 100);
    public static readonly StyledProperty<double> YMinProperty = AvaloniaProperty.Register<FanCurveChart, double>(nameof(YMin), 0);
    public static readonly StyledProperty<double> YMaxProperty = AvaloniaProperty.Register<FanCurveChart, double>(nameof(YMax), 100);
    public static readonly StyledProperty<double> XIntervalProperty = AvaloniaProperty.Register<FanCurveChart, double>(nameof(XInterval), 10);
    public static readonly StyledProperty<double> YIntervalProperty = AvaloniaProperty.Register<FanCurveChart, double>(nameof(YInterval), 10);
    public static readonly StyledProperty<bool> ClampDotsProperty = AvaloniaProperty.Register<FanCurveChart, bool>(nameof(ClampDots));
    public static readonly StyledProperty<IBrush?> ChartBackgroundProperty = AvaloniaProperty.Register<FanCurveChart, IBrush?>(nameof(ChartBackground));
    public static readonly StyledProperty<IBrush?> GridBrushProperty = AvaloniaProperty.Register<FanCurveChart, IBrush?>(nameof(GridBrush));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<FanCurveChart, IBrush?>(nameof(Foreground));
    public static readonly StyledProperty<IBrush?> LineBrushProperty = AvaloniaProperty.Register<FanCurveChart, IBrush?>(nameof(LineBrush));

    private const double HitRadius = 12;
    private const double HandleRadius = 5;

    private readonly List<FanCurvePoint> _points = new();
    private int _hotIndex = -1;
    private int _dragIndex = -1;
    private int _keyIndex;
    private Point _mouse;

    static FanCurveChart()
    {
        FocusableProperty.OverrideDefaultValue<FanCurveChart>(true);
        AffectsRender<FanCurveChart>(TitleProperty, XMinProperty, XMaxProperty, YMinProperty, YMaxProperty, XIntervalProperty,
            YIntervalProperty, ChartBackgroundProperty, GridBrushProperty, ForegroundProperty, LineBrushProperty);
    }

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public double XMin { get => GetValue(XMinProperty); set => SetValue(XMinProperty, value); }
    public double XMax { get => GetValue(XMaxProperty); set => SetValue(XMaxProperty, value); }
    public double YMin { get => GetValue(YMinProperty); set => SetValue(YMinProperty, value); }
    public double YMax { get => GetValue(YMaxProperty); set => SetValue(YMaxProperty, value); }
    public double XInterval { get => GetValue(XIntervalProperty); set => SetValue(XIntervalProperty, value); }
    public double YInterval { get => GetValue(YIntervalProperty); set => SetValue(YIntervalProperty, value); }

    /// <summary>Legacy "fan_clamp": point i can only move inside its own 10 degree slot (30 + 10*i .. +9).</summary>
    public bool ClampDots { get => GetValue(ClampDotsProperty); set => SetValue(ClampDotsProperty, value); }

    public IBrush? ChartBackground { get => GetValue(ChartBackgroundProperty); set => SetValue(ChartBackgroundProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public IBrush? LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }

    /// <summary>Custom axis labels (legacy CustomLabels: °C / °F, RPM / %). Null: plain numbers.</summary>
    public Func<double, string>? XLabel { get; set; }
    public Func<double, string>? YLabel { get; set; }

    /// <summary>Text of the floating tip next to the active point. Null: "x, y".</summary>
    public Func<FanCurvePoint, string>? TipFormatter { get; set; }

    /// <summary>Lower bound of Y for a given X (legacy: (x - 70) * 1.2, so hot temperatures can not have a low speed).</summary>
    public Func<double, double> MinimumY { get; set; } = x => (x - 70) * 1.2;

    public IReadOnlyList<FanCurvePoint> Points => _points;

    /// <summary>The point the keyboard / hover tip refers to, -1 if none.</summary>
    public int ActiveIndex => _dragIndex >= 0 ? _dragIndex : (IsKeyboardFocusWithin ? _keyIndex : _hotIndex);

    public bool IsDragging => _dragIndex >= 0;

    public event EventHandler? CurveChanged;
    public event EventHandler? CurveCommitted;

    /// <summary>True while a point is hovered/dragged (legacy FanDragHint: "hold Shift to move all points").</summary>
    public event EventHandler<bool>? HintChanged;

    public void SetPoints(IEnumerable<(double x, double y)> points)
    {
        _points.Clear();
        foreach (var (x, y) in points) _points.Add(new FanCurvePoint(x, y));
        _keyIndex = Math.Clamp(_keyIndex, 0, Math.Max(0, _points.Count - 1));
        InvalidateVisual();
    }

    // ---- geometry -------------------------------------------------------------------------------------------

    private Rect PlotArea
    {
        get
        {
            const double left = 40, right = 14, top = 30, bottom = 24;
            return new Rect(left, top, Math.Max(1, Bounds.Width - left - right), Math.Max(1, Bounds.Height - top - bottom));
        }
    }

    private Point ToPixel(double x, double y)
    {
        var p = PlotArea;
        double fx = (x - XMin) / Math.Max(1e-9, XMax - XMin);
        double fy = (y - YMin) / Math.Max(1e-9, YMax - YMin);
        return new Point(p.X + p.Width * fx, p.Bottom - p.Height * fy);
    }

    private (double x, double y) FromPixel(Point pt)
    {
        var p = PlotArea;
        double x = XMin + (pt.X - p.X) / p.Width * (XMax - XMin);
        double y = YMin + (p.Bottom - pt.Y) / p.Height * (YMax - YMin);
        return (x, y);
    }

    private int HitTest(Point pt)
    {
        int best = -1;
        double bestDist = HitRadius * HitRadius;
        for (int i = 0; i < _points.Count; i++)
        {
            var c = ToPixel(_points[i].X, _points[i].Y);
            double d = (c.X - pt.X) * (c.X - pt.X) + (c.Y - pt.Y) * (c.Y - pt.Y);
            if (d <= bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    // ---- rendering ------------------------------------------------------------------------------------------

    private string FormatX(double v) => XLabel?.Invoke(v) ?? v.ToString("0", CultureInfo.CurrentCulture);
    private string FormatY(double v) => YLabel?.Invoke(v) ?? v.ToString("0", CultureInfo.CurrentCulture);

    private FormattedText Text(string text, double size, IBrush? brush, FontWeight weight = FontWeight.Normal) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, weight), size, brush ?? Brushes.Gray);

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        var fore = Foreground ?? Brushes.Gray;
        var grid = GridBrush ?? Brushes.DimGray;
        var line = LineBrush ?? Brushes.Gray;

        ctx.DrawRectangle(ChartBackground ?? Brushes.Transparent, null, bounds);

        if (IsKeyboardFocusWithin)
            ctx.DrawRectangle(null, new Pen(grid, 1), bounds.Deflate(0.5));

        if (!string.IsNullOrEmpty(Title))
            ctx.DrawText(Text(Title!, 12, fore, FontWeight.Bold), new Point(10, 6));

        var plot = PlotArea;
        var gridPen = new Pen(grid, 1);

        // grid + labels
        for (double x = XMin; x <= XMax + 1e-6; x += Math.Max(1, XInterval))
        {
            double px = ToPixel(x, YMin).X;
            ctx.DrawLine(gridPen, new Point(px, plot.Y), new Point(px, plot.Bottom));
            var t = Text(FormatX(x), 11, fore);
            ctx.DrawText(t, new Point(px - t.Width / 2, plot.Bottom + 4));
        }
        for (double y = YMin; y <= YMax + 1e-6; y += Math.Max(1, YInterval))
        {
            double py = ToPixel(XMin, y).Y;
            ctx.DrawLine(gridPen, new Point(plot.X, py), new Point(plot.Right, py));
            var t = Text(FormatY(y), 11, fore);
            ctx.DrawText(t, new Point(plot.X - t.Width - 6, py - t.Height / 2));
        }
        ctx.DrawRectangle(null, gridPen, plot);

        if (_points.Count == 0) return;

        // curve
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (int i = 0; i < _points.Count; i++)
            {
                var p = ToPixel(_points[i].X, _points[i].Y);
                if (i == 0) g.BeginFigure(p, false);
                else g.LineTo(p);
            }
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(line, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geo);

        int active = ActiveIndex;
        for (int i = 0; i < _points.Count; i++)
        {
            var p = ToPixel(_points[i].X, _points[i].Y);
            double r = i == active ? HandleRadius + 2 : HandleRadius;
            ctx.DrawEllipse(line, null, p, r, r);
            ctx.DrawEllipse(ChartBackground ?? Brushes.Transparent, null, p, r - 2.5, r - 2.5);
        }

        if (active >= 0 && active < _points.Count)
        {
            var pt = _points[active];
            string tip = TipFormatter?.Invoke(pt) ?? $"{FormatX(pt.X)}, {FormatY(pt.Y)}";
            var t = Text(tip, 11, fore, FontWeight.Bold);
            var c = ToPixel(pt.X, pt.Y);
            double tx = Math.Clamp(c.X - t.Width / 2, plot.X, Math.Max(plot.X, plot.Right - t.Width));
            double ty = c.Y - t.Height - 10 < plot.Y ? c.Y + 10 : c.Y - t.Height - 10;
            var box = new Rect(tx - 4, ty - 2, t.Width + 8, t.Height + 4);
            ctx.DrawRectangle(ChartBackground ?? Brushes.Black, new Pen(grid, 1), box, 3, 3);
            ctx.DrawText(t, new Point(tx, ty));
        }
    }

    // ---- editing --------------------------------------------------------------------------------------------

    private void Move(int index, double newX, double newY, bool all)
    {
        if (index < 0 || index >= _points.Count) return;

        double dx = Math.Clamp(newX, XMin, XMax);
        double dy = Math.Clamp(newY, YMin, YMax);
        dy = Math.Max(dy, MinimumY(dx));

        var cur = _points[index];

        if (ClampDots)
        {
            double minX = 30 + index * 10;
            dx = Math.Clamp(dx, minX, minX + 9);
        }

        if (all)
        {
            double deltaY = dy - cur.Y;
            foreach (var p in _points)
            {
                p.X = Math.Clamp(p.X, XMin, XMax);
                p.Y = Math.Clamp(p.Y + deltaY, 0, YMax);
            }
        }
        else
        {
            cur.X = dx;
            cur.Y = dy;
            AdjustNeighbours(index, dx, dy);
        }

        CurveChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Port of Fans.AdjustAllLevels: keep both X and Y non decreasing along the curve.</summary>
    private void AdjustNeighbours(int index, double x, double y)
    {
        if (index < _points.Count - 1)
        {
            var upper = _points[index + 1];
            if (y > upper.Y)
                for (int i = index + 1; i < _points.Count; i++)
                {
                    if (_points[i].Y >= y) break;
                    _points[i].Y = y;
                }
            if (x > upper.X)
                for (int i = index + 1; i < _points.Count; i++)
                {
                    if (_points[i].X >= x) break;
                    _points[i].X = x;
                }
        }

        if (index > 0)
        {
            var lower = _points[index - 1];
            if (y <= Math.Floor(lower.Y))
                for (int i = index - 1; i >= 0; i--)
                {
                    if (_points[i].Y < y) break;
                    _points[i].Y = Math.Floor(y);
                }
            if (x < lower.X)
                for (int i = index - 1; i >= 0; i--)
                {
                    if (_points[i].X <= x) break;
                    _points[i].X = x;
                }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _mouse = e.GetPosition(this);

        if (_dragIndex >= 0)
        {
            var (x, y) = FromPixel(_mouse);
            Move(_dragIndex, x, y, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            return;
        }

        int hot = HitTest(_mouse);
        if (hot != _hotIndex)
        {
            bool wasHot = _hotIndex >= 0;
            _hotIndex = hot;
            Cursor = hot >= 0 ? new Cursor(StandardCursorType.Hand) : null;
            if (wasHot != (hot >= 0)) HintChanged?.Invoke(this, hot >= 0);
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        int hit = HitTest(e.GetPosition(this));
        if (hit < 0) return;
        _dragIndex = hit;
        _keyIndex = hit;
        e.Pointer.Capture(this);
        HintChanged?.Invoke(this, true);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragIndex < 0) return;
        _dragIndex = -1;
        e.Pointer.Capture(null);
        CurveCommitted?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_dragIndex >= 0) return;
        if (_hotIndex >= 0) HintChanged?.Invoke(this, false);
        _hotIndex = -1;
        Cursor = null;
        InvalidateVisual();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (_points.Count > 0) _keyIndex = Math.Clamp(_keyIndex, 0, _points.Count - 1);
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int count = _points.Count;
        if (count == 0 || e.Handled) return;

        int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 5 : 1;
        var p = _points[Math.Clamp(_keyIndex, 0, count - 1)];

        switch (e.Key)
        {
            case Key.Home: _keyIndex = 0; break;
            case Key.End: _keyIndex = count - 1; break;
            case Key.Left: Move(_keyIndex, p.X - step, p.Y, false); CurveCommitted?.Invoke(this, EventArgs.Empty); break;
            case Key.Right: Move(_keyIndex, p.X + step, p.Y, false); CurveCommitted?.Invoke(this, EventArgs.Empty); break;
            case Key.Down: Move(_keyIndex, p.X, p.Y - step, false); CurveCommitted?.Invoke(this, EventArgs.Empty); break;
            case Key.Up: Move(_keyIndex, p.X, p.Y + step, false); CurveCommitted?.Invoke(this, EventArgs.Empty); break;
            case Key.Tab:
                // stay inside the chart while there are more points in the tab direction (legacy PreviewKeyDown)
                bool back = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                if (back ? _keyIndex > 0 : _keyIndex < count - 1)
                {
                    _keyIndex += back ? -1 : 1;
                    break;
                }
                return;
            default:
                return;
        }

        InvalidateVisual();
        e.Handled = true;
    }
}
