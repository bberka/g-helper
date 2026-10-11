using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using GHelper.AnimeMatrix;

namespace GHelper.App.Views;

/// <summary>
/// Live picture of the LEDs of the AniMe Matrix (legacy Matrix.RenderPreview into a PictureBox): a black panel with a
/// small ellipse per LED, staggered like the real panel, dim when off and gray scaled when lit. The control keeps the
/// 3:1 cell aspect of the legacy bitmap (12 x 4 units per LED), so a pointer position maps to LED cells with
/// <c>x / CellWidth</c> and <c>y / CellHeight</c>.
/// </summary>
public class MatrixPreview : Control
{
    private const int CellW = 12;
    private const int CellH = 4;

    private static readonly IImmutableBrush[] Lit = BuildLitBrushes();
    private static readonly IImmutableBrush Dim = new ImmutableSolidColorBrush(Color.FromRgb(35, 35, 35));
    private static readonly IImmutableBrush Black = new ImmutableSolidColorBrush(Colors.Black);

    private AnimeMatrixDevice? _device;

    public AnimeMatrixDevice? Device
    {
        get => _device;
        set
        {
            _device = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>LED columns incl. the one extra cell of the legacy grid (MaxColumns + 1).</summary>
    public int Columns => (_device?.MaxColumns ?? 34) + 1;

    /// <summary>LED rows incl. the one extra cell of the legacy grid (MaxRows + 1).</summary>
    public int Rows => (_device?.MaxRows ?? 61) + 1;

    /// <summary>Width of one LED column in DIPs.</summary>
    public double CellWidth => Bounds.Width / Columns;

    /// <summary>Height of one LED row in DIPs.</summary>
    public double CellHeight => Bounds.Height / Rows;

    private static IImmutableBrush[] BuildLitBrushes()
    {
        var brushes = new IImmutableBrush[256];
        for (int v = 0; v < 256; v++) brushes[v] = new ImmutableSolidColorBrush(Color.FromRgb((byte)v, (byte)v, (byte)v));
        return brushes;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? Columns * CellW : availableSize.Width;
        return new Size(width, width * Rows / Columns / 3);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(Black, null, bounds);

        var device = _device;
        if (device is null || bounds.Width <= 0 || bounds.Height <= 0) return;

        byte[,] led = device.LedSnapshot();

        double sx = bounds.Width / (Columns * CellW);
        double sy = bounds.Height / (Rows * CellH);

        double ellipseW = (CellW / 2 + 1) * sx;
        double ellipseH = (CellH - 1) * sy;
        double rx = ellipseW / 2, ry = ellipseH / 2;

        for (int y = 0; y < device.MaxRows; y++)
        {
            int last = Math.Min(device.Width(y), device.MaxColumns);
            for (int x = device.FirstX(y); x < last; x++)
            {
                byte v = led[x, y];
                double left = (x * CellW + (y % 2) * CellW / 2) * sx;
                double top = y * CellH * sy;
                context.DrawEllipse(v > 0 ? Lit[v] : Dim, null, new Point(left + rx, top + ry), rx, ry);
            }
        }
    }
}
