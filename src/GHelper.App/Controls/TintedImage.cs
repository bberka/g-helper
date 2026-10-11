using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace GHelper.App.Controls;

/// <summary>
/// Draws the alpha channel of a (black glyph) PNG filled with the foreground colour. The legacy WinForms app inverted
/// its icons when the theme changed (ControlHelper.AdjustImage); tinting with the inherited foreground does the same
/// without a second set of bitmaps and also works for the "white" variants.
/// </summary>
public class TintedImage : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<TintedImage, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<TintedImage>();

    static TintedImage()
    {
        AffectsRender<TintedImage>(SourceProperty, ForegroundProperty);
        AffectsMeasure<TintedImage>(SourceProperty);
    }

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Source is not { } bmp) return default;
        // Icons are authored for 192 dpi (2x): half the pixel size is the natural DIP size.
        double w = bmp.Size.Width / 2, h = bmp.Size.Height / 2;
        double scale = 1;
        if (!double.IsInfinity(availableSize.Width) && w > availableSize.Width) scale = Math.Min(scale, availableSize.Width / w);
        if (!double.IsInfinity(availableSize.Height) && h > availableSize.Height) scale = Math.Min(scale, availableSize.Height / h);
        return new Size(w * scale, h * scale);
    }

    public override void Render(DrawingContext context)
    {
        if (Source is not { } bmp || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var rect = new Rect(Bounds.Size);
        var brush = new ImageBrush(bmp)
        {
            Stretch = Stretch.Uniform,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
        };
        using (context.PushOpacityMask(brush, rect))
            context.DrawRectangle(Foreground ?? Brushes.Black, null, rect);
    }
}
