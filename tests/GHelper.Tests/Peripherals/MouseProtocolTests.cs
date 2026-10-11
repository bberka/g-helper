using System.Drawing;
using GHelper.Peripherals.Mouse;
using Xunit;

namespace GHelper.Tests.Peripherals;

/// <summary>Minimal concrete mouse that exposes the protected packet encoders / decoders.</summary>
internal class ProbeMouse : AsusMouse
{
    public bool DpiColors;
    public bool XyDpi;

    public ProbeMouse(byte reportId = 0x00) : base(0x0B05, 0x1234, "mi_00", false, reportId)
    {
        DpiSettings = new AsusMouseDPI[DPIProfileCount()];
    }

    public override string GetDisplayName() => "Probe Mouse";
    public override int ProfileCount() => 3;
    public override int DPIProfileCount() => 4;
    public override PollingRate[] SupportedPollingrates() =>
        new[] { PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz };
    public override bool HasDPIColors() => DpiColors;
    public override bool HasXYDPI() => XyDpi;

    public byte[] BatteryPacket() => GetBatteryReportPacket();
    public int BatteryFrom(byte[] p) => ParseBattery(p);
    public bool ChargingState(byte[] p) => ParseChargingState(p);
    public PowerOffSetting PowerOff(byte[] p) => ParsePowerOffSetting(p);
    public int LowBattery(byte[] p) => ParseLowBatteryWarning(p);
    public byte[] EnergyPacket(int low, PowerOffSetting off) => GetUpdateEnergySettingsPacket(low, off);

    public byte[]? DpiPacket(AsusMouseDPI dpi, int profile) => GetUpdateDPIPacket(dpi, profile);
    public byte[] ReadDpiPacket() => GetReadDPIPacket();
    public void ReadDpi(byte[] p) => ParseDPI(p);
    public void ReadDpiColors(byte[] p) => ParseDPIColors(p);
    public byte[] ProfilePacket(int profile) => GetUpdateProfilePacket(profile);
    public int ProfileFrom(byte[] p) => ParseProfile(p);
    public int DpiProfileFrom(byte[] p) => ParseDPIProfile(p);
    public byte[] DpiProfileSwitchPacket(int profile) => GetChangeDPIProfilePacket2(profile);

    public byte[] PollingPacket(PollingRate r) => GetUpdatePollingRatePacket(r);
    public PollingRate PollingFrom(byte[] p) => ParsePollingRate(p);
    public byte[] AnglePacket(short a) => GetUpdateAngleAdjustmentPacket(a);
    public short AngleFrom(byte[] p) => ParseAngleAdjustment(p);
    public bool SnappingFrom(byte[] p) => ParseAngleSnapping(p);
    public byte[] SnappingPacket(bool on) => GetUpdateAngleSnappingPacket(on);

    public byte[] DebouncePacket(DebounceTime d) => GetUpdateDebouncePacket(d);
    public DebounceTime DebounceFrom(byte[] p) => ParseDebounce(p);
    public byte[] LiftOffPacket(LiftOffDistance d) => GetUpdateLiftOffDistancePacket(d);
    public LiftOffDistance LiftOffFrom(byte[] p) => ParseLiftOffDistance(p);

    public byte[] ZonePacket(bool on) => GetUpdateZoneModePacket(on);
    public void ZoneFrom(byte[] p) => ParseZoneMode(p);

    public byte[] LightingReadPacket(LightingZone z) => GetReadLightingModePacket(z);
    public byte[] LightingPacket(LightingSetting s, LightingZone z) => GetUpdateLightingModePacket(s, z);
    public LightingSetting? LightingFrom(byte[] p) => ParseLightingSetting(p);

    public byte[] BindingPacket(ushort src, ushort dst, byte sub = 0x21) => GetSetButtonBindingPacket(src, dst, sub);
}

public class MouseProtocolTests
{
    private static byte[] Packet(int length, params (int index, byte value)[] fill)
    {
        var p = new byte[length];
        foreach (var (i, v) in fill) p[i] = v;
        return p;
    }

