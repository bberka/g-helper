using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using GHelper.App.Controls;

namespace GHelper.App.Views.Mouse;

/// <summary>
/// DPI profile button (legacy RButton + the Paint/MouseMove delete hack): while the pointer is over a deletable profile a
/// small X is drawn in the top right corner, and a click on it raises <see cref="DeleteCommand"/> instead of Click.
/// </summary>
public class DpiSlotButton : RButton
{
    /// <summary>Size of the delete hit area in DIPs (legacy: 16 * scale + 5).</summary>
    private const double DeleteArea = 21;

    public static readonly StyledProperty<bool> CanDeleteProperty =
        AvaloniaProperty.Register<DpiSlotButton, bool>(nameof(CanDelete));

    public static readonly StyledProperty<ICommand?> DeleteCommandProperty =
        AvaloniaProperty.Register<DpiSlotButton, ICommand?>(nameof(DeleteCommand));

    private bool _pressedInDeleteArea;

    static DpiSlotButton()
    {
        AffectsRender<DpiSlotButton>(CanDeleteProperty);
    }

    protected override Type StyleKeyOverride => typeof(RButton);

    public bool CanDelete { get => GetValue(CanDeleteProperty); set => SetValue(CanDeleteProperty, value); }
    public ICommand? DeleteCommand { get => GetValue(DeleteCommandProperty); set => SetValue(DeleteCommandProperty, value); }

    private bool IsInDeleteArea(Point p) => CanDelete && p.X > Bounds.Width - DeleteArea && p.Y < DeleteArea;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _pressedInDeleteArea = IsInDeleteArea(e.GetPosition(this));
        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (CanDelete) InvalidateVisual();
    }

    protected override void OnClick()
    {
        if (_pressedInDeleteArea)
        {
            _pressedInDeleteArea = false;
            if (DeleteCommand?.CanExecute(null) == true) DeleteCommand.Execute(null);
            return;
        }

        base.OnClick();
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        if (!CanDelete || !IsPointerOver) return;

        // gray-white X (legacy cross_23 tinted 170,170,170)
        double size = 9;
        double x = Bounds.Width - 5 - size - 2;
        double y = 5 + 2;
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(170, 170, 170)), 1.6, lineCap: PenLineCap.Round);
        ctx.DrawLine(pen, new Point(x, y), new Point(x + size, y + size));
        ctx.DrawLine(pen, new Point(x + size, y), new Point(x, y + size));
    }
}
