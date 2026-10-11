using System.Text;
using GHelper.Platform.Windows.Asus;
using Xunit;

namespace GHelper.Tests.Windows;

public class AsusAcpiProtocolTests
{
    [Fact]
    public void MethodIdsSpellTheAcpiNames()
    {
        Assert.Equal("DSTS", Encoding.ASCII.GetString(BitConverter.GetBytes(AtkAcpiProtocol.DSTS)));
        Assert.Equal("DEVS", Encoding.ASCII.GetString(BitConverter.GetBytes(AtkAcpiProtocol.DEVS)));
        Assert.Equal("INIT", Encoding.ASCII.GetString(BitConverter.GetBytes(AtkAcpiProtocol.INIT)));
        Assert.Equal("WDOG", Encoding.ASCII.GetString(BitConverter.GetBytes(AtkAcpiProtocol.WDOG)));
    }

    [Fact]
    public void DstsBufferLayout()
    {
        byte[] buffer = AtkAcpiProtocol.BuildDsts(0x00120075);

        Assert.Equal(16, buffer.Length);
        Assert.Equal(AtkAcpiProtocol.DSTS, BitConverter.ToUInt32(buffer, 0));
        Assert.Equal(8u, BitConverter.ToUInt32(buffer, 4)); // argument length
        Assert.Equal(0x00120075u, BitConverter.ToUInt32(buffer, 8));
        Assert.Equal(0u, BitConverter.ToUInt32(buffer, 12));
    }

    [Fact]
    public void DstsCarriesStatusArgument()
    {
        byte[] buffer = AtkAcpiProtocol.BuildDsts(0x00110013, 7);
        Assert.Equal(7u, BitConverter.ToUInt32(buffer, 12));
    }

    [Fact]
    public void DevsWithStatusBufferLayout()
    {
        byte[] buffer = AtkAcpiProtocol.BuildDevs(0x00120057, 80);

        Assert.Equal(16, buffer.Length);
        Assert.Equal(AtkAcpiProtocol.DEVS, BitConverter.ToUInt32(buffer, 0));
        Assert.Equal(8u, BitConverter.ToUInt32(buffer, 4));
        Assert.Equal(0x00120057u, BitConverter.ToUInt32(buffer, 8));
        Assert.Equal(80u, BitConverter.ToUInt32(buffer, 12));
    }

    [Fact]
    public void DevsWithParameterBlock()
    {
        byte[] curve = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
        byte[] buffer = AtkAcpiProtocol.BuildDevs(0x00110024, curve);

        Assert.Equal(8 + 4 + 16, buffer.Length);
        Assert.Equal(AtkAcpiProtocol.DEVS, BitConverter.ToUInt32(buffer, 0));
        Assert.Equal(20u, BitConverter.ToUInt32(buffer, 4)); // 4 byte id + 16 byte curve
        Assert.Equal(0x00110024u, BitConverter.ToUInt32(buffer, 8));
        Assert.Equal(curve, buffer.AsSpan(12).ToArray());
    }

    [Fact]
    public void InitAndWatchdogAreEmptyCalls()
    {
        byte[] init = AtkAcpiProtocol.BuildInit();
        Assert.Equal(AtkAcpiProtocol.INIT, BitConverter.ToUInt32(init, 0));
        Assert.Equal(8u, BitConverter.ToUInt32(init, 4));
        Assert.All(init.Skip(8), b => Assert.Equal(0, b));

        byte[] dog = AtkAcpiProtocol.BuildWatchDog();
        Assert.Equal(AtkAcpiProtocol.WDOG, BitConverter.ToUInt32(dog, 0));
    }

    [Fact]
    public void LargeDstsPadsTheInput()
    {
        byte[] buffer = AtkAcpiProtocol.BuildLargeDsts(0x000600C4, 8);

        Assert.Equal(8 + 4 + 8, buffer.Length);
        Assert.Equal(AtkAcpiProtocol.DSTS, BitConverter.ToUInt32(buffer, 0));
        Assert.Equal(12u, BitConverter.ToUInt32(buffer, 4));
        Assert.Equal(0x000600C4u, BitConverter.ToUInt32(buffer, 8));

        byte[] wide = AtkAcpiProtocol.BuildLargeDsts(1, 24);
        Assert.Equal(8 + 4 + 24, wide.Length);
        Assert.Equal(28u, BitConverter.ToUInt32(wide, 4));
    }

    [Fact]
    public void DeviceGetSubtractsTheStatusBit()
    {
        byte[] output = new byte[16];
        BitConverter.GetBytes(0x10000 + 42).CopyTo(output, 0);
        Assert.Equal(42, AtkAcpiProtocol.DeviceGetValue(output));

        // an all zero answer (no driver / unsupported id) is negative: "unsupported"
        Assert.True(AtkAcpiProtocol.DeviceGetValue(new byte[16]) < 0);
    }

    [Fact]
    public void DeviceSetResultIsTheFirstDword()
    {
        byte[] output = new byte[16];
        output[0] = 1;
        Assert.Equal(1, AtkAcpiProtocol.DeviceSetResult(output));
    }

    [Fact]
    public void EventRegistrationIsLittleEndianHandle()
    {
        byte[] data = AtkAcpiProtocol.BuildEventRegistration(0x1234);
        Assert.Equal(8, data.Length);
        Assert.Equal(0x34, data[0]);
        Assert.Equal(0x12, data[1]);
        Assert.All(data.Skip(2), b => Assert.Equal(0, b));
    }

    [Fact]
    public void BackendIsInertUntilUsed()
    {
        // construction + Name + Dispose must not open the device
        using var backend = new WindowsAsusBackend();
        Assert.Equal("ATKACPI", backend.Name);
    }
}