    [Fact]
    public void BatteryReportRequestUsesReportId()
    {
        Assert.Equal(new byte[] { 0x00, 0x12, 0x07 }, new ProbeMouse().BatteryPacket());
        Assert.Equal(new byte[] { 0xEC, 0x12, 0x07 }, new ProbeMouse(0xEC).BatteryPacket());
    }

    [Fact]
    public void BatteryReplyDecodesPercentChargingAndEnergySettings()
    {
        var m = new ProbeMouse();
        byte[] reply = Packet(65, (1, 0x12), (2, 0x07), (5, 87), (6, 0x03), (7, 20), (10, 1));

        Assert.Equal(87, m.BatteryFrom(reply));
        Assert.True(m.ChargingState(reply));
        Assert.Equal(PowerOffSetting.Minutes5, m.PowerOff(reply));
        Assert.Equal(20, m.LowBattery(reply));
    }

    [Fact]
    public void BatteryReplyOfOtherCommandIsRejected()
    {
        var m = new ProbeMouse();
        byte[] reply = Packet(65, (1, 0x12), (2, 0x04), (5, 87), (6, 0x03), (7, 20), (10, 1));

        Assert.Equal(-1, m.BatteryFrom(reply));
        Assert.False(m.ChargingState(reply));
        Assert.Equal(PowerOffSetting.Never, m.PowerOff(reply));
        Assert.Equal(0, m.LowBattery(reply));
    }

    [Fact]
    public void EnergySettingsPacketLayout()
    {
        byte[] packet = new ProbeMouse().EnergyPacket(15, PowerOffSetting.Minutes10);
        Assert.Equal(new byte[] { 0x00, 0x51, 0x37, 0x00, 0x00, 0x04, 0x00, 15 }, packet);

        packet = new ProbeMouse().EnergyPacket(0, PowerOffSetting.Never);
        Assert.Equal(0xFF, packet[5]);
    }

    [Theory]
    [InlineData(100u, 1)]
    [InlineData(800u, 15)]
    [InlineData(1600u, 31)]
    [InlineData(2000u, 39)]
    public void DpiIsEncodedAsStepsOfTheIncrement(uint dpi, int encoded)
    {
        var m = new ProbeMouse();
        byte[]? packet = m.DpiPacket(new AsusMouseDPI { DPI = dpi }, 2);

        Assert.NotNull(packet);
        Assert.Equal(new byte[] { 0x00, 0x51, 0x31, 0x01, 0x00, (byte)encoded, 0x00 }, packet);
    }

    [Fact]
    public void DpiOutsideSupportedRangeProducesNoPacket()
    {
        var m = new ProbeMouse();
        Assert.Null(m.DpiPacket(new AsusMouseDPI { DPI = 50 }, 1));
        Assert.Null(m.DpiPacket(new AsusMouseDPI { DPI = 2050 }, 1));
    }

    [Fact]
    public void DpiPacketAppendsColorWhenModelHasDpiColors()
    {
        var m = new ProbeMouse { DpiColors = true };
        byte[]? packet = m.DpiPacket(new AsusMouseDPI { DPI = 400, Color = Color.FromArgb(1, 2, 3) }, 1);

        Assert.Equal(new byte[] { 0x00, 0x51, 0x31, 0x00, 0x00, 7, 0x00, 1, 2, 3 }, packet);
    }

    [Fact]
    public void DpiReplyIsDecodedPerProfile()
    {
        var m = new ProbeMouse();
        // profile n at offset 5 + 2n: little endian step count
        byte[] reply = Packet(65, (1, 0x12), (2, 0x04), (3, 0x00),
            (5, 15), (6, 0),      // 800
            (7, 31), (8, 0),      // 1600
            (9, 39), (10, 0),     // 2000
            (11, 0), (12, 1));    // 256 steps = 12850 dpi

        m.ReadDpi(reply);

        Assert.Equal(800u, m.DpiSettings[0].DPI);
        Assert.Equal(1600u, m.DpiSettings[1].DPI);
        Assert.Equal(2000u, m.DpiSettings[2].DPI);
        Assert.Equal(12850u, m.DpiSettings[3].DPI);
    }

