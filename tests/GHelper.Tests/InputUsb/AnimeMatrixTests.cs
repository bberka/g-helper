using System.Drawing;
using System.Text;
using GHelper.AnimeMatrix;
using Xunit;

namespace GHelper.Tests.InputUsb;

// Golden values computed from app/AnimeMatrix/AnimeMatrixDevice.cs (geometry tables, packet layout, bitmap mapping).
public class AnimeMatrixTests
{
    static (AnimeMatrixDevice device, FakeUsbProvider usb) Create(AnimeType model = AnimeType.GA402)
    {
        var usb = new FakeUsbProvider();
        return (new AnimeMatrixDevice(model, usb), usb);
    }

    [Fact]
    public void GA402Geometry()
    {
        var (d, _) = Create();

        Assert.Equal(61, d.MaxRows);
        Assert.Equal(34, d.MaxColumns);
        Assert.Equal(11, d.FullRows);
        Assert.Equal(0, d.LedStart);

        Assert.Equal(34, d.Width(0));
        Assert.Equal(0, d.FirstX(11));
        Assert.Equal(1, d.FirstX(12));
        Assert.Equal(1, d.FirstX(13));
        Assert.Equal(2, d.FirstX(14));
        Assert.Equal(25, d.FirstX(60));

        Assert.Equal(34, d.Pitch(0));
        Assert.Equal(33, d.Pitch(12));
        Assert.Equal(32, d.Pitch(14));

        Assert.Equal(408, d.RowToLinearAddress(12));
        Assert.Equal(441, d.RowToLinearAddress(13));
        Assert.Equal(1449, d.RowToLinearAddress(61));
    }

    [Fact]
    public void GA401Geometry()
    {
        var (d, _) = Create(AnimeType.GA401);

        Assert.Equal(55, d.MaxRows);
        Assert.Equal(33, d.MaxColumns);
        Assert.Equal(5, d.FullRows);
        Assert.Equal(1, d.LedStart);

        Assert.Equal(1, d.FirstX(0));
        Assert.Equal(0, d.FirstX(1));

        Assert.Equal(33, d.Pitch(0));
        Assert.Equal(35, d.Pitch(1));
        Assert.Equal(33, d.Pitch(4));
        Assert.Equal(34, d.Pitch(5));
        Assert.Equal(33, d.Pitch(6));

        Assert.Equal(1, d.RowToLinearAddress(0));
        Assert.Equal(34, d.RowToLinearAddress(1));
        Assert.Equal(69, d.RowToLinearAddress(2));
        Assert.Equal(170, d.RowToLinearAddress(5));
    }

    [Fact]
    public void GU604Geometry()
    {
        var (d, _) = Create(AnimeType.GU604);

        Assert.Equal(92, d.MaxRows);
        Assert.Equal(39, d.MaxColumns);
        Assert.Equal(9, d.FullRows);

        Assert.Equal(1, d.FirstX(0));
        Assert.Equal(38, d.Pitch(0));
        Assert.Equal(39, d.Pitch(1));
        Assert.Equal(38, d.Pitch(10));
    }

    [Fact]
    public void StrixGeometryGrowsWithTheRow()
    {
        var (d, _) = Create(AnimeType.STRIX);

        Assert.Equal(68, d.MaxRows);
        Assert.Equal(29, d.FullRows);

        Assert.Equal(1, d.Width(0));
        Assert.Equal(34, d.Width(67));
        Assert.Equal(1, d.FirstX(30));
        Assert.Equal(15, d.Pitch(30));
    }

    static byte[] Packet(params byte[] command)
    {
        var data = new byte[640];
        data[0] = 0x5E;
        command.CopyTo(data, 1);
        return data;
    }

