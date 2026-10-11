using AvaloniaColor = Avalonia.Media.Color;
using DrawingColor = System.Drawing.Color;

namespace GHelper.App.Views.Peripherals;

/// <summary>Core speaks System.Drawing.Color, Avalonia its own struct.</summary>
public static class PeripheralColors
{
    public static AvaloniaColor ToAvalonia(DrawingColor c) => AvaloniaColor.FromArgb(c.A, c.R, c.G, c.B);

    public static DrawingColor ToDrawing(AvaloniaColor c) => DrawingColor.FromArgb(c.A, c.R, c.G, c.B);

    /// <summary>Opaque version: the legacy colour dialog never returned alpha.</summary>
    public static DrawingColor ToDrawingOpaque(AvaloniaColor c) => DrawingColor.FromArgb(255, c.R, c.G, c.B);
}
