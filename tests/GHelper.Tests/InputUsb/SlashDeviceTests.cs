using System.Text;
using GHelper.AnimeMatrix;
using Xunit;

namespace GHelper.Tests.InputUsb;

// Golden packets from app/AnimeMatrix/SlashDevice.cs: 128 byte feature reports, report id 0x5E (0x5D on the Aura interface).
public class SlashDeviceTests
{
    static (SlashDevice device, FakeUsbProvider usb) Create(bool longModel = false)
    {
        var usb = new FakeUsbProvider();
        return (new SlashDevice(usb, longModel), usb);
    }

    static byte[] Report(params byte[] command) => ReportWithId(0x5E, command);

    static byte[] ReportWithId(byte id, params byte[] command)
    {
        var data = new byte[128];
        data[0] = id;
        command.CopyTo(data, 1);
        return data;
    }

    [Fact]
    public void WakeUpSequence()
    {
        var (d, usb) = Create();
        d.WakeUp();

        Assert.Equal(3, usb.Sets.Count);
        Assert.Equal(Report(Encoding.ASCII.GetBytes("ASUS Tech.Inc.")), usb.Sets[0]);
        Assert.Equal(Report(0xC2), usb.Sets[1]);
        Assert.Equal(Report(0xD1, 0x01, 0x00, 0x01), usb.Sets[2]);
    }

    [Fact]
    public void InitEnableAndSave()
    {
        var (d, usb) = Create();

        d.Init();
        d.SetEnabled(true);
        d.SetEnabled(false);
        d.Save();

        Assert.Equal(Report(0xD7, 0x00, 0x00, 0x01, 0xAC), usb.Sets[0]);
        Assert.Equal(Report(0xD2, 0x02, 0x01, 0x08, 0xAB), usb.Sets[1]);
        Assert.Equal(Report(0xD8, 0x02, 0x00, 0x01, 0x00), usb.Sets[2]);
        Assert.Equal(Report(0xD8, 0x02, 0x00, 0x01, 0x80), usb.Sets[3]);
        Assert.Equal(Report(0xD4, 0x00, 0x00, 0x01, 0xAB), usb.Sets[4]);
    }

    [Fact]
    public void OptionsEncodeBrightnessAndInterval()
    {
        var (d, usb) = Create();

        d.SetOptions(true, brightness: 3, interval: 4);
        d.SetOptions(false, brightness: 1, interval: 0);

        Assert.Equal(Report(0xD3, 0x03, 0x01, 0x08, 0xAB, 0xFF, 0x01, 0x01, 0x06, 0xFF, 0xFF, 0x04), usb.Sets[0]);
        Assert.Equal(Report(0xD3, 0x03, 0x01, 0x08, 0xAB, 0xFF, 0x01, 0x00, 0x06, 0x55, 0xFF, 0x00), usb.Sets[1]);
    }

    [Fact]
    public void ModeFallsBackToFullRecordWhenDeviceReportsNothing()
    {
        var (d, usb) = Create();
        d.SetMode(SlashMode.Bounce);

        Assert.Equal(2, usb.Sets.Count);
        Assert.Equal(Report(0xD2, 0x03, 0x00, 0x0C), usb.Sets[0]);
        Assert.Equal(Report(0xD3, 0x04, 0x00, 0x0C, 0x01, 0x10, 0x02, 0x42, 0x03, 0x13, 0x04, 0x11, 0x05, 0x12, 0x06, 0x13), usb.Sets[1]);
    }

    [Fact]
    public void ModeRewritesTheRecordTheDeviceReturned()
    {
        var (d, usb) = Create();
        usb.OnGet = request =>
        {
            var response = (byte[])request.Clone();
            response[5] = 0x01;
            response[6] = 0x07;
            response[7] = 0x42;
            return response;
        };

        d.SetMode(SlashMode.Flux);

        Assert.Equal(2, usb.Sets.Count);
        var written = usb.Sets[1];
        Assert.Equal(0xD3, written[1]);
        Assert.Equal(0x04, written[2]);
        Assert.Equal(0x25, written[6]);   // Flux
        Assert.Equal(0x42, written[7]);   // everything else is preserved
    }

    [Theory]
    [InlineData(SlashMode.Static, 0x06)]
    [InlineData(SlashMode.Bounce, 0x10)]
    [InlineData(SlashMode.BitStream, 0x1D)]
    [InlineData(SlashMode.Transmission, 0x1A)]
    [InlineData(SlashMode.Phantom, 0x24)]
    [InlineData(SlashMode.GameOver, 0x42)]
    [InlineData(SlashMode.FX3, 0x62)]
    [InlineData(SlashMode.BatteryLevel, 0x00)]
    public void ModeCodes(SlashMode mode, int code)
    {
        Assert.Equal((byte)code, SlashDevice.GetModeCode(mode));
    }