    [Fact]
    public void XyDpiModelsReadFourBytesPerProfile()
    {
        var m = new ProbeMouse { XyDpi = true };
        Assert.Equal(new byte[] { 0x00, 0x12, 0x04, 0x02 }, m.ReadDpiPacket());

        byte[] reply = Packet(65, (1, 0x12), (2, 0x04), (3, 0x02), (5, 15), (9, 31));
        m.ReadDpi(reply);

        Assert.Equal(800u, m.DpiSettings[0].DPI);
        Assert.Equal(1600u, m.DpiSettings[1].DPI);
    }

    [Fact]
    public void DpiReplyForOtherCommandIsIgnored()
    {
        var m = new ProbeMouse();
        m.ReadDpi(Packet(65, (1, 0x12), (2, 0x05), (3, 0x00), (5, 15)));
        Assert.All(m.DpiSettings, d => Assert.Null(d));
    }

    [Fact]
    public void DpiColorsReplyIsDecoded()
    {
        var m = new ProbeMouse { DpiColors = true };
        byte[] reply = Packet(65, (1, 0x12), (2, 0x04), (3, 0x03),
            (5, 255), (6, 0), (7, 0),
            (8, 0), (9, 255), (10, 0),
            (11, 0), (12, 0), (13, 255));

        m.ReadDpiColors(reply);

        Assert.Equal(Color.FromArgb(255, 0, 0), m.DpiSettings[0].Color);
        Assert.Equal(Color.FromArgb(0, 255, 0), m.DpiSettings[1].Color);
        Assert.Equal(Color.FromArgb(0, 0, 255), m.DpiSettings[2].Color);
    }

    [Fact]
    public void ProfileAndDpiProfileComeFromStatusReply()
    {
        var m = new ProbeMouse();
        byte[] reply = Packet(65, (1, 0x12), (2, 0x00), (3, 0x00), (11, 2), (12, 3));

        Assert.Equal(2, m.ProfileFrom(reply));
        Assert.Equal(3, m.DpiProfileFrom(reply));
        Assert.Equal(0, m.ProfileFrom(Packet(65, (1, 0x12), (2, 0x01))));
        Assert.Equal(1, m.DpiProfileFrom(Packet(65, (1, 0x12), (2, 0x01))));
    }

    [Fact]
    public void DpiProfileSwitchPacket()
    {
        Assert.Equal(new byte[] { 0x00, 0x51, 0x31, 0x09, 0x00, 3 }, new ProbeMouse().DpiProfileSwitchPacket(3));
    }

    [Fact]
    public void PollingRatePacket()
    {
        Assert.Equal(new byte[] { 0x00, 0x51, 0x31, 0x04, 0x00, 0x03 }, new ProbeMouse().PollingPacket(PollingRate.PR1000Hz));
    }

    [Theory]
    [InlineData(0, PollingRate.PR125Hz)]
    [InlineData(2, PollingRate.PR500Hz)]
    [InlineData(3, PollingRate.PR1000Hz)]
    public void PollingRateReplyIsDecoded(byte raw, PollingRate expected)
    {
        var m = new ProbeMouse();
        Assert.Equal(expected, m.PollingFrom(Packet(65, (1, 0x12), (2, 0x04), (3, 0x00), (13, raw))));
    }

    [Fact]
    public void UnsupportedPollingRateFallsBackToHighestSupported()
    {
        var m = new ProbeMouse();
        Assert.Equal(PollingRate.PR1000Hz, m.PollingFrom(Packet(65, (1, 0x12), (2, 0x04), (3, 0x00), (13, 6))));
    }

    [Fact]
    public void BoosterUsesHighNibbleOfPollingByte()
    {
        var m = new ProbeMouse { Booster = true };
        // 0x20 -> high nibble 2 = 500 Hz, low bits ignored
        Assert.Equal(PollingRate.PR500Hz, m.PollingFrom(Packet(65, (1, 0x12), (2, 0x04), (3, 0x00), (13, 0x20))));
        // no high nibble: falls back to low 3 bits
        Assert.Equal(PollingRate.PR250Hz, m.PollingFrom(Packet(65, (1, 0x12), (2, 0x04), (3, 0x00), (13, 0x01))));
    }

