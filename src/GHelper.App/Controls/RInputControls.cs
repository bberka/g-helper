using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using GHelper.App.Theme;

namespace GHelper.App.Controls;

/// <summary>
/// Drop down list (legacy RComboBox). Visuals come from the "RComboBox" style in Theme/Controls.axaml (palette +
/// rounded border); the mouse wheel never changes the selection unless the list is open, like the legacy control.
/// </summary>
public class RComboBox : ComboBox
{

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (!IsDropDownOpen)
        {
            e.Handled = true;
            return;
        }
        base.OnPointerWheelChanged(e);
    }
}

/// <summary>Text box with a placeholder, themed through the palette (legacy RTextBox).</summary>
public class RTextBox : TextBox
{
}

/// <summary>Numeric up/down (legacy RNumericUpDown), palette themed.</summary>
public class RNumericUpDown : NumericUpDown
{

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        // legacy NumericUpDownWithUnit: only react to the wheel when focused
        if (!IsKeyboardFocusWithin)
        {
            e.Handled = true;
            return;
        }
        base.OnPointerWheelChanged(e);
    }
}

/// <summary>
/// Numeric up/down that displays a unit suffix ("45 (W)"), or its <see cref="OffText"/> when the value is 0
/// (legacy NumericUpDownWithUnit).
/// </summary>
public class NumericUpDownWithUnit : RNumericUpDown
{
    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<NumericUpDownWithUnit, string?>(nameof(Unit));

    public static readonly StyledProperty<bool> UnitFirstProperty =
        AvaloniaProperty.Register<NumericUpDownWithUnit, bool>(nameof(UnitFirst));

    public static readonly StyledProperty<string?> OffTextProperty =
        AvaloniaProperty.Register<NumericUpDownWithUnit, string?>(nameof(OffText));


    public NumericUpDownWithUnit()
    {
        TextConverter = new UnitConverter(this);
    }

    public string? Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public bool UnitFirst { get => GetValue(UnitFirstProperty); set => SetValue(UnitFirstProperty, value); }
    public string? OffText { get => GetValue(OffTextProperty); set => SetValue(OffTextProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == UnitProperty || change.Property == UnitFirstProperty || change.Property == OffTextProperty)
        {
            // a new converter instance makes the control regenerate its text
            TextConverter = new UnitConverter(this);
        }
    }

    /// <summary>
    /// NumericUpDown semantics: <c>ConvertBack</c> renders a value as text, <c>Convert</c> parses the typed text.
    /// </summary>
    private sealed class UnitConverter : IValueConverter
    {
        private readonly NumericUpDownWithUnit _owner;
        public UnitConverter(NumericUpDownWithUnit owner) => _owner = owner;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not decimal d) return string.Empty;
            if (d == 0 && !string.IsNullOrEmpty(_owner.OffText)) return _owner.OffText;
            string number = d.ToString("0.##", culture);
            if (string.IsNullOrEmpty(_owner.Unit)) return number;
            return _owner.UnitFirst ? $"({_owner.Unit}) {number}" : $"{number} ({_owner.Unit})";
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string text = value?.ToString() ?? "";
            if (!string.IsNullOrEmpty(_owner.OffText) && text == _owner.OffText) return 0m;

            // keep digits, sign and decimal separator only: everything else belongs to the unit
            var sb = new System.Text.StringBuilder();
            foreach (char c in text)
                if (char.IsDigit(c) || c == '-' || c == '.' || c == ',') sb.Append(c);
            return decimal.TryParse(sb.ToString(), NumberStyles.Number, culture, out var d) ? d : null;
        }
    }
}

/// <summary>Check box (legacy RCheckBox). Optionally draws a rounded outline around the whole control.</summary>
public class RCheckBox : CheckBox
{
    public static readonly StyledProperty<bool> ShowBorderProperty =
        AvaloniaProperty.Register<RCheckBox, bool>(nameof(ShowBorder));

    static RCheckBox()
    {
        AffectsRender<RCheckBox>(ShowBorderProperty, BorderBrushProperty, BackgroundProperty);
    }


    public bool ShowBorder { get => GetValue(ShowBorderProperty); set => SetValue(ShowBorderProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // ":bordered" gives the outlined variant its own padding / height in the template (Theme/Controls.axaml)
        if (change.Property == ShowBorderProperty) PseudoClasses.Set(":bordered", ShowBorder);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var rect = new Rect(Bounds.Size);
        if (!ShowBorder || AppTheme.Flat || rect.Width < 2 || rect.Height < 2) return;
        double r = CornerRadius.TopLeft;
        context.DrawRectangle(null, BorderBrush is { } bb ? new Pen(bb, 1) : null, rect.Deflate(0.5), r, r);
    }
}

/// <summary>
/// Track bar (legacy RTrackBar) - Avalonia's Slider with the palette applied; the unfilled part of the channel uses the
/// ChartGrid colour like the legacy owner-drawn channel. Accessible name / value announcements come from the
/// built-in slider automation peer.
/// </summary>
public class RTrackBar : Slider
{
}
