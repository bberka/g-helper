using GHelper.Mode;
using Xunit;

namespace GHelper.Tests.Engine;

public class ModeControlTests : EngineTestBase
{
    // keys are per mode: *_0 = balanced, the mode all tests below run in
    private static readonly string[] Keys =
    {
        "performance_mode", "performance_0", "performance_1",
        "limit_total_0", "limit_slow_0", "limit_fast_0", "limit_cpu_0", "limit_gpucpu_0", "limit_crossload_0", "limit_cputemp_0",
        "gpu_power_0", "gpu_boost_0", "gpu_temp_0", "auto_apply_0", "auto_apply_power_0", "auto_uv_0", "cpu_temp_0",
        "fan_profile_cpu_0", "fan_profile_gpu_0", "fan_profile_mid_0", "hysteresis_up_0", "hysteresis_down_0",
        "powermode_0", "mode_command_0", "auto_boost_0", "skip_powermode", "mode_delay",
    };

    public ModeControlTests(EngineFixture fixture) : base(fixture)
    {
        CleanConfig(Keys);
        AppConfig.Set("performance_mode", 0);
    }

    [Fact]
    public void SetPowerWritesSustainedAndSlowLimits()
    {
        Asus.Values[AsusACPI.PPT_APUA0] = 0;   // supported
        AppConfig.Set("limit_total_0", 80);
        AppConfig.Set("limit_cpu_0", 60);
        AppConfig.Set("limit_fast_0", 90);

        new ModeControl().SetPower();

        Assert.Equal(new[] { 80 }, Asus.SetValues((uint)AsusACPI.PPT_APUA3));
        Assert.Equal(new[] { 80 }, Asus.SetValues((uint)AsusACPI.PPT_APUA0));   // no slow limit configured -> follows total
        Assert.EndsWith($": {Modes.GetCurrentName()} 80W", ModeControl.ModeLabel);
    }

    [Fact]
    public void SetPowerUsesSeparateSlowLimit()
    {
        Asus.Values[AsusACPI.PPT_APUA0] = 0;
        AppConfig.Set("limit_total_0", 70);
        AppConfig.Set("limit_slow_0", 85);
        AppConfig.Set("limit_cpu_0", 60);
        AppConfig.Set("limit_fast_0", 90);

        new ModeControl().SetPower();

        Assert.Equal(new[] { 70 }, Asus.SetValues((uint)AsusACPI.PPT_APUA3));
        Assert.Equal(new[] { 85 }, Asus.SetValues((uint)AsusACPI.PPT_APUA0));
    }

    [Fact]
    public void SetPowerSkipsOutOfRangeLimits()
    {
        Asus.Values[AsusACPI.PPT_APUA0] = 0;
        AppConfig.Set("limit_total_0", AsusACPI.MaxTotal + 50);
        AppConfig.Set("limit_cpu_0", 60);
        AppConfig.Set("limit_fast_0", 90);

        new ModeControl().SetPower();

        Assert.Empty(Asus.Sets);
    }

    [Fact]
    public void SetPowerSkipsWhenNothingConfigured()
    {
        Asus.Values[AsusACPI.PPT_APUA0] = 0;
        new ModeControl().SetPower();
        Assert.Empty(Asus.Sets);
    }

    [Fact]
    public void SetGpuPowerWritesSupportedLimitsOnly()
    {
        Asus.Values[AsusACPI.GPU_POWER] = 0;
        Asus.Values[(uint)AsusACPI.PPT_GPUC0] = 0;
        // PPT_GPUC2 (temp target) unsupported
        AppConfig.Set("gpu_power_0", 25);
        AppConfig.Set("gpu_boost_0", 15);
        AppConfig.Set("gpu_temp_0", 80);

        new ModeControl().SetGPUPower();

        Assert.Equal(new[] { 25 }, Asus.SetValues(AsusACPI.GPU_POWER));
        Assert.Equal(new[] { 15 }, Asus.SetValues((uint)AsusACPI.PPT_GPUC0));
        Assert.Empty(Asus.SetValues((uint)AsusACPI.PPT_GPUC2));
    }

