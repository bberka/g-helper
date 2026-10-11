using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GHelper.App.Views.Overlay;

/// <summary>
/// Surface of the overlay window: draws the current <see cref="OverlayFrame"/> in device pixels, independent of the
/// display scaling (the legacy overlay deliberately ignores OS DPI; its size comes from the "overlay_scale_percent" option).
/// </summary>
public sealed class OverlayView : Control
{
    private OverlayFrame? _frame;

    public OverlayView()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = false;
    }

    public OverlayFrame? Frame
    {
        get => _frame;
        set
        {
            _frame = value;
            InvalidateVisual();
        }
    }

    /// <summary>Device pixels per DIP of the window showing this control.</summary>
    public double PixelsPerDip => (TopLevel.GetTopLevel(this)?.RenderScaling) is > 0 and var s ? s : 1.0;

    public override void Render(DrawingContext context)
    {
        if (_frame is null) return;

        double toDip = 1.0 / PixelsPerDip;
        using (context.PushTransform(Matrix.CreateScale(toDip, toDip)))
            OverlayRenderer.Render(context, _frame);
    }
}
