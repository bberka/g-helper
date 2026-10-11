using Avalonia;
using Avalonia.Media;

namespace GHelper.App.Controls;

/// <summary>Drawing helpers shared by the custom controls (port of ControlHelper.DrawGradientBorder / RButton.Shift).</summary>
public static class ChromeHelper
{
    private const float GradientHeightFraction = 0.3f;
    private const float LightGradientHeightFraction = 0.9f;
    private const byte TopFadeAlpha = 64;

    public static Color Lighten(Color c, double amount) => Color.FromArgb(
        c.A,
        (byte)Math.Clamp(c.R + (255 - c.R) * amount, 0, 255),
        (byte)Math.Clamp(c.G + (255 - c.G) * amount, 0, 255),
        (byte)Math.Clamp(c.B + (255 - c.B) * amount, 0, 255));

    /// <summary>Move a colour towards black (light backgrounds) or white (dark backgrounds): hover / pressed feedback.</summary>
    public static Color Shift(Color from, double amount)
    {
        int lum = (from.R * 30 + from.G * 59 + from.B * 11) / 100;
        byte target = lum > 128 ? (byte)0 : (byte)255;
        return Color.FromArgb(
            from.A,
            (byte)(from.R + (target - from.R) * amount),
            (byte)(from.G + (target - from.G) * amount),
            (byte)(from.B + (target - from.B) * amount));
    }

    public static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Clamp(alpha, 0, 255), c.R, c.G, c.B);

    public static Color ColorOf(IBrush? brush, Color fallback = default) =>
        brush is ISolidColorBrush s ? s.Color : fallback;

    /// <summary>
    /// Border with a lighter top edge fading into the side colour. <paramref name="stroke"/> is the pen width; the
    /// path is centred on <paramref name="bounds"/> deflated by half the stroke (Inset) or inflated (Outset).
    /// </summary>
    public static void DrawGradientBorder(DrawingContext ctx, Rect bounds, Color sideColor, double radius, double stroke,
        bool dark, bool inset, double topLighten = 0.1)
    {
        if (sideColor.A == 0 || stroke <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return;

        Color topColor = !dark && stroke <= 1.0
            ? WithAlpha(sideColor, TopFadeAlpha)
            : Lighten(sideColor, topLighten);

        double flat = Math.Max(1, stroke);
        double grad = Math.Round(bounds.Height * (dark ? GradientHeightFraction : LightGradientHeightFraction));
        double pad = stroke;
        double axisStart = bounds.Y - pad;
        double axisLen = bounds.Height + 2 * pad;
        double p1 = Math.Clamp((pad + flat) / axisLen, 0, 0.98);
        double p2 = Math.Clamp((pad + flat + grad) / axisLen, p1 + 0.01, 1);

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(topColor, 0),
                new GradientStop(topColor, p1),
                new GradientStop(sideColor, p2),
                new GradientStop(sideColor, 1),
            },
        };

        // Centre the stroke on the requested edge alignment.
        Rect path = inset ? bounds.Deflate(stroke / 2) : bounds.Inflate(stroke / 2);
        double r = Math.Max(0, radius + (inset ? -stroke / 2 : stroke / 2));
        ctx.DrawRectangle(null, new Pen(brush, stroke), path, r, r);
        _ = axisStart;
    }
}
