using GHelper.AnimeMatrix;
using GHelper.App.Services;
using SkiaSharp;
using Xunit;

namespace GHelper.App.Tests;

/// <summary>
/// The Skia text rasterizer / picture decoder behind MatrixRendering (the legacy GDI+ DrawString and Image.FromStream
/// paths): glyph masks must be 1 bit white-on-transparent and GIFs must report their frames and delay.
/// </summary>
public class MatrixRendererTests
{
    private static int LitCount(PixelBuffer buffer)
    {
        int lit = 0;
        for (int i = 3; i < buffer.Data.Length; i += 4)
            if (buffer.Data[i] != 0) lit++;
        return lit;
    }

    [Fact]
    public void Module_initializer_registers_renderer_and_decoder()
    {
        // touching any type of the assembly runs its module initializer
        _ = new SkiaMatrixTextRenderer();

        Assert.NotNull(MatrixRendering.TextRenderer);
        Assert.NotNull(MatrixRendering.ImageDecoder);
    }

    [Fact]
    public void Text_renders_white_glyphs_without_antialiasing()
    {
        var renderer = new SkiaMatrixTextRenderer();

        var buffer = renderer.DrawText("HELLO 123", new MatrixFontSpec("Arial", Bold: true), 15, 50, 10);

        Assert.NotNull(buffer);
        Assert.Equal(50, buffer!.Height);
        Assert.True(buffer.Width > 40, "9 characters at 15 px must be wider than 40 px, was " + buffer.Width);

        int lit = 0;
        for (int i = 0; i < buffer.Data.Length; i += 4)
        {
            byte a = buffer.Data[i + 3];
            Assert.True(a == 0 || a == 255, "anti-aliased pixel with alpha " + a);
            if (a == 255)
            {
                lit++;
                Assert.Equal(255, buffer.Data[i]);
                Assert.Equal(255, buffer.Data[i + 1]);
                Assert.Equal(255, buffer.Data[i + 2]);
            }
            else
            {
                // legacy matrix brightness is (R+G+B)/3: untouched pixels must be black
                Assert.Equal(0, buffer.Data[i] + buffer.Data[i + 1] + buffer.Data[i + 2]);
            }
        }

        Assert.InRange(lit, 40, buffer.Width * buffer.Height / 2);
    }

    [Fact]
    public void Text_is_anchored_at_the_requested_top()
    {
        var renderer = new SkiaMatrixTextRenderer();

        var high = renderer.DrawText("H", new MatrixFontSpec("Arial"), 15, 60, 5)!;
        var low = renderer.DrawText("H", new MatrixFontSpec("Arial"), 15, 60, 30)!;

        int FirstRow(PixelBuffer b)
        {
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++)
                    if (b.Data[(y * b.Width + x) * 4 + 3] != 0) return y;
            return -1;
        }

        int a = FirstRow(high), b = FirstRow(low);
        Assert.True(a >= 0 && b >= 0);
        Assert.Equal(25, b - a);
    }

    [Fact]
    public void Longer_text_is_wider_and_empty_text_is_null()
    {
        var renderer = new SkiaMatrixTextRenderer();
        var font = new MatrixFontSpec("Arial", NormalizeAscent: true);

        var shortText = renderer.DrawText("Hi", font, 15, 40, 5)!;
        var longText = renderer.DrawText("Hi there, matrix", font, 15, 40, 5)!;

        Assert.True(longText.Width > shortText.Width * 2);
        Assert.True(LitCount(longText) > LitCount(shortText));
        Assert.Null(renderer.DrawText("", font, 15, 40, 5));
    }

    [Fact]
    public void Larger_size_lights_more_pixels()
    {
        var renderer = new SkiaMatrixTextRenderer();

        var small = renderer.DrawText("MATRIX", new MatrixFontSpec("Arial"), 10, 50, 5)!;
        var big = renderer.DrawText("MATRIX", new MatrixFontSpec("Arial"), 25, 50, 5)!;

        Assert.True(LitCount(big) > LitCount(small));
    }

    [Fact]
    public void Font_install_check_and_missing_font_file()
    {
        var renderer = new SkiaMatrixTextRenderer();

        Assert.False(renderer.IsFontInstalled("Definitely Not A Font 12345"));
        Assert.False(renderer.IsFontInstalled(""));
        Assert.Null(renderer.LoadFontFile(Path.Combine(Path.GetTempPath(), "does-not-exist.otf")));
    }

    // GIF89a 2x1, global palette black/red/green/blue, two frames (red, green), 12 centiseconds each, uncompressed LZW
    private static byte[] TwoFrameGif() =>
    [
        (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a',
        0x02, 0x00, 0x01, 0x00, 0x91, 0x00, 0x00,
        0, 0, 0,  255, 0, 0,  0, 255, 0,  0, 0, 255,
        // frame 0: all red
        0x21, 0xF9, 0x04, 0x00, 0x0C, 0x00, 0x00, 0x00,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01, 0x00, 0x00,
        0x02, 0x02, 0x4C, 0x0A, 0x00,
        // frame 1: all green
        0x21, 0xF9, 0x04, 0x00, 0x0C, 0x00, 0x00, 0x00,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01, 0x00, 0x00,
        0x02, 0x02, 0x94, 0x0A, 0x00,
        0x3B,
    ];

    [Fact]
    public void Gif_decodes_all_frames_and_reports_delay()
    {
        var path = Path.Combine(Path.GetTempPath(), "ghelper-matrix-" + Guid.NewGuid().ToString("N") + ".gif");
        File.WriteAllBytes(path, TwoFrameGif());

        try
        {
            var image = new SkiaMatrixImageDecoder().Load(path);

            Assert.NotNull(image);
            Assert.Equal(2, image!.Frames.Count);
            Assert.Equal(120, image.FrameDelayMs);

            var first = image.Frames[0];
            Assert.Equal(2, first.Width);
            Assert.Equal(1, first.Height);
            Assert.Equal(System.Drawing.Color.FromArgb(255, 255, 0, 0), first.GetPixel(0, 0));
            Assert.Equal(System.Drawing.Color.FromArgb(255, 0, 255, 0), image.Frames[1].GetPixel(1, 0));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Still_picture_has_one_frame_without_delay_and_is_shrunk_to_the_cap()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(900, 450, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(new SKColor(10, 200, 30));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        var path = Path.Combine(Path.GetTempPath(), "ghelper-matrix-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, encoded.ToArray());

        try
        {
            var decoded = new SkiaMatrixImageDecoder().Load(path);

            Assert.NotNull(decoded);
            Assert.Single(decoded!.Frames);
            Assert.Equal(0, decoded.FrameDelayMs);
            Assert.Equal(SkiaMatrixImageDecoder.MaxSide, decoded.First.Width);
            Assert.Equal(SkiaMatrixImageDecoder.MaxSide / 2, decoded.First.Height);

            var px = decoded.First.GetPixel(10, 10);
            Assert.Equal(10, px.R);
            Assert.Equal(200, px.G);
            Assert.Equal(30, px.B);
            Assert.Equal(255, px.A);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Decoder_returns_null_for_missing_or_broken_files()
    {
        var decoder = new SkiaMatrixImageDecoder();
        Assert.Null(decoder.Load(Path.Combine(Path.GetTempPath(), "no-such-picture.png")));

        var path = Path.Combine(Path.GetTempPath(), "ghelper-matrix-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllText(path, "this is not a picture");
        try { Assert.Null(decoder.Load(path)); }
        finally { File.Delete(path); }
    }
}
