using System.Drawing;
using GHelper.Overlay;

namespace GHelper.App.Views.Overlay;

/// <summary>Pure placement rules of the overlay window (device pixels), split out so they are unit testable.</summary>
public static class OverlayGeometry
{
    /// <summary>The window is kept this far inside the screen while dragging / restoring.</summary>
    public const int EdgeMargin = 5;

    /// <summary>A press and release within this distance is a click (cycles the preset), not a drag.</summary>
    public const int ClickTolerance = 5;

    /// <summary>Drag: proposed top-left clamped so the window stays on <paramref name="screen"/> (legacy WM_MOUSEMOVE).</summary>
    public static Point ClampDrag(Rectangle screen, Point proposed, Size window)
    {
        int minX = screen.Left + EdgeMargin, maxX = Math.Max(minX, screen.Right - window.Width - EdgeMargin);
        int minY = screen.Top + EdgeMargin, maxY = Math.Max(minY, screen.Bottom - window.Height - EdgeMargin);
        return new Point(Math.Clamp(proposed.X, minX, maxX), Math.Clamp(proposed.Y, minY, maxY));
    }

    public static bool IsClick(Point pressed, Point released) =>
        Math.Abs(released.X - pressed.X) <= ClickTolerance && Math.Abs(released.Y - pressed.Y) <= ClickTolerance;

    /// <summary>
    /// The window changed size (scale step, battery column): keep the edge nearest to a screen corner where it was, so a
    /// window pinned to the right / bottom grows to the left / up (legacy ApplyScale).
    /// </summary>
    public static Point KeepNearestEdge(Rectangle screen, Point location, Size oldSize, Size newSize)
    {
        var (anchor, _, _) = OverlayLayout.ToAnchor(screen, location, oldSize);
        bool right = (anchor & 1) != 0, bottom = (anchor & 2) != 0;
        int x = right ? location.X + oldSize.Width - newSize.Width : location.X;
        int y = bottom ? location.Y + oldSize.Height - newSize.Height : location.Y;
        return new Point(x, y);
    }

    /// <summary>Wheel with the drag keys down: +/- one step, clamped to the supported range.</summary>
    public static int ScaleAfterWheel(int currentPercent, int wheelDelta)
    {
        int next = currentPercent + (wheelDelta > 0 ? OverlaySettings.ScaleStepPercent : -OverlaySettings.ScaleStepPercent);
        return Math.Clamp(next, OverlaySettings.MinScalePercent, OverlaySettings.MaxScalePercent);
    }

    /// <summary>Overlay window size in DIPs for a pixel size on a display with the given render scaling.</summary>
    public static (double width, double height) ToDips(Size pixels, double renderScaling)
    {
        double s = renderScaling > 0 ? renderScaling : 1;
        return (pixels.Width / s, pixels.Height / s);
    }
}
