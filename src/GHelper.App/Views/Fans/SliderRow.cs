using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GHelper.App.Controls;

namespace GHelper.App.Views.Fans;

/// <summary>
/// One slider block of the fans window: caption on the left, current value (bold) on the right, slider underneath
/// (legacy: a Panel with two Labels and an RTrackBar). <see cref="Value"/> only follows user input, so a change of the
/// range (e.g. GPU power maximum arriving late) never writes a clamped value back to the view model.
/// </summary>
internal sealed class SliderRow : Grid
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SliderRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> ValueTextProperty =
        AvaloniaProperty.Register<SliderRow, string?>(nameof(ValueText));

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<SliderRow, double>(nameof(Minimum), 0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<SliderRow, double>(nameof(Maximum), 100);

    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<SliderRow, double>(nameof(Step), 1);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SliderRow, double>(nameof(Value), 0, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private readonly TextBlock _title = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _value = new()
    {
        FontWeight = FontWeight.Bold,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Right,
    };

    private readonly RSlider _slider = new() { Height = 20, Margin = new Thickness(0, 6, 0, 0) };
    private bool _syncing;

    public SliderRow()
    {
        RowDefinitions = new RowDefinitions("Auto,Auto");
        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        Margin = new Thickness(0, 4, 0, 8);

        SetRow(_title, 0);
        SetRow(_value, 0);
        SetColumn(_value, 1);
        SetRow(_slider, 1);
        SetColumnSpan(_slider, 2);
        _title.Margin = new Thickness(5, 0, 8, 0);
        _value.Margin = new Thickness(0, 0, 5, 0);

        Children.Add(_title);
        Children.Add(_value);
        Children.Add(_slider);

        _slider.ValueChanged += (_, e) =>
        {
            if (_syncing) return;
            SetCurrentValue(ValueProperty, Math.Round(e.NewValue));
        };

        _slider.ValueCommitted += (_, _) => ValueCommitted?.Invoke(this, EventArgs.Empty);
    }

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? ValueText { get => GetValue(ValueTextProperty); set => SetValue(ValueTextProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public RSlider Slider => _slider;

    /// <summary>Mouse up / key up on the slider (legacy MouseUp + KeyUp handlers that applied the value).</summary>
    public event EventHandler? ValueCommitted;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TitleProperty)
        {
            _title.Text = Title;
            AutomationProperties.SetName(_slider, Title);
        }
        else if (change.Property == ValueTextProperty)
        {
            _value.Text = ValueText;
        }
        else if (change.Property == StepProperty)
        {
            _slider.Step = Step;
        }
        else if (change.Property == MinimumProperty || change.Property == MaximumProperty || change.Property == ValueProperty)
        {
            ApplyToSlider();
        }
    }

    private void ApplyToSlider()
    {
        _syncing = true;
        try
        {
            double min = Minimum, max = Math.Max(Maximum, Minimum);
            // widen first so neither end is ever coerced by the other
            if (max > _slider.Maximum) _slider.Maximum = max;
            _slider.Minimum = min;
            _slider.Maximum = max;
            _slider.Value = Math.Clamp(Value, min, max);
        }
        finally
        {
            _syncing = false;
        }
    }
}
