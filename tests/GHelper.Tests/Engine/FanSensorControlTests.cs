using GHelper.Fan;
using GHelper.Mode;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace GHelper.Tests.Engine;

public class FanSensorControlTests : EngineTestBase
{
    public FanSensorControlTests(EngineFixture fixture) : base(fixture)
    {
        FanSensorControl.fanRpm = true;
    }

    private static void ResetMax()
    {
        CleanConfig("fan_max_0", "fan_max_1", "fan_max_2");
        FanSensorControl.SetFanMax(AsusFan.CPU, FanSensorControl.DEFAULT_FAN_MAX);
        FanSensorControl.SetFanMax(AsusFan.GPU, FanSensorControl.DEFAULT_FAN_MAX);
        FanSensorControl.SetFanMax(AsusFan.Mid, FanSensorControl.DEFAULT_FAN_MAX);
    }

    [Fact]
    public void FormatsRpmAndRaisesMeasuredMax()
    {
        ResetMax();

        Assert.Equal("4500RPM", FanSensorControl.FormatFan(AsusFan.CPU, 45));
        Assert.Equal(58, FanSensorControl.GetFanMax(AsusFan.CPU));

        // faster than the known max (but plausible): becomes the new max and is persisted
        Assert.Equal("6000RPM", FanSensorControl.FormatFan(AsusFan.CPU, 60));
        Assert.Equal(60, FanSensorControl.GetFanMax(AsusFan.CPU));
        Assert.Equal(60, AppConfig.Get("fan_max_0"));
    }

    [Fact]
    public void FormatsPercentRelativeToMax()
    {
        ResetMax();
        FanSensorControl.fanRpm = false;
        try
        {
            FanSensorControl.SetFanMax(AsusFan.GPU, 60);

            Assert.Equal("50%", FanSensorControl.FormatFan(AsusFan.GPU, 30));
            Assert.Equal("100%", FanSensorControl.FormatFan(AsusFan.GPU, 60));
        }
        finally
        {
            FanSensorControl.fanRpm = true;
            ResetMax();
        }
    }

    [Fact]
    public void MissingFanFormatsToNull()
    {
        Assert.Null(FanSensorControl.FormatFan(AsusFan.CPU, -1));
    }

    [Fact]
    public void ImplausibleReadingDoesNotChangeMax()
    {
        ResetMax();

        FanSensorControl.FormatFan(AsusFan.CPU, 120);   // > INADEQUATE_MAX
        Assert.Equal(58, FanSensorControl.GetFanMax(AsusFan.CPU));
    }

    [Fact]
    public void InadequateStoredMaxFallsBackToDefault()
    {
        FanSensorControl.SetFanMax(AsusFan.Mid, 500);
        Assert.Equal(FanSensorControl.DEFAULT_FAN_MAX, FanSensorControl.GetFanMax(AsusFan.Mid));
        ResetMax();
    }

    [Fact]
    public void XgmUsesFixedMax()
    {
        Assert.Equal(FanSensorControl.XGM_FAN_MAX, FanSensorControl.GetFanMax(AsusFan.XGM));
        Assert.Equal(FanSensorControl.DEFAULT_FAN_MIN, FanSensorControl.GetFanMin(AsusFan.XGM));
    }

    [Fact]
    public void RpmFlagIsPersisted()
    {
        FanSensorControl.fanRpm = false;
        Assert.Equal(0, AppConfig.Get("fan_rpm"));
        FanSensorControl.fanRpm = true;
        Assert.Equal(1, AppConfig.Get("fan_rpm"));
    }

    [Fact]
    public void CalibrationStoresMeasuredMaximums()
    {
        ResetMax();
        Asus.Values[AsusACPI.CPU_Fan] = 55;
        Asus.Values[AsusACPI.GPU_Fan] = 57;
        var control = new FanSensorControl();

        control.StartCalibration();

        // calibration pushes full speed curves for all three fans in turbo
        Assert.Contains(AsusACPI.PerformanceTurbo, Asus.SetValues(AsusACPI.PerformanceMode));
        Assert.Single(Asus.SetBuffers(AsusACPI.DevsCPUFanCurve));
        Assert.Single(Asus.SetBuffers(AsusACPI.DevsGPUFanCurve));
        Assert.Single(Asus.SetBuffers(AsusACPI.DevsMidFanCurve));

        // drive the (private) one second timer by hand instead of waiting 16 seconds
        var timer = TestStatics.Get<System.Timers.Timer>(typeof(FanSensorControl), "timer")!;
        timer.Enabled = false;
        var tick = typeof(FanSensorControl).GetMethod("Timer_Elapsed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var args = (System.Timers.ElapsedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(System.Timers.ElapsedEventArgs));

        for (int i = 0; i < 16; i++) tick.Invoke(control, new object?[] { null, args });

        Assert.Equal(55, AppConfig.Get("fan_max_0"));
        Assert.Equal(57, AppConfig.Get("fan_max_1"));
        Assert.StartsWith("Measured - CPU: 5500", ModeControl.FansResult);
        Assert.Contains("fansaxis", Ui.Refreshes);
        Assert.False(timer.Enabled);

        ResetMax();
    }
}