    [Fact]
    public void AngleSnappingAndAdjustmentPackets()
    {
        var m = new ProbeMouse();
        Assert.Equal(new byte[] { 0x00, 0x51, 0x31, 0x06, 0x00, 0x01 }, m.SnappingPacket(true));
        Assert.Equal(new byte[] { 0x00, 0x51, 0x31, 0x06, 0x00, 0x00 }, m.SnappingPacket(false));
        // -30 degrees = 0xFFE2 little endian
        Assert.Equal(new byte[] { 0x00, 0x51, 0x31, 0x0B, 0x00, 0xE2, 0xFF }, m.AnglePacket(-30));
    }

    [Fact]
    public void AngleAdjustmentReplyIsSignExtended()
    {
        var m = new ProbeMouse();
        byte[] reply = Packet(65, (1, 0x12), (2, 0x04), (3, 0x00), (17, 1), (19, 0xE2), (20, 0xFF));

        Assert.Equal(-30, m.AngleFrom(reply));
        Assert.True(m.SnappingFrom(reply));
    }

    [Fact]
    public void DebouncePacketAndClampedReply()
    {
        var m = new ProbeMouse();
        Assert.Equal(new byte[] { 0x00, 0x51, 0x31, 0x05, 0x00, 0x03 }, m.DebouncePacket(DebounceTime.MS16));

        Assert.Equal(DebounceTime.MS20, m.DebounceFrom(Packet(65, (1, 0x12), (2, 0x04), (3, 0x00), (15, 4))));
        // above the model maximum is clamped
        Assert.Equal(m.MaxDebounce(), m.DebounceFrom(Packet(65, (1, 0x12), (2, 0x04), (3, 0x00), (15, 0x7F))));
        // wrong command: minimum
        Assert.Equal(m.MinDebounce(), m.DebounceFrom(Packet(65, (1, 0x12), (2, 0x09), (3, 0x00), (15, 4))));
    }

    [Fact]
    public void LiftOffPacketAndReply()
    {
        var m = new ProbeMouse();
        Assert.Equal(new byte[] { 0x00, 0x51, 0x35, 0xFF, 0x00, 0xFF, 0x01 }, m.LiftOffPacket(LiftOffDistance.High));
        Assert.Equal(LiftOffDistance.High, m.LiftOffFrom(Packet(65, (1, 0x12), (2, 0x06), (8, 1))));
        Assert.Equal(LiftOffDistance.Low, m.LiftOffFrom(Packet(65, (1, 0x12), (2, 0x05), (8, 1))));
    }

    [Fact]
    public void ZoneModePacketEncodesPollingRateAndDpi()
    {
        var m = new ProbeMouse { ZoneModeDPI = 1600, ZoneModePollingRate = PollingRate.PR4000Hz };

        Assert.Equal(new byte[] { 0x00, 0x51, 0x44, 0x00, 0x00, 0x01, 0x05, 31, 0 }, m.ZonePacket(true));
        Assert.Equal(0x00, m.ZonePacket(false)[5]);
    }

    [Fact]
    public void ZoneModeReplyIsDecoded()
    {
        var m = new ProbeMouse();
        m.ZoneFrom(Packet(65, (1, 0x12), (2, 0x14), (5, 1), (6, (byte)PollingRate.PR1000Hz), (7, 15), (8, 0)));

        Assert.True(m.ZoneMode);
        Assert.Equal(PollingRate.PR1000Hz, m.ZoneModePollingRate);
        Assert.Equal(800, m.ZoneModeDPI);
    }

    [Fact]
    public void LightingPacketCarriesModeBrightnessAndColor()
    {
        var m = new ProbeMouse();
        var setting = new LightingSetting
        {
            LightingMode = LightingMode.Static,
            Brightness = 60,
            RGBColor = Color.FromArgb(10, 20, 30),
        };

        byte[] packet = m.LightingPacket(setting, LightingZone.Logo);

        Assert.Equal(new byte[] { 0x00, 0x51, 0x28, 0x00, 0x00, 0x00, 60, 10, 20, 30, 0x00, 0x00, 0x00 }, packet);
    }