    [Fact]
    public void SetCrossPowerWritesAllThreeLimits()
    {
        Asus.Values[(uint)AsusACPI.PPT_GPUCPU9C] = 0;
        Asus.Values[(uint)AsusACPI.PPT_CROSS9F] = 0;
        Asus.Values[(uint)AsusACPI.PPT_TEMP9E] = 0;
        AppConfig.Set("limit_gpucpu_0", 10);
        AppConfig.Set("limit_crossload_0", 20);
        AppConfig.Set("limit_cputemp_0", 90);

        new ModeControl().SetCrossPower();

        Assert.Equal(new[] { 10 }, Asus.SetValues((uint)AsusACPI.PPT_GPUCPU9C));
        Assert.Equal(new[] { 20 }, Asus.SetValues((uint)AsusACPI.PPT_CROSS9F));
        Assert.Equal(new[] { 90 }, Asus.SetValues((uint)AsusACPI.PPT_TEMP9E));
    }

    [Fact]
    public void AutoFansAppliesStoredCurvesAndMarksLabel()
    {
        AppConfig.Set("auto_apply_0", 1);
        AppConfig.Set("fan_profile_cpu_0", "1E-28-32-3C-46-50-5A-64-0A-14-28-3C-50-64-64-64");
        AppConfig.Set("fan_profile_gpu_0", "1E-28-32-3C-46-50-5A-64-05-0A-14-1E-28-32-3C-46");
        AppConfig.Set("hysteresis_up_0", 2);
        AppConfig.Set("hysteresis_down_0", 3);
        Asus.Values[AsusACPI.FanHysteresis] = 0;

        new ModeControl().AutoFans();

        var cpu = Assert.Single(Asus.SetBuffers(AsusACPI.DevsCPUFanCurve));
        Assert.Equal(0x1E, cpu[0]);
        Assert.Equal(0x64, cpu[15]);
        Assert.Single(Asus.SetBuffers(AsusACPI.DevsGPUFanCurve));
        Assert.Single(Asus.SetBuffers(AsusACPI.FanHysteresis));
        Assert.Equal("", ModeControl.FansResult);
        Assert.Contains("+", ModeControl.ModeLabel);
        Assert.Contains("modelabel", Ui.Refreshes);
    }

    [Fact]
    public void AutoFansDoesNothingWithoutApplyFlag()
    {
        AppConfig.Set("fan_profile_cpu_0", "1E-28-32-3C-46-50-5A-64-0A-14-28-3C-50-64-64-64");

        new ModeControl().AutoFans();

        Assert.Empty(Asus.Sets);
        Assert.DoesNotContain("+", ModeControl.ModeLabel);
    }

    [Fact]
    public void SetPerformanceModeAppliesPlatformMode()
    {
        Asus.Values[AsusACPI.PerformanceMode] = 0;
        var control = new ModeControl();

        control.SetPerformanceMode(1, notify: true);
        control.WaitForApply();

        Assert.Equal(1, Modes.GetCurrent());
        Assert.Contains(1, Asus.SetValues(AsusACPI.PerformanceMode));
        Assert.Contains("SetPowerModeInt:1", P.PowerService.Calls);
        Assert.Contains("mode", Ui.Refreshes);
        Assert.Contains("fans", Ui.Refreshes);
        Assert.Contains(Ui.Toasts, t => t.icon == "Battery" && t.text == Modes.GetName(1));
    }

    [Fact]
    public void SetPerformanceModeFallsBackToBalancedForUnknownMode()
    {
        var control = new ModeControl();

        control.SetPerformanceMode(9);
        control.WaitForApply();

        Assert.Equal(0, Modes.GetCurrent());
    }

    [Fact]
    public void SetPerformanceModeHonoursPowerModeOverrideAndSkipFlag()
    {
        var control = new ModeControl();
        AppConfig.Set("powermode_0", "custom-scheme");

        control.SetPerformanceMode(0);
        control.WaitForApply();
        Assert.Contains("SetPowerMode:custom-scheme", P.PowerService.Calls);

        P.PowerService.Calls.Clear();
        AppConfig.Set("skip_powermode", 1);
        control.SetPerformanceMode(0);
        control.WaitForApply();
        Assert.DoesNotContain(P.PowerService.Calls, c => c.StartsWith("SetPowerMode"));
    }