    [Fact]
    public void ControlPackets()
    {
        var (d, usb) = Create();

        d.WakeUp();
        d.SetBrightness(BrightnessMode.Medium);
        d.SetDisplayState(false);
        d.SetDisplayState(true);
        d.SetBuiltInAnimation(true);
        d.SetBuiltInAnimation(false, new BuiltInAnimation(
            BuiltInAnimation.Running.RogLogoGlitch, BuiltInAnimation.Sleeping.Starfield,
            BuiltInAnimation.Shutdown.SeeYa, BuiltInAnimation.Startup.StaticEmergence));

        Assert.Equal(7, usb.Sets.Count);
        Assert.All(usb.Sets, s => Assert.Equal(640, s.Length));

        Assert.Equal(Packet(Encoding.ASCII.GetBytes("ASUS Tech.Inc.")), usb.Sets[0]);
        Assert.Equal(Packet(0xC0, 0x04, 0x02), usb.Sets[1]);
        Assert.Equal(Packet(0xC3, 0x01, 0x80), usb.Sets[2]);
        Assert.Equal(Packet(0xC3, 0x01, 0x00), usb.Sets[3]);
        Assert.Equal(Packet(0xC4, 0x01, 0x00), usb.Sets[4]);
        Assert.Equal(Packet(0xC4, 0x01, 0x80), usb.Sets[5]);
        Assert.Equal(Packet(0xC5, 0x0F), usb.Sets[6]);
    }

    [Fact]
    public void BuiltInAnimationByte()
    {
        var a = new BuiltInAnimation(BuiltInAnimation.Running.BinaryBannerScroll, BuiltInAnimation.Sleeping.BannerSwipe,
            BuiltInAnimation.Shutdown.GlitchOut, BuiltInAnimation.Startup.GlitchConstruction);
        Assert.Equal(0x00, a.AsByte);

        var b = new BuiltInAnimation(BuiltInAnimation.Running.BinaryBannerScroll, BuiltInAnimation.Sleeping.Starfield,
            BuiltInAnimation.Shutdown.GlitchOut, BuiltInAnimation.Startup.StaticEmergence);
        Assert.Equal(0x0A, b.AsByte);
    }

    [Fact]
    public void PresentSendsPagedFrameAndCommit()
    {
        var (d, usb) = Create();

        d.Clear();
        d.SetLedLinear(0, 7);
        d.SetLedPlanar(5, 12, 0xAB);   // row 12 starts at 408 and its first LED is x=1 -> 408 - 1 + 5
        d.SetLedPlanar(0, 12, 0xCD);   // left of the first lit column: ignored
        d.SetLedLinear(1450, 0xEE);    // outside the buffer: ignored

        int presented = 0;
        d.OnPresent = () => presented++;
        d.Present();

        Assert.Equal(1, presented);
        Assert.Equal(4, usb.Sets.Count);

        // 1450 LEDs in pages of 490: [0,490) [490,980) [980,1450)
        Assert.Equal(new byte[] { 0x5E, 0xC0, 0x02, 0x01, 0x00, 0xEA, 0x01 }, usb.Sets[0][..7]);
        Assert.Equal(new byte[] { 0x5E, 0xC0, 0x02, 0xEB, 0x01, 0xEA, 0x01 }, usb.Sets[1][..7]);
        Assert.Equal(new byte[] { 0x5E, 0xC0, 0x02, 0xD5, 0x03, 0xD6, 0x01 }, usb.Sets[2][..7]);
        Assert.Equal(Packet(0xC0, 0x03), usb.Sets[3]);

        Assert.All(usb.Sets, s => Assert.Equal(640, s.Length));

        Assert.Equal(7, usb.Sets[0][7]);
        Assert.Equal(0xAB, usb.Sets[0][7 + 412]);
        Assert.Equal(0xAB, usb.Sets[0].Skip(7).Take(490).Max());
        Assert.DoesNotContain((byte)0xCD, usb.Sets[0]);
        Assert.DoesNotContain((byte)0xEE, usb.Sets[2]);
    }

    [Fact]
    public void DisplayOffSkipsPresentButNotifies()
    {
        var (d, usb) = Create();
        d.SetDisplayState(false);
        usb.Sets.Clear();

        int presented = 0;
        d.OnPresent = () => presented++;
        d.Present();

        Assert.Empty(usb.Sets);
        Assert.Equal(1, presented);
    }

    [Fact]
    public void DiagonalPlotMapsToPlanarRows()
    {
        var (d, usb) = Create();

        d.Clear();
        d.SetLedDiagonal(10, 5, 0x55);   // dx=10 dy=5 -> planar (2, 15), row 15 starts at 506 and first x is 2
        d.SetLedDiagonal(4, 5, 0x66);    // dx - dy == -1 is the unmapped gap
        d.Present();

        // address 506 is the 17th LED of the second page [490, 980)
        Assert.Equal(0x55, usb.Sets[1][7 + 506 - 490]);
        Assert.DoesNotContain(usb.Sets, s => s.Skip(7).Contains((byte)0x66));
    }