    [Fact]
    public void LightingPacketOnlyCarriesOptionsTheModeSupports()
    {
        var m = new ProbeMouse();
        var rainbow = new LightingSetting
        {
            LightingMode = LightingMode.Rainbow,
            Brightness = 100,
            AnimationDirection = AnimationDirection.CounterClockwise,
            AnimationSpeed = AnimationSpeed.Fast,
            RandomColor = true,
        };

        byte[] packet = m.LightingPacket(rainbow, LightingZone.Logo);

        Assert.Equal(0x03, packet[5]);
        Assert.Equal(0x01, packet[10]);              // direction
        Assert.Equal(0x00, packet[11]);              // random colour is a comet only option
        Assert.Equal((byte)AnimationSpeed.Fast, packet[12]);

        var staticRed = new LightingSetting { LightingMode = LightingMode.Static, Brightness = 100, AnimationDirection = AnimationDirection.CounterClockwise, AnimationSpeed = AnimationSpeed.Fast };
        packet = m.LightingPacket(staticRed, LightingZone.Logo);
        Assert.Equal(0x00, packet[10]);
        Assert.Equal(0x00, packet[12]);
    }

    [Fact]
    public void OutOfRangeBrightnessIsResetToQuarter()
    {
        var m = new ProbeMouse();
        var setting = new LightingSetting { Brightness = 250 };

        byte[] packet = m.LightingPacket(setting, LightingZone.Logo);

        Assert.Equal(25, packet[6]);
        Assert.Equal(25, setting.Brightness);
    }

    [Fact]
    public void LightingReplyIsDecoded()
    {
        var m = new ProbeMouse();
        byte[] reply = Packet(65, (1, 0x12), (2, 0x03), (5, 0x03), (6, 80), (7, 1), (8, 2), (9, 3),
            (11, 1), (12, 1), (13, (byte)AnimationSpeed.Fast));

        LightingSetting? setting = m.LightingFrom(reply);

        Assert.NotNull(setting);
        Assert.Equal(LightingMode.Rainbow, setting!.LightingMode);
        Assert.Equal(80, setting.Brightness);
        Assert.Equal(Color.FromArgb(1, 2, 3), setting.RGBColor);
        Assert.Equal(AnimationDirection.CounterClockwise, setting.AnimationDirection);
        Assert.Equal(AnimationSpeed.Fast, setting.AnimationSpeed);
        Assert.False(setting.RandomColor);
    }

    [Fact]
    public void LightingReplyWithUnknownModeIsOffAndOtherCommandIsNull()
    {
        var m = new ProbeMouse();
        Assert.Equal(LightingMode.Off, m.LightingFrom(Packet(65, (1, 0x12), (2, 0x03), (5, 0x42)))!.LightingMode);
        Assert.Null(m.LightingFrom(Packet(65, (1, 0x12), (2, 0x04))));
    }

    [Fact]
    public void LightingReadPacketAddressesZoneIndex()
    {
        var m = new ProbeMouse();
        Assert.Equal(new byte[] { 0x00, 0x12, 0x03, 0x00 }, m.LightingReadPacket(LightingZone.Logo));
        Assert.Equal(new byte[] { 0x00, 0x12, 0x03, 0x00 }, m.LightingReadPacket(LightingZone.All));
    }

    [Fact]
    public void ButtonBindingPacketIsLittleEndianSourceThenDestination()
    {
        var m = new ProbeMouse();
        Assert.Equal(new byte[] { 0x00, 0x51, 0x21, 0x00, 0x00, 0xE4, 0x01, 0x68, 0x00 }, m.BindingPacket(0x01E4, 0x0068));
        Assert.Equal(0x23, m.BindingPacket(0x01E4, 0x0068, 0x23)[2]);
    }

    [Fact]
    public void ProfilePacketsUseReportId()
    {
        Assert.Equal(new byte[] { 0xEC, 0x50, 0x02, 0x02 }, new ProbeMouse(0xEC).ProfilePacket(2));
    }
}