    [Fact]
    public void CycleWritesNextModeAndToastsWithChargerIcon()
    {
        AppConfig.Set("mode_delay", 600000);   // the debounce timer must not fire inside the test
        P.PowerService.Source = Platform.PowerSource.AC;
        Modes.SetCurrent(0);

        new ModeControl().CyclePerformanceMode();

        Assert.Equal(1, Modes.GetCurrent());
        Assert.Contains(Ui.Toasts, t => t.icon == "Charger");
    }

    [Fact]
    public void ShutdownAndSleepResetsAreOptIn()
    {
        var control = new ModeControl();

        control.SleepReset();
        control.ShutdownReset();

        // model specific defaults (FX507Z / GU605..) are not active for the fake model
        Assert.Empty(Asus.SetValues(AsusACPI.PerformanceMode));

        AppConfig.Set("shutdown_reset", 1);
        AppConfig.Set("sleep_reset", 1);
        try
        {
            control.ShutdownReset();
            control.SleepReset();
            Assert.Equal(new[] { AsusACPI.PerformanceBalanced, 0 }, Asus.SetValues(AsusACPI.PerformanceMode));
        }
        finally
        {
            CleanConfig("shutdown_reset", "sleep_reset");
        }
    }

    [Fact]
    public void RyzenRequiresElevationAndRelaunchesOnRequest()
    {
        var control = new ModeControl();

        Assert.Equal("", control.SetRyzen(launchAsAdmin: true));

        Assert.Contains("RunAsAdmin:uv:False", P.ProcessService.Calls);
        Assert.Empty(P.Tuning.Calls);
    }

    [Fact]
    public void RyzenAppliesTemperatureLimitWhenElevated()
    {
        P.SystemInfo.IsElevated = true;
        AppConfig.Set("cpu_temp_0", 85);
        var control = new ModeControl();

        string result = control.SetRyzen();

        Assert.Contains("Thm:85", P.Tuning.Calls);
        Assert.Contains("CPU Temp 85", result);
        Assert.True(ModeControl.IsPawnAvailable());
    }

    [Fact]
    public void RyzenReportsStatusFailure()
    {
        P.SystemInfo.IsElevated = true;
        P.Tuning.Result = 0xFF;
        AppConfig.Set("cpu_temp_0", 85);

        string result = new ModeControl().SetRyzen();

        Assert.Contains("Failed", result);
    }

    [Fact]
    public void RyzenDoesNothingWhenDriverUnavailable()
    {
        P.SystemInfo.IsElevated = true;
        P.Tuning.InitOk = false;
        AppConfig.Set("cpu_temp_0", 85);

        Assert.Equal("", new ModeControl().SetRyzen());
        Assert.False(ModeControl.IsPawnAvailable());
        Assert.Empty(P.Tuning.Calls);
    }

    [Fact]
    public void PawnInstalledFollowsPlatformSupport()
    {
        P.Tuning.Supported = false;
        Assert.False(ModeControl.IsPawnInstalled());
        P.Tuning.Supported = true;
        Assert.True(ModeControl.IsPawnInstalled());
    }

    [Fact]
    public void ReadRyzenLimitsFormatsPlatformReading()
    {
        string text = new ModeControl().ReadRyzenLimits();

        Assert.Contains("SPL: 25", text);
        Assert.Contains("sPPT 30", text);
        Assert.Contains("fPPT 35", text);
    }

    [Fact]
    public void ResetPerformanceModeRestoresDefaultPowerMode()
    {
        AppConfig.Set("powermode_0", "custom-scheme");

        new ModeControl().ResetPerformanceMode();

        Assert.Equal(new[] { 0 }, Asus.SetValues(AsusACPI.PerformanceMode));
        Assert.False(AppConfig.Exists("powermode_0"));
        Assert.Contains("SetPowerModeInt:0", P.PowerService.Calls);
    }
}
