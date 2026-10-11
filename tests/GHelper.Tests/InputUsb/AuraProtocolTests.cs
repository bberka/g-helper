using System.Drawing;
using GHelper.USB;
using Xunit;

namespace GHelper.Tests.InputUsb;

// Golden bytes derived from reading app/USB/Aura.cs (AuraMessage, AuraPowerMessage, ApplyDirect*).
public class AuraProtocolTests
{
    [Fact]
    public void StaticRedUsesReportIdAndEffectHeader()
    {
        byte[] msg = AuraProtocol.AuraMessage(AuraMode.AuraStatic, Color.FromArgb(255, 0, 0), Color.Black, 0xEB, isWhite: false);

        Assert.Equal(new byte[]
        {
            0x5D, 0xB3, 0x00, 0x00,       // report id, set effect, zone, mode
            0xFF, 0x00, 0x00,             // colour
            0xEB, 0x00,                   // speed, direction
            0x00,                         // random colour flag
            0x00, 0x00, 0x00,             // colour 2
            0x00, 0x00, 0x00, 0x00        // padding up to 17 bytes
        }, msg);
    }

    [Fact]
    public void BreatheSetsSecondColourAndRandomFlag()
    {
        byte[] msg = AuraProtocol.AuraMessage(AuraMode.AuraBreathe, Color.FromArgb(0, 255, 0), Color.FromArgb(0, 0, 255), 0xF5, false);

        Assert.Equal(new byte[]
        {
            0x5D, 0xB3, 0x00, 0x01, 0x00, 0xFF, 0x00, 0xF5, 0x00, 0x01, 0x00, 0x00, 0xFF, 0, 0, 0, 0
        }, msg);
    }

    [Fact]
    public void BlackColourRequestsRandomColour()
    {
        byte[] msg = AuraProtocol.AuraMessage(AuraMode.AuraColorCycle, Color.Black, Color.Black, 0xE1, false);

        Assert.Equal(0x02, msg[3]);
        Assert.Equal(0xFF, msg[9]);
    }

    [Fact]
    public void WhiteBacklightOnlyKeepsRed()
    {
        byte[] msg = AuraProtocol.AuraMessage(AuraMode.AuraStatic, Color.FromArgb(10, 20, 30), Color.FromArgb(40, 50, 60), 0xEB, isWhite: true);

        Assert.Equal(10, msg[4]);
        Assert.Equal(0, msg[5]);
        Assert.Equal(0, msg[6]);
        Assert.Equal(40, msg[10]);
        Assert.Equal(0, msg[11]);
        Assert.Equal(0, msg[12]);
    }

    [Theory]
    [InlineData(AuraSpeed.Slow, 0xE1)]
    [InlineData(AuraSpeed.Normal, 0xEB)]
    [InlineData(AuraSpeed.Fast, 0xF5)]
    public void SpeedBytes(AuraSpeed speed, int expected)
    {
        Assert.Equal(expected, AuraProtocol.SpeedByte(speed));
    }

    [Fact]
    public void ApplyAndSetMessages()
    {
        Assert.Equal(new byte[] { 0x5D, 0xB4 }, AuraProtocol.MessageApply);
        Assert.Equal(new byte[] { 0x5D, 0xB5, 0, 0, 0 }, AuraProtocol.MessageSet);
    }

    [Fact]
    public void PowerMessageAllOff()
    {
        Assert.Equal(new byte[] { 0x5D, 0xBD, 0x01, 0x00, 0x00, 0x00, 0x00, 0xFF }, AuraProtocol.PowerMessage(new AuraPower()));
    }

    [Fact]
    public void PowerMessageAllOn()
    {
        var all = new AuraPower
        {
            BootLogo = true, BootKeyb = true, AwakeLogo = true, AwakeKeyb = true,
            SleepLogo = true, SleepKeyb = true, ShutdownLogo = true, ShutdownKeyb = true,
            BootBar = true, AwakeBar = true, SleepBar = true, ShutdownBar = true,
            BootLid = true, AwakeLid = true, SleepLid = true, ShutdownLid = true,
            BootRear = true, AwakeRear = true, SleepRear = true, ShutdownRear = true,
        };

        // keyboard 0xFF, bar uses bits 0-4, lid and rear mirror their nibble into the upper half
        Assert.Equal(new byte[] { 0x5D, 0xBD, 0x01, 0xFF, 0x1F, 0xFF, 0xFF, 0xFF }, AuraProtocol.PowerMessage(all));
    }

