namespace GHelper.AnimeMatrix
{
    /// <summary>Font request for <see cref="IMatrixTextRenderer.DrawText"/>.</summary>
    /// <param name="Family">Font family name.</param>
    /// <param name="Bold">Bold weight (the Consolas fallback of the legacy code).</param>
    /// <param name="NormalizeAscent">
    /// Scale the em size by 1.1 * emHeight / cellAscent of the family (legacy TextFont) so that user picked fonts line up
    /// with the default matrix font. The default / fallback fonts are used as is.
    /// </param>
    public readonly record struct MatrixFontSpec(string Family, bool Bold = false, bool NormalizeAscent = false);

    /// <summary>
    /// Text rasterizer provided by the UI layer (the legacy code used System.Drawing.Graphics.DrawString).
    /// Core only needs a lit-pixel mask, so implementations draw white glyphs on a transparent background
    /// without anti-aliasing (TextRenderingHint.SingleBitPerPixel).
    /// </summary>
    public interface IMatrixTextRenderer
    {
        /// <summary>Is a font family with this name installed in the OS.</summary>
        bool IsFontInstalled(string family);

        /// <summary>Register a font file for private use and return its family name, null on failure.</summary>
        string? LoadFontFile(string path);

        /// <summary>
        /// Render a single line. Returns a buffer of (measured text width) x <paramref name="height"/> pixels with the glyph
        /// origin at y = <paramref name="top"/>, x = 0, or null when the text has no width.
        /// </summary>
        PixelBuffer? DrawText(string text, MatrixFontSpec font, float sizePx, int height, float top);
    }

    /// <summary>Decoded (possibly animated) picture for the matrix picture mode.</summary>
    public sealed class MatrixImage
    {
        public MatrixImage(IReadOnlyList<PixelBuffer> frames, int frameDelayMs = 0)
        {
            if (frames.Count == 0) throw new ArgumentException("At least one frame required", nameof(frames));
            Frames = frames;
            FrameDelayMs = frameDelayMs;
        }

        public IReadOnlyList<PixelBuffer> Frames { get; }

        /// <summary>GIF frame delay in milliseconds (legacy: property item 0x5100 * 10), 0 for still pictures.</summary>
        public int FrameDelayMs { get; }

        public PixelBuffer First => Frames[0];
    }

    /// <summary>Image file decoder provided by the UI layer (png/jpg/bmp/gif).</summary>
    public interface IMatrixImageDecoder
    {
        MatrixImage? Load(string fileName);
    }

    /// <summary>Registration points for the UI layer. Set once at start up; null means the feature is unavailable.</summary>
    public static class MatrixRendering
    {
        public static IMatrixTextRenderer? TextRenderer { get; set; }
        public static IMatrixImageDecoder? ImageDecoder { get; set; }
    }
}
