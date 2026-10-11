using System.Drawing;
using GHelper.Peripherals;
using GHelper.Peripherals.Headset;
using GHelper.Peripherals.Headset.Models;
using GHelper.Peripherals.Keyboard;
using GHelper.Peripherals.Keyboard.Models;
using GHelper.USB;
using Xunit;

namespace GHelper.Tests.Peripherals;

internal class ProbeKeyboard : AsusKeyboard
{
    public ProbeKeyboard(byte reportId = 0x00) : base(0x0B05, 0x4321, "mi_01", reportId) { }

    public override string GetDisplayName() => "Probe Keyboard";

    public byte[] Aura(KeyboardLightingMode mode, Color c1, Color c2, AuraSpeed speed, int brightness)
        => BuildAuraPacket(mode, c1, c2, speed, brightness);
    public byte[]? Binding(ushort source, ushort action) => KeyBindingPacket(source, action);
    public int BatteryFrom(byte[] reply) => ParseBattery(reply);
}

internal class ProbeTufK1 : TUFK1
{
    public byte[] Aura(KeyboardLightingMode mode, Color c1, Color c2, AuraSpeed speed, int brightness)
        => BuildAuraPacket(mode, c1, c2, speed, brightness);
}

internal class ProbeHeadset : AsusHeadset
{
    public ProbeHeadset() : base(0x0B05, 0x4321) { }

    public override string GetDisplayName() => "Probe Headset";

    public byte[] Lighting(HeadsetLightingMode mode, int brightness, Color color) => LightingPacket(mode, brightness, color);
    public void BatteryFrom(byte[] reply) => ParseBattery(reply);
    public bool ChargingFrom(byte[] reply) => ParseCharging(reply);
    public void LightingFrom(byte[] config) => ParseLighting(config);
}

internal class ProbeCetra : CetraRgb
{
    public byte[] Lighting(HeadsetLightingMode mode, int brightness, Color color) => LightingPacket(mode, brightness, color);
}

public class KeyboardProtocolTests
{
    [Fact]
    public void StaticLightingPacketCarriesColorSpeedAndBrightness()
    {
        var kb = new ProbeKeyboard();

        byte[] packet = kb.Aura(KeyboardLightingMode.Static, Color.FromArgb(1, 2, 3), Color.Black, AuraSpeed.Normal, 80);

        Assert.Equal(19, packet.Length);
        Assert.Equal(new byte[] { 0x00, 0x51, 0x2C, 0x00, 0x00 }, packet.Take(5).ToArray());
        Assert.Equal(30, packet[5]);   // normal speed
        Assert.Equal(80, packet[6]);   // brightness %
        Assert.Equal(0x00, packet[7]); // explicit colour
        Assert.Equal(0x02, packet[9]);
        Assert.Equal(new byte[] { 1, 2, 3 }, packet.Skip(10).Take(3).ToArray());
    }

    [Theory]
    [InlineData(AuraSpeed.Slow, 255)]
    [InlineData(AuraSpeed.Normal, 30)]
    [InlineData(AuraSpeed.Fast, 10)]
    public void AuraSpeedMapsToDeviceSpeedByte(AuraSpeed speed, byte expected)
    {
        var kb = new ProbeKeyboard();
        Assert.Equal(expected, kb.Aura(KeyboardLightingMode.Static, Color.Red, Color.Black, speed, 100)[5]);
    }

    [Fact]
    public void BrightnessIsCappedAt100()
    {
        var kb = new ProbeKeyboard();
        Assert.Equal(100, kb.Aura(KeyboardLightingMode.Static, Color.Red, Color.Black, AuraSpeed.Normal, 250)[6]);
    }

    [Fact]
    public void BlackColorSelectsRandomColorMode()
    {
        var kb = new ProbeKeyboard();
        Assert.Equal(1, kb.Aura(KeyboardLightingMode.Breathing, Color.Black, Color.Black, AuraSpeed.Normal, 100)[7]);
    }

