using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace GHelper.App.Controls;

/// <summary>
/// Button showing one or two colour swatches on its right side (legacy RColorButton: keyboard lighting colour 1 / 2).
/// Clicking the second swatch raises <see cref="Swatch2Click"/> instead of Click.
/// </summary>
public class RColorButton : RButton
{
    public static readonly StyledProperty<Color?> SwatchColorProperty =
        AvaloniaProperty.Register<RColorButton, Color?>(nameof(SwatchColor));

    public static readonly StyledProperty<Color?> SwatchColor2Property =
        AvaloniaProperty.Register<RColorButton, Color?>(nameof(SwatchColor2));

    public static readonly RoutedEvent<RoutedEventArgs> Swatch2ClickEvent =
        RoutedEvent.Register<RColorButton, RoutedEventArgs>(nameof(Swatch2Click), RoutingStrategies.Bubble);

    private Rect _swatch2Rect;
    private bool _pressedSwatch2;

    static RColorButton()
    {
        AffectsRender<RColorButton>(SwatchColorProperty, SwatchColor2Property);
    }

    public Color? SwatchColor { get => GetValue(SwatchColorProperty); set => SetValue(SwatchColorProperty, value); }
    public Color? SwatchColor2 { get => GetValue(SwatchColor2Property); set => SetValue(SwatchColor2Property, value); }

    public event EventHandler<RoutedEventArgs>? Swatch2Click
    {
        add => AddHandler(Swatch2ClickEvent, value);
        remove => RemoveHandler(Swatch2ClickEvent, value);
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        _swatch2Rect = default;
        var c1 = SwatchColor;
        var c2 = SwatchColor2;
        if (c1 is null && c2 is null) return;

        double size = Math.Round(Bounds.Height * 0.45);
        double margin = (Bounds.Height - size) / 2;
        if (size <= 0) return;
        double radius = Math.Max(1, CornerRadius.TopLeft);

        double gap = Math.Round(margin * 0.6);
        double x = Bounds.Width - margin - size;
        IBrush edge = Brushes.Gray;
        if (this.TryFindResource("BorderMainBrush", ActualThemeVariant, out var found) && found is IBrush foundBrush)
            edge = foundBrush;
        var border = new Pen(edge, 1);

        if (c1 is { A: > 0 } col1)
        {
            ctx.DrawRectangle(new SolidColorBrush(col1), border, new Rect(x, margin, size, size), radius, radius);
            x -= gap + size;
        }

        if (c2 is { A: > 0 } col2)
        {
            _swatch2Rect = new Rect(x, margin, size, size);
            ctx.DrawRectangle(new SolidColorBrush(col2), border, _swatch2Rect, radius, radius);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this);
        _pressedSwatch2 = SwatchColor2 is not null && _swatch2Rect.Contains(p);
        base.OnPointerPressed(e);
    }

    protected override void OnClick()
    {
        if (_pressedSwatch2)
        {
            _pressedSwatch2 = false;
            RaiseEvent(new RoutedEventArgs(Swatch2ClickEvent));
            return;
        }
        base.OnClick();
    }
}