    [Fact]
    public void PowerMessageAwakeKeyboardOnly()
    {
        var flags = new AuraPower { AwakeKeyb = true };
        Assert.Equal(new byte[] { 0x5D, 0xBD, 0x01, 0x08, 0x00, 0x00, 0x00, 0xFF }, AuraProtocol.PowerMessage(flags));
    }

    [Fact]
    public void PowerMessageBarAwakeSetsTwoBits()
    {
        // legacy quirk: AwakeBar is written to bit 0 and bit 2
        var flags = new AuraPower { AwakeBar = true };
        Assert.Equal(0b00000101, AuraProtocol.PowerMessage(flags)[4]);
    }

    [Fact]
    public void AllyPowerUsesInputReport()
    {
        var flags = new AuraPower { BootKeyb = true, SleepKeyb = true };
        Assert.Equal(new byte[] { 0x5A, 0xD1, 0x09, 0x01, 0x05 }, AuraProtocol.AllyPowerMessage(flags));
    }

    static Color[] Zones() => Enumerable.Range(0, 8).Select(z => Color.FromArgb(10 * (z + 1), 100 + z, 200 + z)).ToArray();

    static Color Led(List<byte[]> packets, int led)
    {
        byte[] packet;
        int offset;

        if (led < AuraProtocol.KeySet)
        {
            packet = packets[led / AuraProtocol.LedsPerPacket];
            offset = 9 + 3 * (led % AuraProtocol.LedsPerPacket);
        }
        else
        {
            packet = packets[^1];
            offset = 9 + 3 * (led - AuraProtocol.KeySet);
        }

        return Color.FromArgb(packet[offset], packet[offset + 1], packet[offset + 2]);
    }

    [Fact]
    public void PerKeyDirectUsesElevenKeyPacketsAndATail()
    {
        var zones = Zones();
        var packets = AuraProtocol.DirectPackets(zones, strix4Zone: false, numpad: false, flipped: false);

        Assert.Equal(12, packets.Count);
        Assert.All(packets, p => Assert.Equal(64, p.Length));

        // key block: 16 LEDs per packet, start index in byte 6, count in byte 7
        Assert.Equal(new byte[] { 0x5D, 0xBC, 0x00, 0x01, 0x01, 0x01, 0x00, 0x10 }, packets[0][..8]);
        Assert.Equal(new byte[] { 0x5D, 0xBC, 0x00, 0x01, 0x01, 0x01, 0x10, 0x10 }, packets[1][..8]);
        Assert.Equal(new byte[] { 0x5D, 0xBC, 0x00, 0x01, 0x01, 0x01, 0xA0, 0x07 }, packets[10][..8]);
        // tail (logo, lid, lightbar)
        Assert.Equal(new byte[] { 0x5D, 0xBC, 0x00, 0x01, 0x04, 0x00, 0x00, 0x00 }, packets[11][..8]);
    }