    [Fact]
    public void CustomPatternIsFourReports()
    {
        var (d, usb) = Create();
        d.SetCustom(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, null);

        Assert.Equal(4, usb.Sets.Count);
        Assert.Equal(Report(0xD2, 0x02, 0x01, 0x08, 0xAC), usb.Sets[0]);
        Assert.Equal(Report(0xD3, 0x03, 0x01, 0x08, 0xAC, 0xFF, 0xFF, 0x01, 0x05, 0xFF, 0xFF), usb.Sets[1]);
        Assert.Equal(Report(0xD4, 0x00, 0x00, 0x01, 0xAC), usb.Sets[2]);
        // only as many bytes as the lightbar has LEDs (7)
        Assert.Equal(Report(0xD3, 0x00, 0x00, 0x07, 1, 2, 3, 4, 5, 6, 7), usb.Sets[3]);
    }

    [Fact]
    public void LongLightbarHas35Leds()
    {
        var (d, usb) = Create(longModel: true);
        d.ContinueCustom(Enumerable.Range(1, 40).Select(i => (byte)i).ToArray(), null);

        var packet = usb.Sets[0];
        Assert.Equal(0x23, packet[4]);
        Assert.Equal(35, packet[5 + 34]);
        Assert.Equal(0, packet[5 + 35]);
    }

    [Fact]
    public void AudioPatternMixesBassAndTreble()
    {
        var (d, usb) = Create();

        // 7 LEDs of 14.28 each; bass 30 fills two and a tenth, written from the far end
        d.SetAudioPattern(3, bass: 30, treble: 0);
        Assert.Equal(Report(0xD3, 0x00, 0x00, 0x07, 0, 0, 0, 0, 0x09, 0x60, 0x60), usb.Sets[0]);

        usb.Sets.Clear();
        d.SetAudioPattern(3, bass: 200, treble: 200);
        // 0x60 | 0xF0
        Assert.Equal(Report(0xD3, 0x00, 0x00, 0x07, 0xF0, 0xF0, 0xF0, 0xF0, 0xF0, 0xF0, 0xF0), usb.Sets[0]);
    }

    [Fact]
    public void PowerAndLidSettings()
    {
        var (d, usb) = Create();

        d.SetPowerSaving(true, 20);
        d.SetPowerSaving(false, 100);
        d.SetLightingOnBattery(true);
        d.SetLightingOnLidClose(false);
        d.SetBatteryAnimation(true);

        Assert.Equal(Report(0xD5, 0x00, 0x00, 0x03, 0x00, 0x14), usb.Sets[0]);
        Assert.Equal(Report(0xD5, 0x00, 0x00, 0x03, 0x80, 0xFF), usb.Sets[1]);
        Assert.Equal(Report(0xD8, 0x01, 0x00, 0x01, 0x00), usb.Sets[2]);
        Assert.Equal(Report(0xD8, 0x00, 0x00, 0x02, 0xA5, 0x80), usb.Sets[3]);
        Assert.Equal(Report(0xD2, 0x02, 0x01, 0x08, 0xA3), usb.Sets[4]);
        Assert.Equal(Report(0xD3, 0x03, 0x01, 0x08, 0xA3, 0x02, 0xFF, 0x01, 0x02, 0xFF, 0x02), usb.Sets[5]);
    }

    [Fact]
    public void FlagsReadAndWriteTheRecord()
    {
        var (d, usb) = Create();
        usb.OnGet = request =>
        {
            var response = (byte[])request.Clone();
            response[6] = 0x01;
            response[8] = 0x01;
            return response;
        };

        Assert.True(d.GetFlag(0xAB));

        usb.Sets.Clear();
        d.SetFlag(0xAB, false);

        // request for the record, then the same record rewritten as a D3 03 write with the flag cleared
        Assert.Equal(Report(0xD2, 0x02, 0x01, 0x08, 0xAB), usb.Sets[0]);
        var written = usb.Sets[1];
        Assert.Equal(0xD3, written[1]);
        Assert.Equal(0x03, written[2]);
        Assert.Equal(0x00, written[8]);
    }

    [Fact]
    public void AuraInterfaceUsesReportId5D()
    {
        var usb = new FakeUsbProvider();
        var d = new SlashDeviceAura(usb, longModel: false);

        d.Save();

        Assert.Equal(ReportWithId(0x5D, 0xD4, 0x00, 0x00, 0x01, 0xAB), usb.Sets[0]);
        Assert.Equal(0x19B6, d.ProductID());
    }
}
