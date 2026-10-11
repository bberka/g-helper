using System.Runtime.CompilerServices;
using GHelper.AnimeMatrix;
using SkiaSharp;

namespace GHelper.App.Services;

/// <summary>
/// Registers the Skia based text rasterizer and picture decoder with Core (<see cref="MatrixRendering"/>) as soon as the
/// assembly is loaded, so the AniMe Matrix code can draw text / load pictures before any window exists.
/// </summary>
internal static class MatrixRendererRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        MatrixRendering.TextRenderer ??= new SkiaMatrixTextRenderer();
        MatrixRendering.ImageDecoder ??= new SkiaMatrixImageDecoder();
    }
}

/// <summary>
/// Port of the legacy GDI+ DrawString path of AnimeMatrixDevice.DrawTextBitmap: white glyphs on a transparent
/// background, 1 bit per pixel (no anti-aliasing), the text origin anchored at (0, top). Pure SkiaSharp, so it is
/// thread safe and works without a running UI loop (the matrix timers render from thread pool threads).
/// </summary>
public sealed class SkiaMatrixTextRenderer : IMatrixTextRenderer
{
    private const int MaxWidth = 8192;

    private readonly object _lock = new();
    private readonly Dictionary<string, SKTypeface> _privateFonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Family, bool Bold), SKTypeface> _typefaces = new();
    private HashSet<string>? _installed;

    public bool IsFontInstalled(string family)
    {
        if (string.IsNullOrWhiteSpace(family)) return false;

        lock (_lock)
        {
            if (_privateFonts.ContainsKey(family)) return true;

            _installed ??= new HashSet<string>(SKFontManager.Default.FontFamilies, StringComparer.OrdinalIgnoreCase);
            return _installed.Contains(family);
        }
    }

    public string? LoadFontFile(string path)
    {
        try
        {
            var typeface = SKTypeface.FromFile(path);
            if (typeface is null || string.IsNullOrEmpty(typeface.FamilyName)) return null;

            lock (_lock)
            {
                _privateFonts[typeface.FamilyName] = typeface;
                _typefaces.Clear();
            }

            return typeface.FamilyName;
        }
        catch
        {
            return null;
        }
    }

    private SKTypeface Resolve(MatrixFontSpec font)
    {
        lock (_lock)
        {
            if (_privateFonts.TryGetValue(font.Family, out var privateFace)) return privateFace;

            var key = (font.Family, font.Bold);
            if (!_typefaces.TryGetValue(key, out var face))
            {
                face = SKTypeface.FromFamilyName(font.Family, font.Bold ? SKFontStyle.Bold : SKFontStyle.Normal)
                       ?? SKTypeface.Default;
                _typefaces[key] = face;
            }

            return face;
        }
    }

    /// <summary>
    /// em size to use so that user chosen families line up with the matrix font: legacy
    /// <c>1.1 * emHeight / cellAscent</c>, expressed through the ascent ratio of a probe font.
    /// </summary>
    private static float NormalizedSize(SKTypeface face, float sizePx)
    {
        using var probe = new SKFont(face, 100);
        float ascent = -probe.Metrics.Ascent;
        if (ascent <= 0) return sizePx;
        return sizePx * 1.1f * 100f / ascent;
    }

    public PixelBuffer? DrawText(string text, MatrixFontSpec font, float sizePx, int height, float top)
    {
        if (string.IsNullOrEmpty(text) || height <= 0 || sizePx <= 0) return null;

        var face = Resolve(font);
        float em = font.NormalizeAscent ? NormalizedSize(face, sizePx) : sizePx;

        using var skFont = new SKFont(face, em)
        {
            Edging = SKFontEdging.Alias,
            Subpixel = false,
            Hinting = SKFontHinting.Normal,
            ForceAutoHinting = false,
        };

        // GDI+ MeasureString / DrawString keep ~1/6 em of padding on both sides of the text
        float pad = em / 6f;
        float advance = skFont.MeasureText(text);
        int width = (int)Math.Ceiling(advance + 2 * pad);
        if (width <= 0) return null;
        width = Math.Min(width, MaxWidth);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = false })
        {
            canvas.Clear(SKColors.Transparent);

            // DrawString puts the top of the line box at "top"; Skia wants the baseline
            float baseline = top - skFont.Metrics.Ascent;
            canvas.DrawText(text, pad, baseline, skFont, paint);
        }

        var buffer = new PixelBuffer(width, height);
        bitmap.GetPixelSpan().CopyTo(buffer.Data);
        return buffer;
    }
}
