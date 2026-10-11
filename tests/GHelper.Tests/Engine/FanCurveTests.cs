using Xunit;

namespace GHelper.Tests.Engine;

public class FanCurveTests : EngineTestBase
{
    public FanCurveTests(EngineFixture fixture) : base(fixture) { }

    private static byte[] Bytes(params int[] values) => values.Select(v => (byte)v).ToArray();

    [Fact]
    public void FixFanCurveInsertsFirstPointAndDropsFourthWhenCurveStartsHot()
    {
        CleanConfig("fan_clamp");

        // default balanced CPU profile: first point is 58C, which ASUS firmware does not like
        var curve = AsusACPI.FixFanCurve(Bytes(0x3A, 0x3D, 0x40, 0x44, 0x48, 0x4D, 0x51, 0x62, 0x08, 0x11, 0x16, 0x1A, 0x22, 0x29, 0x30, 0x45));

        Assert.Equal(Bytes(30, 50, 60, 64, 72, 80, 90, 100), curve[..8]);
        Assert.Equal(Bytes(0, 8, 17, 22, 34, 41, 48, 69), curve[8..]);
    }

    [Fact]
    public void FixFanCurveWithoutClampKeepsTemperatures()
    {
        AppConfig.Set("fan_clamp", 0);
        try
        {
            var curve = AsusACPI.FixFanCurve(Bytes(0x3A, 0x3D, 0x40, 0x44, 0x48, 0x4D, 0x51, 0x62, 0x08, 0x11, 0x16, 0x1A, 0x22, 0x29, 0x30, 0x45));

            Assert.Equal(Bytes(30, 58, 61, 64, 72, 77, 81, 98), curve[..8]);
            Assert.Equal(Bytes(0, 8, 17, 22, 34, 41, 48, 69), curve[8..]);
        }
        finally
        {
            CleanConfig("fan_clamp");
        }
    }

    [Fact]
    public void FixFanCurveSeparatesDuplicatedPoints()
    {
        CleanConfig("fan_clamp");

        var curve = AsusACPI.FixFanCurve(Bytes(30, 30, 30, 30, 30, 30, 30, 30, 0, 5, 10, 15, 20, 25, 30, 35));

        // 30,36,42,... then clamped into their 10 degree slots
        Assert.Equal(Bytes(30, 40, 50, 60, 70, 80, 90, 100), curve[..8]);
    }

    [Fact]
    public void FixFanCurveKeepsAlreadyValidCurve()
    {
        CleanConfig("fan_clamp");

        var input = Bytes(30, 40, 50, 60, 70, 80, 90, 100, 0, 10, 20, 30, 40, 50, 60, 70);
        var curve = AsusACPI.FixFanCurve((byte[])input.Clone());

        Assert.Equal(input, curve);
    }

    [Fact]
    public void FixFanCurveRejectsWrongLength()
    {
        Assert.Throws<Exception>(() => AsusACPI.FixFanCurve(new byte[8]));
    }

    [Fact]
    public void InvalidCurveDetection()
    {
        Assert.True(AsusACPI.IsInvalidCurve(new byte[15]));
        Assert.True(AsusACPI.IsInvalidCurve(new byte[16]));
        Assert.True(AsusACPI.IsEmptyCurve(new byte[16]));
        Assert.False(AsusACPI.IsInvalidCurve(Bytes(30, 40, 50, 60, 70, 80, 90, 100, 0, 10, 20, 30, 40, 50, 60, 70)));
    }

    [Fact]
    public void SetFanCurveAppliesScaleToPwmHalfOnly()
    {
        AppConfig.Set("fan_scale", 50);
        try
        {
            var curve = Bytes(30, 40, 50, 60, 70, 80, 90, 100, 10, 20, 40, 60, 80, 100, 100, 100);

            int result = Program.acpi.SetFanCurve(AsusFan.CPU, curve);

            Assert.Equal(1, result);
            var sent = Assert.Single(Asus.SetBuffers(AsusACPI.DevsCPUFanCurve));
            Assert.Equal(Bytes(30, 40, 50, 60, 70, 80, 90, 100), sent[..8]);
            Assert.Equal(Bytes(5, 10, 20, 30, 40, 50, 50, 50), sent[8..]);
        }
        finally
        {
            CleanConfig("fan_scale");
        }
    }

    [Fact]
    public void SetFanCurveRoutesToTheRightEndpoint()
    {
        var curve = Bytes(30, 40, 50, 60, 70, 80, 90, 100, 10, 20, 40, 60, 80, 100, 100, 100);

        Program.acpi.SetFanCurve(AsusFan.GPU, (byte[])curve.Clone());
        Program.acpi.SetFanCurve(AsusFan.Mid, (byte[])curve.Clone());

        Assert.Single(Asus.SetBuffers(AsusACPI.DevsGPUFanCurve));
        Assert.Single(Asus.SetBuffers(AsusACPI.DevsMidFanCurve));
        Assert.Empty(Asus.SetBuffers(AsusACPI.DevsCPUFanCurve));
    }

    [Fact]
    public void SetFanCurveRejectsBadInput()
    {
        Assert.Equal(-1, Program.acpi.SetFanCurve(AsusFan.CPU, new byte[8]));
        Assert.Equal(-1, Program.acpi.SetFanCurve(AsusFan.CPU, new byte[16]));
        Assert.Empty(Asus.Sets);
    }

    [Fact]
    public void GetFanTreatsOutOfRangeAsMissing()
    {
        Asus.Values[AsusACPI.CPU_Fan] = 45;
        Asus.Values[AsusACPI.GPU_Fan] = 130;

        Assert.Equal(45, Program.acpi.GetFan(AsusFan.CPU));
        Assert.Equal(-1, Program.acpi.GetFan(AsusFan.GPU));
        Assert.Equal(-1, Program.acpi.GetFan(AsusFan.Mid));
    }

    [Fact]
    public void SetFanRangeUsesCurveEndpoints()
    {
        var curve = Bytes(30, 40, 50, 60, 70, 80, 90, 100, 10, 20, 40, 60, 80, 100, 100, 100);

        Assert.Equal(1, Program.acpi.SetFanRange(AsusFan.CPU, curve));

        var sent = Assert.Single(Asus.SetBuffers(AsusACPI.DevsCPUFan));
        // min = curve[8] (10%), max = curve[15] (100%) scaled to 0..255
        Assert.Equal(new byte[] { 25, 255 }, sent);
    }
}