public class MouseDataTests
{
    [Fact]
    public void LightingSettingRoundTripsThroughExport()
    {
        var original = new LightingSetting
        {
            LightingMode = LightingMode.Comet,
            Brightness = 42,
            RGBColor = Color.FromArgb(0x12, 0x34, 0x56),
            RandomColor = true,
            AnimationSpeed = AnimationSpeed.Slow,
            AnimationDirection = AnimationDirection.CounterClockwise,
        };

        byte[] blob = original.Export();
        Assert.Equal(12, blob.Length);

        var restored = new LightingSetting();
        Assert.True(restored.Import(blob));
        Assert.Equal(original, restored);
    }

    [Fact]
    public void LightingSettingRejectsWrongLength()
    {
        Assert.False(new LightingSetting().Import(new byte[11]));
    }

    [Fact]
    public void DpiSettingRoundTripsThroughExport()
    {
        var original = new AsusMouseDPI { DPI = 1600, Color = Color.FromArgb(9, 8, 7) };

        byte[] blob = original.Export();
        Assert.Equal(8, blob.Length);

        var restored = new AsusMouseDPI();
        Assert.True(restored.Import(blob));
        Assert.Equal(1600u, restored.DPI);
        Assert.Equal(original.Color.ToArgb(), restored.Color.ToArgb());
        Assert.False(restored.Import(new byte[7]));
    }

    [Fact]
    public void MouseProfileExportHasDocumentedLayout()
    {
        var m = new ProbeMouse();
        for (int i = 0; i < m.DPIProfileCount(); i++)
            m.DpiSettings[i] = new AsusMouseDPI { DPI = (uint)(400 * (i + 1)) };
        m.LightingSetting[0] = new LightingSetting();

        byte[] blob = m.Export();

        // magic + lighting zones*12 + (low battery 4 + power off 1 + lift off 1) + dpi*8 + 13
        Assert.Equal(4 + 12 + 6 + 4 * 8 + 13, blob.Length);
        Assert.Equal("GMP1", System.Text.Encoding.ASCII.GetString(blob, 0, 4));
    }

    [Fact]
    public void MouseProfileImportRejectsForeignBlobs()
    {
        var m = new ProbeMouse();
        Assert.False(m.Import(new byte[5]));

        byte[] wrongMagic = new byte[4 + 12 + 6 + 4 * 8 + 13];
        Assert.False(m.Import(wrongMagic));
    }

    [Fact]
    public void BuiltInComboCommandsAreVirtualKeyLists()
    {
        var altTab = AsusMouse.CombosByCode[0x0068];
        Assert.Equal("Alt + Tab", altTab.Label);
        Assert.Equal("0xA4 0x09", altTab.DefaultCommand);
        Assert.Equal("mouse_combo_F13", altTab.ConfigKey);

        var taskManager = AsusMouse.CombosByCode[0x0072];
        Assert.Equal("0xA2 0xA0 0x1B", taskManager.DefaultCommand);
        Assert.Equal("mouse_combo_F23", taskManager.ConfigKey);
    }

    [Fact]
    public void ActionCodeLabelsResolveOrReportUnknown()
    {
        Assert.Equal("Alt + Tab", AsusMouse.LabelForActionCode(0x0068));
        Assert.Equal("Unknown (0xBEEF)", AsusMouse.LabelForActionCode(0xBEEF));
    }

    [Fact]
    public void EveryComboCodeIsUniqueAndBindable()
    {
        Assert.Equal(AsusMouse.MouseCombos.Count, AsusMouse.CombosByCode.Count);
        Assert.All(AsusMouse.MouseCombos, c => Assert.True(AsusMouse.BindingCodes.ContainsKey(c.PassthroughCode)));
    }

    [Fact]
    public void ButtonSlotsHaveUniqueSourceCodes()
    {
        var slots = new ProbeMouse().ButtonSlots;
        Assert.Equal(slots.Count, slots.Values.Select(v => v.SourceCode).Distinct().Count());
    }
}