    [Fact]
    public void RearStripAddsThreePacketsAfterTheTail()
    {
        var zones = Zones();
        var without = AuraProtocol.DirectPackets(zones, false, false, false);
        var packets = AuraProtocol.DirectPackets(zones, false, false, false, rearStrip: true);

        Assert.Equal(without.Count + 3, packets.Count);
        for (int i = 0; i < without.Count; i++) Assert.Equal(without[i], packets[i]);

        // 39 LEDs in 16 / 16 / 7 chunks, 1 based start index in byte 6, count in byte 7, report type 0x05
        Assert.Equal(new byte[] { 0x5D, 0xBC, 0x00, 0x01, 0x05, 0x00, 0x01, 0x10 }, packets[12][..8]);
        Assert.Equal(new byte[] { 0x5D, 0xBC, 0x00, 0x01, 0x05, 0x00, 0x11, 0x10 }, packets[13][..8]);
        Assert.Equal(new byte[] { 0x5D, 0xBC, 0x00, 0x01, 0x05, 0x00, 0x21, 0x07 }, packets[14][..8]);

        // first LED is zone 0, last LED is zone 3 (interpolated along the strip)
        Assert.Equal(zones[0], Color.FromArgb(packets[12][9], packets[12][10], packets[12][11]));
        int last = 9 + 6 * 3;
        Assert.Equal(zones[3], Color.FromArgb(packets[14][last], packets[14][last + 1], packets[14][last + 2]));

        // bytes after the last LED are cleared
        Assert.All(packets[14][(9 + 7 * 3)..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void RearStripIsNotSentForFourZoneBoards()
    {
        var packets = AuraProtocol.DirectPackets(Zones(), strix4Zone: true, numpad: false, flipped: false, rearStrip: true);
        Assert.Single(packets);
    }

    [Theory]
    [InlineData(21, 0)]   // ESC
    [InlineData(28, 1)]   // F5
    [InlineData(33, 2)]   // F9
    [InlineData(36, 3)]   // F12
    [InlineData(131, 1)]  // space bar
    [InlineData(135, 2)]  // right alt
    [InlineData(167, 0)]  // logo
    [InlineData(176, 0)]  // lid left
    [InlineData(177, 3)]  // lid right
    [InlineData(174, 5)]  // lightbar 1
    [InlineData(172, 4)]  // lightbar 3
    [InlineData(171, 6)]  // lightbar 4
    [InlineData(169, 7)]  // lightbar 6
    public void PerKeyLedGetsItsZoneColour(int led, int zone)
    {
        var zones = Zones();
        var packets = AuraProtocol.DirectPackets(zones, false, false, false);
        Assert.Equal(zones[zone], Led(packets, led));
    }

    [Fact]
    public void NumpadLayoutMovesZoneBoundaries()
    {
        var zones = Zones();
        var normal = AuraProtocol.DirectPackets(zones, false, false, false);
        var numpad = AuraProtocol.DirectPackets(zones, false, true, false);

        // F4 (LED 26) is zone 1 on the normal layout and zone 1 as well, but F3 (LED 25) moves from 1 to 0
        Assert.Equal(zones[1], Led(normal, 25));
        Assert.Equal(zones[0], Led(numpad, 25));
    }

    [Fact]
    public void FourZoneDirectIsOnePacket()
    {
        var zones = Zones();
        var packets = AuraProtocol.DirectPackets(zones, strix4Zone: true, numpad: false, flipped: false);

        Assert.Single(packets);
        var p = packets[0];
        Assert.Equal(new byte[] { 0x5D, 0xBC, 0x00, 0x01, 0x04, 0x00, 0x00, 0x00 }, p[..8]);

        // key zones 0..3, two unused, lightbar right to left 7 7 6 5 4 4
        int[] expected = { 0, 1, 2, 3, 0, 0, 7, 7, 6, 5, 4, 4 };
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(zones[expected[i]].R, p[9 + 3 * i]);
            Assert.Equal(zones[expected[i]].G, p[10 + 3 * i]);
            Assert.Equal(zones[expected[i]].B, p[11 + 3 * i]);
        }
        Assert.Equal(0, p[9 + 3 * expected.Length]);
    }

    [Fact]
    public void FourZoneFlippedReversesLightbar()
    {
        var zones = Zones();
        var p = AuraProtocol.DirectPackets(zones, true, false, flipped: true)[0];

        int[] expected = { 0, 1, 2, 3, 0, 0, 4, 4, 5, 6, 7, 7 };
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(zones[expected[i]].R, p[9 + 3 * i]);
    }

    [Fact]
    public void LightbarPacketOnlyCarriesTheBar()
    {
        var zones = Zones();
        var p = AuraProtocol.LightbarPacket(zones, flipped: false);

        Assert.Equal(64, p.Length);
        Assert.Equal(new byte[] { 0x5D, 0xBC, 0x00, 0x01, 0x04 }, p[..5]);
        Assert.Equal(zones[7].R, p[9 + 6 * 3]);
        Assert.Equal(zones[4].R, p[9 + 10 * 3]);
    }

    [Fact]
    public void SingleZoneDirectPacket()
    {
        var p = AuraProtocol.DirectSingleColorPacket(Color.FromArgb(1, 2, 3));

        Assert.Equal(new byte[] { 0x5D, 0xBC, 0x01, 0x01, 0, 0, 0, 0, 0, 1, 2, 3 }, p);
    }
}
