using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GHelper.App.Theme;

namespace GHelper.App.Controls;

/// <summary>Colour maths shared by the picker (legacy RColorPicker statics).</summary>
public static class ColorMath
{
    public static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;
        switch ((int)(h / 60) % 6)
        {
            case 0: r = c; g = x; break;
            case 1: r = x; g = c; break;
            case 2: g = c; b = x; break;
            case 3: g = x; b = c; break;
            case 4: r = x; b = c; break;
            case 5: r = c; b = x; break;
        }
        return Color.FromArgb(255, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public static (double h, double s, double v) RgbToHsv(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        double h = 0;
        if (delta > 0)
        {
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
        }
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : delta / max, max);
    }

    /// <summary>0x00BBGGRR, the format of the legacy "aura_color_custom" list (System.Windows.Forms.ColorDialog).</summary>
    public static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    public static Color FromColorRef(int v) => Color.FromArgb(255, (byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF));

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}

/// <summary>Persistence of the 14 editable swatches. Value format: COLORREF integers joined by '-'.</summary>
public interface IColorPickerStore
{
    string? LoadCustomColors();
    void SaveCustomColors(string value);
}

/// <summary>Themed HSV colour picker dialog (port of app/UI/RColorPicker.cs, replacing the native ColorDialog).</summary>
public class ColorPickerWindow : Window
{
    private const int Cols = 14;
    private const int Pad = 12, Square = 200, HueW = 18, RightW = 150, Sw = 22, Gap = 6;

