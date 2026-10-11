using System.Drawing;
using GHelper.AnimeMatrix;
using Xunit;

namespace GHelper.Tests.InputUsb;

public class PixelBufferTests
{
    [Fact]
    public void FromBgraSwapsChannels()
    {
        var buffer = PixelBuffer.FromBgra(2, 1, new byte[] { 3, 2, 1, 255, 30, 20, 10, 128 });

        Assert.Equal(Color.FromArgb(255, 1, 2, 3), buffer.GetPixel(0, 0));
        Assert.Equal(Color.FromArgb(128, 10, 20, 30), buffer.GetPixel(1, 0));
    }

    [Fact]
    public void GrayIsPlainChannelAverageIgnoringAlpha()
    {
        var buffer = new PixelBuffer(1, 1);
        buffer.SetPixel(0, 0, Color.FromArgb(10, 30, 60, 90));

        Assert.Equal(60, buffer.Gray(0, 0));
    }

    [Fact]
    public void FillAndOutlineRectangles()
    {
        var buffer = new PixelBuffer(10, 10);
        buffer.FillRectangle(Color.White, 2, 2, 3, 2);

        Assert.Equal(Color.White.ToArgb(), buffer.GetPixel(2, 2).ToArgb());
        Assert.Equal(Color.White.ToArgb(), buffer.GetPixel(4, 3).ToArgb());
        Assert.Equal(0, buffer.GetPixel(5, 3).A);
        Assert.Equal(0, buffer.GetPixel(4, 4).A);

        var outline = new PixelBuffer(10, 10);
        outline.DrawRectangle(Color.White, 1, 1, 4, 3);   // GDI+ covers 5 x 4 pixels

        Assert.Equal(255, outline.GetPixel(5, 4).A);
        Assert.Equal(255, outline.GetPixel(1, 1).A);
        Assert.Equal(0, outline.GetPixel(3, 3).A);
        Assert.Equal(0, outline.GetPixel(6, 4).A);
    }

    [Fact]
    public void DrawKeepsTransparentPixelsOfTheSource()
    {
        var dest = new PixelBuffer(4, 1);
        dest.SetPixel(0, 0, Color.Red);
        dest.SetPixel(1, 0, Color.Red);

        var src = new PixelBuffer(2, 1);
        src.SetPixel(0, 0, Color.White);

        dest.Draw(src, 0, 0);

        Assert.Equal(Color.White.ToArgb(), dest.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Red.ToArgb(), dest.GetPixel(1, 0).ToArgb());
    }

    [Fact]
    public void NearestNeighbourScalesBlocks()
    {
        var src = new PixelBuffer(2, 1);
        src.SetPixel(0, 0, Color.Black);
        src.SetPixel(1, 0, Color.White);

        var dest = new PixelBuffer(4, 1);
        dest.DrawScaled(src, 0, 0, 4, 1, InterpolationMode.NearestNeighbor);

        Assert.Equal(new[] { 0, 0, 255, 255 }, Enumerable.Range(0, 4).Select(x => (int)dest.GetPixel(x, 0).R));
    }

    [Fact]
    public void ScaledDrawLeavesOutsideUntouched()
    {
        var src = new PixelBuffer(1, 1);
        src.SetPixel(0, 0, Color.White);

        var dest = new PixelBuffer(6, 1);
        dest.DrawScaled(src, 2, 0, 2, 1, InterpolationMode.NearestNeighbor);

        Assert.Equal(new[] { 0, 0, 255, 255, 0, 0 }, Enumerable.Range(0, 6).Select(x => (int)dest.GetPixel(x, 0).R));
    }

    [Fact]
    public void DownscaleAveragesTheCoveredArea()
    {
        var src = new PixelBuffer(4, 2);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 4; x++)
                src.SetPixel(x, y, x < 2 ? Color.FromArgb(255, 200, 100, 0) : Color.FromArgb(255, 0, 0, 0));

        var dest = src.Resize(2, 1);

        Assert.Equal(Color.FromArgb(255, 200, 100, 0).ToArgb(), dest.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.FromArgb(255, 0, 0, 0).ToArgb(), dest.GetPixel(1, 0).ToArgb());

        Assert.Equal(Color.FromArgb(100, 50, 0).ToArgb(), src.Resize(1, 1).GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void AverageColorIsTheMeanOfAllPixels()
    {
        var buffer = new PixelBuffer(2, 1);
        buffer.SetPixel(0, 0, Color.FromArgb(255, 100, 0, 50));
        buffer.SetPixel(1, 0, Color.FromArgb(255, 200, 100, 51));

        Assert.Equal(Color.FromArgb(150, 50, 50).ToArgb(), buffer.AverageColor().ToArgb());
    }

    [Fact]
    public void InterpolationModeKeepsGdiValues()
    {
        Assert.Equal(0, (int)InterpolationMode.Default);
        Assert.Equal(4, (int)InterpolationMode.Bicubic);
        Assert.Equal(5, (int)InterpolationMode.NearestNeighbor);
        Assert.Equal(7, (int)InterpolationMode.HighQualityBicubic);
    }
}