    [Fact]
    public void FullWhitePictureLightsEveryAddressableLed()
    {
        var (d, _) = Create();

        // 102 x 61 is exactly the frame the planar renderer targets (MaxColumns / 2 * 6 by MaxRows)
        var white = new PixelBuffer(102, 61);
        Array.Fill(white.Data, (byte)255);

        d.GenerateFrame(white, quality: InterpolationMode.NearestNeighbor);

        var led = d.LedSnapshot();
        int lit = 0;
        for (int y = 0; y < d.MaxRows; y++)
            for (int x = 0; x < d.MaxColumns; x++)
                if (led[x, y] == 255) lit++;

        Assert.Equal(1449, lit);
    }

    [Fact]
    public void HalfBlackPictureSplitsAtColumn17()
    {
        var (d, _) = Create();

        var image = new PixelBuffer(102, 61);
        for (int y = 0; y < 61; y++)
            for (int x = 51; x < 102; x++)
                image.SetPixel(x, y, Color.White);

        d.GenerateFrame(image, quality: InterpolationMode.NearestNeighbor);

        var led = d.LedSnapshot();
        for (int y = 0; y < 11; y++)
        {
            Assert.Equal(0, led[16, y]);
            Assert.Equal(255, led[17, y]);
        }
    }

    [Fact]
    public void ContrastAndGammaScaleTheBrightness()
    {
        var (d, _) = Create();

        var gray = new PixelBuffer(102, 61);
        for (int y = 0; y < 61; y++)
            for (int x = 0; x < 102; x++)
                gray.SetPixel(x, y, Color.FromArgb(100, 100, 100));

        d.GenerateFrame(gray, contrast: 100, gamma: 0, quality: InterpolationMode.NearestNeighbor);
        Assert.Equal(100, d.LedSnapshot()[10, 0]);

        d.GenerateFrame(gray, contrast: 200, gamma: 30, quality: InterpolationMode.NearestNeighbor);
        // (300 + 30) * 200 / 300
        Assert.Equal(220, d.LedSnapshot()[10, 0]);

        d.GenerateFrame(gray, contrast: 100, gamma: -285, quality: InterpolationMode.NearestNeighbor);
        // 15 / 300 * 100 = 5, under the 20 threshold -> dark
        Assert.Equal(0, d.LedSnapshot()[10, 0]);
    }

    [Fact]
    public void BarsDrawAroundTheCentreRow()
    {
        var (d, _) = Create();

        d.Clear();
        d.DrawBar(5, 4);   // planar: dx = 10, dy = 20, height 4 -> value 4 * 255 / 30 = 34, mirrored half at 255

        var led = d.LedSnapshot();
        Assert.Equal(255, led[10, 20]);   // mirrored write wins over the dim one on the centre row
        Assert.Equal(255, led[11, 20]);
        Assert.Equal(34, led[10, 21]);
        Assert.Equal(255, led[10, 19]);
        Assert.Equal(34, led[11, 22]);
        Assert.Equal(255, led[11, 18]);
        Assert.Equal(0, led[10, 25]);
    }

    [Fact]
    public void FramesAreCapturedAndReplayed()
    {
        var (d, usb) = Create();

        d.Clear();
        d.SetLedLinear(3, 1);
        d.AddFrame();
        d.Clear();
        d.SetLedLinear(3, 2);
        d.AddFrame();

        d.PresentNextFrame();
        Assert.Equal(1, usb.Sets[0][7 + 3]);

        usb.Sets.Clear();
        d.PresentNextFrame();
        Assert.Equal(2, usb.Sets[0][7 + 3]);

        usb.Sets.Clear();
        d.PresentNextFrame();   // wraps
        Assert.Equal(1, usb.Sets[0][7 + 3]);
    }

    [Fact]
    public void PacketTruncatesAtReportLength()
    {
        var packet = new AnimeMatrixPacket(new byte[] { 0xC0, 0x02 });
        packet.AppendData(Enumerable.Repeat((byte)9, 1000).ToArray());

        Assert.Equal(640, packet.Data.Length);
        Assert.Equal(0x5E, packet.Data[0]);
        Assert.Equal(9, packet.Data[638]);
        Assert.Equal(0, packet.Data[639]);
    }
}
