using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GHelper.App.Views.Keyboard;

/// <summary>
/// Draws one key of the grid: rounded tile, legend, and the LED colour as the border (legacy flat button with
/// FlatAppearance.BorderColor). Selected keys get a thicker border, keys with a binding an underlined legend.
/// </summary>
public sealed class KeyTileView : Control
{
    private readonly KeyTile _tile;
    private bool _pressed;

    public KeyTileView(KeyTile tile)
    {
        _tile = tile;
        Width = tile.Width;
        Height = tile.Height;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, tile.ToolTip);
        AutomationProperties.SetName(this, tile.ToolTip);

        tile.PropertyChanged += OnTileChanged;
    }

    public KeyTile Tile => _tile;

    public event Action<KeyTile>? Clicked;

    private void OnTileChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _tile.PropertyChanged -= OnTileChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _tile.PropertyChanged -= OnTileChanged;
        _tile.PropertyChanged += OnTileChanged;
    }

    private IBrush Resource(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : fallback;

    public override void Render(DrawingContext ctx)
    {
        var rect = new Rect(Bounds.Size);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        IBrush back = Resource("ButtonMainBrush", Brushes.DimGray);
        IBrush fore = Resource("ForeMainBrush", Brushes.White);

        double border = _tile.Selected ? 3.5 : 2;
        var pen = new Pen(new SolidColorBrush(_tile.BorderColor), border);

        ctx.DrawRectangle(back, null, rect, 3, 3);
        ctx.DrawRectangle(null, pen, rect.Deflate(border / 2), 3, 3);

        if (IsPointerOver && _pressed)
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), null, rect, 3, 3);

        var text = new FormattedText(_tile.Legend, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            Typeface.Default, 10, fore)
        {
            MaxTextWidth = Math.Max(1, rect.Width - 4),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };
        if (_tile.Bound) text.SetTextDecorations(TextDecorations.Underline);

        ctx.DrawText(text, new Point(2, (rect.Height - text.Height) / 2));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pressed = true;
        Focus();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pressed) return;
        _pressed = false;
        InvalidateVisual();
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) Clicked?.Invoke(_tile);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Enter or Key.Space)
        {
            Clicked?.Invoke(_tile);
            e.Handled = true;
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        InvalidateVisual();
    }
}