    [Fact]
    public void SecondColorSelectsDualColorModeOnlyWhereSupported()
    {
        var kb = new ProbeKeyboard();

        byte[] breathing = kb.Aura(KeyboardLightingMode.Breathing, Color.Red, Color.Blue, AuraSpeed.Normal, 100);
        Assert.Equal(16, breathing[7]);
        Assert.Equal(new byte[] { 0, 0, 255 }, breathing.Skip(13).Take(3).ToArray());

        byte[] staticMode = kb.Aura(KeyboardLightingMode.Static, Color.Red, Color.Blue, AuraSpeed.Normal, 100);
        Assert.Equal(0, staticMode[7]);
    }

    [Theory]
    [InlineData(KeyboardLightingMode.Wave)]
    [InlineData(KeyboardLightingMode.Ripple)]
    public void WaveAndRippleUseRandomColor(KeyboardLightingMode mode)
    {
        var kb = new ProbeKeyboard();
        byte[] packet = kb.Aura(mode, Color.FromArgb(9, 9, 9), Color.Black, AuraSpeed.Fast, 100);

        Assert.Equal(1, packet[7]);
        Assert.Equal(0, packet[10]);
    }

    [Fact]
    public void ReportIdIsFirstByte()
    {
        var kb = new ProbeKeyboard(0x02);
        Assert.Equal(0x02, kb.Aura(KeyboardLightingMode.Static, Color.Red, Color.Black, AuraSpeed.Normal, 100)[0]);
    }

    [Fact]
    public void KeyBindingPacketLayout()
    {
        var kb = new ProbeKeyboard();
        Assert.Equal(new byte[] { 0x00, 0x51, 0x20, 0x1F, 0x00, 0x34, 0x12 }, kb.Binding(0x001F, 0x1234));
    }

    [Fact]
    public void KeyBindingPacketNeedsSourceAndAction()
    {
        var kb = new ProbeKeyboard();
        Assert.Null(kb.Binding(0, 0x1234));
        Assert.Null(kb.Binding(0x1F, 0));
    }

    [Fact]
    public void BatteryPercentIsByteSix()
    {
        var reply = new byte[64];
        reply[1] = 0x12;
        reply[2] = 0x01;
        reply[6] = 64;
        Assert.Equal(64, new ProbeKeyboard().BatteryFrom(reply));
    }

    [Fact]
    public void ModelSpecificPacketLayoutIsUsed()
    {
        var k1 = new ProbeTufK1();

        byte[] packet = k1.Aura(KeyboardLightingMode.Static, Color.FromArgb(7, 8, 9), Color.Black, AuraSpeed.Fast, 50);

        Assert.Equal(12, packet.Length);
        Assert.Equal(2, packet[5]);    // TUF K1 speed range is 0..2
        Assert.Equal(50, packet[6]);
        Assert.Equal(new byte[] { 7, 8, 9 }, packet.Skip(9).ToArray());
    }

    [Fact]
    public void ModelsAreConstructibleWithoutHardware()
    {
        var keyboards = new AsusKeyboard[]
        {
            new Azoth(), new AzothExtreme(), new AzothX(), new StrixFlareII(), new StrixScopeII(),
            new StrixScopeRX(), new Falchion(), new TUFK1(), new TUFK3(), new TUFK3GenII(), new ClaymoreII(),
        };

        Assert.All(keyboards, kb =>
        {
            Assert.False(string.IsNullOrWhiteSpace(kb.GetDisplayName()));
            Assert.Equal(0x0B05, kb.VendorID());
            Assert.NotEqual(0, kb.ProductID());
            Assert.Equal(PeripheralType.Keyboard, kb.DeviceType());
            Assert.Equal("icons8_keyboard_48", kb.IconKey());
        });
    }

    [Fact]
    public void KeyboardIdentityIsTypeAndIds()
    {
        Assert.Equal(new Azoth(), new Azoth());
        Assert.NotEqual<AsusKeyboard>(new Azoth(), new AzothX());
    }
}

public class LayoutTests
{
    [Fact]
    public void KeyNamesMapToUniqueWireCodes()
    {
        var codes = AuraKeyboardLayouts.Keys.Values;
        Assert.Equal(codes.Count(), codes.Distinct().Count());
        Assert.Equal(0x1F, AuraKeyboardLayouts.Keys["A"]);
        Assert.Equal(0x6E, AuraKeyboardLayouts.Keys["Esc"]);
    }