    private static Color C(int rgb) => Color.FromArgb(255, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    private static readonly Color[] Defaults =
    {
        C(0xFF0000), C(0xFF4000), C(0xFF8000), C(0xFFBF00), C(0xFFFF00), C(0x80FF00), C(0x00FF00),
        C(0x00FF80), C(0x00FFFF), C(0x0080FF), C(0x0000FF), C(0x8000FF), C(0xFF00FF), C(0xFF0080),
        C(0xFFFFFF), C(0xE0E0E0), C(0xC0C0C0), C(0xA0A0A0), C(0x808080), C(0x606060), C(0x404040),
        C(0x000000), C(0xFFC0C0), C(0xFFE0C0), C(0xFFFFC0), C(0xC0FFC0), C(0xC0FFFF), C(0xC0C0FF),
    };

    private readonly Color _original;
    private readonly bool _allowRandom;
    private readonly IColorPickerStore? _store;
    private readonly List<Color> _custom = new();
    private double _hue, _sat, _val;
    private bool _suppressHex;

    private readonly SvPanel _sv = new();
    private readonly HuePanel _huePanel = new();
    private readonly Border _preview = new();
    private readonly RTextBox _hex = new() { MaxLength = 7 };
    private readonly TextBlock _rgbLabel = new();
    private readonly List<Swatch> _customSwatches = new();
    private Swatch? _active;

    /// <summary>Currently selected colour.</summary>
    public Color Color { get; private set; }

    /// <summary>True when closed with OK.</summary>
    public bool Accepted { get; private set; }

    /// <summary>
    /// Fires when a colour is committed (swatch / hex / mouse-up) so the caller can apply it live. When the dialog is
    /// cancelled the original colour is re-raised so the caller can revert (legacy behaviour).
    /// </summary>
    public event Action<Color>? ColorChanged;

    public ColorPickerWindow() : this(Colors.Red) { }

    public ColorPickerWindow(Color initial, bool allowRandom = false, IColorPickerStore? store = null)
    {
        Color = initial;
        _original = initial;
        _allowRandom = allowRandom;
        _store = store;
        (_hue, _sat, _val) = ColorMath.RgbToHsv(initial);

        Title = "Color";
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        LoadCustom();
        BuildLayout();
        RefreshCustom();
        UpdateAll(null);

        Closing += (_, _) =>
        {
            if (!Accepted && ColorMath.ToColorRef(Color) != ColorMath.ToColorRef(_original))
                ColorChanged?.Invoke(_original);
        };
    }

    private void LoadCustom()
    {
        for (int i = 0; i < Cols; i++) _custom.Add(Colors.White);
        int idx = 0;
        foreach (var token in (_store?.LoadCustomColors() ?? "").Split('-'))
        {
            if (idx >= Cols) break;
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                _custom[idx++] = ColorMath.FromColorRef(v);
        }
    }

    private static void Place(Canvas canvas, Control c, double x, double y, double w = double.NaN, double h = double.NaN)
    {
        Canvas.SetLeft(c, x);
        Canvas.SetTop(c, y);
        if (!double.IsNaN(w)) c.Width = w;
        if (!double.IsNaN(h)) c.Height = h;
        canvas.Children.Add(c);
    }

    private void BuildLayout()
    {
        var canvas = new Canvas();

        _sv.Picked += (s, v) => { _sat = s; _val = v; Color = ColorMath.HsvToRgb(_hue, _sat, _val); Preview(_sv); };
        _sv.Committed += Commit;
        Place(canvas, _sv, Pad, Pad, Square, Square);

        int hueX = Pad + Square + 8;
        _huePanel.Picked += h => { _hue = h; Color = ColorMath.HsvToRgb(_hue, _sat, _val); Preview(_huePanel); };
        _huePanel.Committed += Commit;
        Place(canvas, _huePanel, hueX, Pad, HueW, Square);

        int rightX = hueX + HueW + 16;

        _preview.BorderThickness = new Thickness(1);
        _preview.BorderBrush = Brushes.Gray;
        Place(canvas, _preview, rightX, Pad, RightW, 110);

        var hexLabel = new TextBlock { Text = "Hex" };
        Place(canvas, hexLabel, rightX, Pad + 118);

        _hex.TextChanged += (_, _) => { if (!_suppressHex) ApplyHex(false); };
        _hex.LostFocus += (_, _) => ApplyHex(true);
        Place(canvas, _hex, rightX, Pad + 140, RightW, UiMetrics.ControlHeight);

        Place(canvas, _rgbLabel, rightX, Pad + 140 + UiMetrics.ControlHeight + 8);

        int gridY = Pad + Square + 10;
        for (int i = 0; i < Defaults.Length; i++)
        {
            var sw = AddSwatch(canvas, Pad + (i % Cols) * (Sw + Gap), gridY + (i / Cols) * (Sw + Gap), x => { SetActive(x); Apply(x.Color); });
            sw.Color = Defaults[i];
        }

        int presetRows = (Defaults.Length + Cols - 1) / Cols;
        int customY = gridY + presetRows * (Sw + Gap) + 8;
        for (int i = 0; i < Cols; i++)
        {
            int idx = i;
            _customSwatches.Add(AddSwatch(canvas, Pad + i * (Sw + Gap), customY, _ => OnCustomClick(idx)));
        }

        int formW = rightX + RightW + Pad;
        int buttonsY = customY + Sw + 14;

        var ok = new RButton { Content = "OK", Width = 84 };
        ok.Click += (_, _) => { Accepted = true; Close(); };
        var cancel = new RButton { Content = "Cancel", Width = 84 };
        cancel.Click += (_, _) => { Accepted = false; Close(); };
        Place(canvas, cancel, formW - Pad - 84, buttonsY);
        Place(canvas, ok, formW - Pad - 84 - 8 - 84, buttonsY);

        if (_allowRandom)
        {
            var random = new RButton { Content = Properties.Strings.AuraRandomColor, Width = 84 };
            random.Click += (_, _) => Apply(Colors.Black);
            Place(canvas, random, Pad, buttonsY);
        }

        Width = formW;
        Height = buttonsY + UiMetrics.ControlHeight + Pad;
        Content = canvas;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Accepted = false; Close(); }
            else if (e.Key == Key.Enter && !_hex.IsFocused) { Accepted = true; Close(); }
        };
    }

    private Swatch AddSwatch(Canvas canvas, double x, double y, Action<Swatch> onClick)
    {
        var sw = new Swatch();
        sw.Clicked += () => onClick(sw);
        Place(canvas, sw, x, y, Sw, Sw);
        return sw;
    }

    private void SetActive(Swatch sw)
    {
        if (_active != null && _active != sw) _active.Selected = false;
        _active = sw;
        sw.Selected = true;
    }

    private void OnCustomClick(int idx)
    {
        SetActive(_customSwatches[idx]);
        Apply(_custom[idx]);
    }

    private void RefreshCustom()
    {
        for (int i = 0; i < _customSwatches.Count; i++) _customSwatches[i].Color = _custom[i];
    }

    private int ActiveCustomIndex => _active is null ? -1 : _customSwatches.IndexOf(_active);

    private void Preview(Control source)
    {
        UpdateAll(source);
        int idx = ActiveCustomIndex;
        if (idx >= 0) { _custom[idx] = Color; _customSwatches[idx].Color = Color; }
        else if (_active != null) { _active.Selected = false; _active = null; }
    }

    private void Commit()
    {
        if (ActiveCustomIndex >= 0)
            _store?.SaveCustomColors(string.Join("-", _custom.Select(ColorMath.ToColorRef)));
        ColorChanged?.Invoke(Color);
    }

    private void Apply(Color c)
    {
        Color = c;
        (_hue, _sat, _val) = ColorMath.RgbToHsv(c);
        UpdateAll(null);
        Commit();
    }

    private void ApplyHex(bool normalize)
    {
        string text = (_hex.Text ?? "").Trim().TrimStart('#');
        if (text.Length == 6 && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            Color = C(rgb);
            (_hue, _sat, _val) = ColorMath.RgbToHsv(Color);
            Preview(_hex);
            Commit();
        }
        else if (normalize)
        {
            _hex.Text = ColorMath.ToHex(Color);
        }
    }

    private void UpdateAll(Control? source)
    {
        _sv.Hue = _hue; _sv.Sat = _sat; _sv.Val = _val;
        _huePanel.Hue = _hue;
        if (source != _sv) _sv.InvalidateVisual();
        if (source != _huePanel) _huePanel.InvalidateVisual();

        _preview.Background = new SolidColorBrush(Color);
        if (source != _hex)
        {
            _suppressHex = true;
            _hex.Text = ColorMath.ToHex(Color);
            _suppressHex = false;
        }
        _rgbLabel.Text = $"R {Color.R}   G {Color.G}   B {Color.B}";
    }

    // ---- parts --------------------------------------------------------------------------------------------

    private sealed class Swatch : Control
    {
        private Color _color;
        private bool _selected;
        public event Action? Clicked;

        public Swatch() { Cursor = new Cursor(StandardCursorType.Hand); }

        public Color Color { get => _color; set { _color = value; InvalidateVisual(); } }
        public bool Selected { get => _selected; set { _selected = value; InvalidateVisual(); } }

        public override void Render(DrawingContext ctx)
        {
            var r = new Rect(Bounds.Size);
            ctx.DrawRectangle(new SolidColorBrush(_color), null, r);
            var edge = _selected ? AppColors.Standard : Color.FromArgb(255, 128, 128, 128);
            double t = _selected ? 2 : 1;
            ctx.DrawRectangle(null, new Pen(new SolidColorBrush(edge), t), r.Deflate(t / 2));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Clicked?.Invoke();
        }
    }

    private abstract class PickPanel : Control
    {
        private bool _drag;
        public event Action? Committed;

        protected abstract void Pick(Point p);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _drag = true;
            e.Pointer.Capture(this);
            Pick(e.GetPosition(this));
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_drag) Pick(e.GetPosition(this));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!_drag) return;
            _drag = false;
            e.Pointer.Capture(null);
            Committed?.Invoke();
        }
    }

    private sealed class SvPanel : PickPanel
    {
        public double Hue, Sat, Val;
        public event Action<double, double>? Picked;

        public SvPanel() { Cursor = new Cursor(StandardCursorType.Cross); }

        public override void Render(DrawingContext ctx)
        {
            var rect = new Rect(Bounds.Size);
            var satBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(ColorMath.HsvToRgb(Hue, 1, 1), 1) },
            };
            var valBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Colors.Black, 1) },
            };
            ctx.DrawRectangle(satBrush, null, rect);
            ctx.DrawRectangle(valBrush, null, rect);

            double cx = Sat * rect.Width, cy = (1 - Val) * rect.Height, r = 9;
            ctx.DrawEllipse(null, new Pen(Brushes.White, 2), new Point(cx, cy), r, r);
            ctx.DrawEllipse(null, new Pen(Brushes.Black, 1), new Point(cx, cy), r + 1.5, r + 1.5);
        }

        protected override void Pick(Point p)
        {
            Sat = Math.Clamp(p.X / Bounds.Width, 0, 1);
            Val = Math.Clamp(1 - p.Y / Bounds.Height, 0, 1);
            InvalidateVisual();
            Picked?.Invoke(Sat, Val);
        }
    }

    private sealed class HuePanel : PickPanel
    {
        public double Hue;
        public event Action<double>? Picked;

        private static readonly Color[] Stops = { Colors.Red, Colors.Yellow, Colors.Lime, Colors.Cyan, Colors.Blue, Colors.Magenta, Colors.Red };

        public HuePanel() { Cursor = new Cursor(StandardCursorType.Hand); }

        public override void Render(DrawingContext ctx)
        {
            var rect = new Rect(Bounds.Size);
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            };
            for (int i = 0; i < Stops.Length; i++)
                brush.GradientStops.Add(new GradientStop(Stops[i], i / (double)(Stops.Length - 1)));
            ctx.DrawRectangle(brush, null, rect);

            double y = Hue / 360.0 * rect.Height, h = 4;
            ctx.DrawRectangle(null, new Pen(Brushes.White, 2), new Rect(1, y - h, rect.Width - 2, h * 2));
            ctx.DrawRectangle(null, new Pen(Brushes.Black, 1), new Rect(0, y - h - 1.5, rect.Width, h * 2 + 3));
        }

        protected override void Pick(Point p)
        {
            Hue = Math.Clamp(p.Y / Bounds.Height, 0, 1) * 360;
            InvalidateVisual();
            Picked?.Invoke(Hue);
        }
    }
}