    [Fact]
    public void EveryLayoutHasKeysAndLedIds()
    {
        Assert.NotEmpty(AuraKeyboardLayouts.Data);
        foreach (var (name, layout) in AuraKeyboardLayouts.Data)
        {
            Assert.True(layout.Rows.Count > 0, name);
            Assert.True(layout.LedIds.Length > 0, name);
            Assert.True(layout.Rows.Sum(r => r.Length) == layout.LedIds.Length, name);
        }
    }

    [Theory]
    [InlineData("Num 7", "7")]
    [InlineData("Right Shift", "Shift")]
    [InlineData("Esc", "Esc")]
    public void ShortLabelStripsNumpadAndRightPrefix(string name, string expected)
    {
        Assert.Equal(expected, AuraKeyboardLayouts.ShortLabel(name));
    }
}

public class HeadsetProtocolTests
{
    [Fact]
    public void DefaultLightingPacket()
    {
        var hs = new ProbeHeadset();

        byte[] packet = hs.Lighting(HeadsetLightingMode.Breathing, 70, Color.FromArgb(10, 20, 30));

        Assert.Equal(new byte[] { 0xCC, 0x51, 0x28, 0x00, 0x00, 0x02, 70, 10, 20, 30 }, packet);
    }

    [Fact]
    public void CetraLightingPacketAnimatesBreathingAndBlanksColorCycle()
    {
        var hs = new ProbeCetra();

        byte[] breathing = hs.Lighting(HeadsetLightingMode.Breathing, 100, Color.FromArgb(10, 20, 30));
        Assert.Equal(new byte[] { 0xCC, 0x51, 0x28, 0x00, 0x00, 0x02, 0x01, 0x00, 0xFF, 0x00, 10, 20, 30 }, breathing);

        byte[] cycle = hs.Lighting(HeadsetLightingMode.ColorCycle, 100, Color.FromArgb(10, 20, 30));
        Assert.Equal(new byte[] { 0, 0, 0 }, cycle.Skip(10).ToArray());

        byte[] staticMode = hs.Lighting(HeadsetLightingMode.Static, 100, Color.Red);
        Assert.Equal(0x00, staticMode[6]);
    }

    [Fact]
    public void BatteryReplyIsDecoded()
    {
        var hs = new ProbeHeadset();
        var reply = new byte[64];
        reply[5] = 10;   // sleep timer
        reply[6] = 55;   // battery %
        reply[7] = 20;   // low battery warning

        hs.BatteryFrom(reply);

        Assert.Equal(55, hs.Battery);
        Assert.Equal(10, hs.SleepTimer);
        Assert.Equal(20, hs.LowBatteryWarning);
    }

    [Fact]
    public void ChargingReplyIsDecoded()
    {
        var hs = new ProbeHeadset();
        var reply = new byte[64];
        Assert.False(hs.ChargingFrom(reply));
        reply[5] = 1;
        Assert.True(hs.ChargingFrom(reply));
    }

    [Fact]
    public void LightingReplyIsDecoded()
    {
        var hs = new ProbeHeadset();
        var reply = new byte[64];
        reply[5] = 4;     // colour cycle
        reply[6] = 250;   // out of range brightness is clamped
        reply[7] = 1;
        reply[8] = 2;
        reply[9] = 3;

        hs.LightingFrom(reply);

        Assert.Equal(100, hs.Brightness);
        Assert.Equal(Color.FromArgb(1, 2, 3), hs.LightingColor);
    }

    [Fact]
    public void ModelsAreConstructibleWithoutHardware()
    {
        var headsets = new AsusHeadset[] { new DeltaII(), new DeltaIIKjp(), new Pelta(), new CetraSpeedNova(), new Clavis(), new CetraRgb(), new StrixGo24() };

        Assert.All(headsets, hs =>
        {
            Assert.False(string.IsNullOrWhiteSpace(hs.GetDisplayName()));
            Assert.Equal(PeripheralType.Headset, hs.DeviceType());
            Assert.Equal("icons8_headphones_48", hs.IconKey());
        });
    }
}
