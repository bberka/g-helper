using System.Reflection;
using Xunit;

namespace GHelper.Tests.Engine;

public class AppConfigTests : EngineTestBase
{
    public AppConfigTests(EngineFixture fixture) : base(fixture) { }

    private static bool Invoke(string method, string path)
    {
        var m = typeof(AppConfig).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(AppConfig), method);
        return (bool)m.Invoke(null, new object[] { path })!;
    }

    private string TempFile(string content)
    {
        var path = Path.Combine(EngineFixture.ConfigDir, "cfg-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void NumbersBooleansAndStrings()
    {
        AppConfig.Set("a_int", 5);
        AppConfig.Set("a_text", "hello");
        AppConfig.Set("a_numeric_text", "42");
        AppConfig.Set("a_zero", 0);
        AppConfig.Set("a_one", 1);

        Assert.Equal(5, AppConfig.Get("a_int"));
        Assert.Equal("hello", AppConfig.GetString("a_text"));
        Assert.Equal(42, AppConfig.Get("a_numeric_text"));      // numbers stored as text still parse
        Assert.Equal(-1, AppConfig.Get("a_text"));              // not a number -> default
        Assert.Equal(7, AppConfig.Get("missing", 7));
        Assert.Null(AppConfig.GetString("missing"));
        Assert.Equal("dflt", AppConfig.GetString("missing", "dflt"));

        Assert.True(AppConfig.Is("a_one"));
        Assert.False(AppConfig.Is("a_zero"));
        Assert.False(AppConfig.Is("missing"));

        // IsNotFalse: only an explicit 0 turns a default-on option off
        Assert.True(AppConfig.IsNotFalse("missing"));
        Assert.True(AppConfig.IsNotFalse("a_one"));
        Assert.False(AppConfig.IsNotFalse("a_zero"));
    }

    [Fact]
    public void RemoveAndExists()
    {
        AppConfig.Set("tmp_key", 1);
        Assert.True(AppConfig.Exists("tmp_key"));

        AppConfig.Remove("tmp_key");
        Assert.False(AppConfig.Exists("tmp_key"));
    }

    [Fact]
    public void ModeScopedKeysFollowCurrentMode()
    {
        AppConfig.Set("performance_mode", 2);
        AppConfig.Set("limit_total_2", 45);
        AppConfig.Set("powermode_2", "scheme");
        AppConfig.Set("auto_apply_2", 1);

        Assert.Equal(45, AppConfig.GetMode("limit_total"));
        Assert.Equal("scheme", AppConfig.GetModeString("powermode"));
        Assert.True(AppConfig.IsMode("auto_apply"));
        Assert.True(AppConfig.IsApplyFans());
        Assert.False(AppConfig.IsApplyPower());

        AppConfig.SetMode("limit_total", 50);
        Assert.Equal(50, AppConfig.Get("limit_total_2"));

        AppConfig.RemoveMode("powermode");
        Assert.False(AppConfig.Exists("powermode_2"));
    }

    [Fact]
    public void OnBatteryOverrideFallsBackToBaseSetting()
    {
        AppConfig.Set("zone", 1);
        Assert.True(AppConfig.IsOnBattery("zone"));

        AppConfig.Set("zone_bat", 0);
        Assert.False(AppConfig.IsOnBattery("zone"));
    }

    [Fact]
    public void FanCurveConfigRoundTrip()
    {
        AppConfig.Set("performance_mode", 0);
        var curve = new byte[] { 30, 40, 50, 60, 70, 80, 90, 100, 0, 10, 20, 30, 40, 50, 60, 70 };

        AppConfig.SetFanConfig(AsusFan.CPU, curve);

        Assert.Equal("1E-28-32-3C-46-50-5A-64-00-0A-14-1E-28-32-3C-46", AppConfig.GetString("fan_profile_cpu_0"));
        Assert.Equal(curve, AppConfig.GetFanConfig(AsusFan.CPU));
        Assert.Empty(AppConfig.GetFanConfig(AsusFan.GPU));   // nothing stored yet
    }

    [Fact]
    public void FanCurveKeysAreNamedPerFanAndMode()
    {
        AppConfig.Set("performance_mode", 3);

        Assert.Equal("fan_profile_cpu_3", AppConfig.GgetParamName(AsusFan.CPU));
        Assert.Equal("fan_profile_gpu_3", AppConfig.GgetParamName(AsusFan.GPU));
        Assert.Equal("fan_profile_mid_3", AppConfig.GgetParamName(AsusFan.Mid));
        Assert.Equal("fan_profile_xgm_3", AppConfig.GgetParamName(AsusFan.XGM));
    }

    [Fact]
    public void StringToBytesParsesHexPairs()
    {
        Assert.Equal(new byte[] { 0x1E, 0xFF, 0x00 }, AppConfig.StringToBytes("1E-FF-00"));
    }

    [Theory]
    [InlineData(AsusACPI.PerformanceBalanced, "3A-3D-40-44-48-4D-51-62-08-11-16-1A-22-29-30-45")]
    [InlineData(AsusACPI.PerformanceTurbo, "1E-3F-44-48-4C-50-54-62-11-1A-22-29-34-43-51-5A")]
    [InlineData(AsusACPI.PerformanceSilent, "1E-31-3B-42-47-50-5A-64-00-00-03-0C-14-1C-22-29")]
    public void DefaultCpuCurvePerBaseMode(int mode, string expected)
    {
        AppConfig.Set("performance_mode", mode);

        Assert.Equal(AppConfig.StringToBytes(expected), AppConfig.GetDefaultCurve(AsusFan.CPU));
    }

    [Fact]
    public void DefaultCurvesAreValidForEveryDevice()
    {
        foreach (int mode in new[] { 0, 1, 2 })
        {
            AppConfig.Set("performance_mode", mode);
            foreach (var fan in new[] { AsusFan.CPU, AsusFan.GPU })
                Assert.False(AsusACPI.IsInvalidCurve(AppConfig.GetDefaultCurve(fan)));
        }
    }

    [Fact]
    public void ModelDetectionUsesPlatformModelString()
    {
        Assert.Equal("ROG Zephyrus G14 GA402RJ_GA402RJ", AppConfig.GetModel());
        Assert.Equal("ROG Zephyrus G14 GA402RJ", AppConfig.GetModelShort());
        Assert.True(AppConfig.ContainsModel("ga402rj"));
        Assert.True(AppConfig.IsASUS());
        Assert.True(AppConfig.IsG14AMD());
        Assert.False(AppConfig.IsAlly());
        Assert.False(AppConfig.IsDesktop());
    }

    [Fact]
    public void BrokenConfigIsRecoveredKeyByKey()
    {
        var path = TempFile("{\"keep_int\": 12, \"keep_text\": \"abc\", \"broken\": [1,2, \"half\": ");

        Assert.True(Invoke("TryRecoverConfig", path));

        Assert.Equal(12, AppConfig.Get("keep_int"));
        Assert.Equal("abc", AppConfig.GetString("keep_text"));
    }

    [Fact]
    public void LenientLoaderAcceptsCommentsAndTrailingCommas()
    {
        var path = TempFile("{\n // user edited\n \"performance_mode\": 2,\n \"charge_limit\": 80,\n}");

        Assert.True(Invoke("TryLoadConfig", path));

        Assert.Equal(2, AppConfig.Get("performance_mode"));
        Assert.Equal(80, AppConfig.Get("charge_limit"));
    }

    [Fact]
    public void MissingFileIsNotLoaded()
    {
        var path = Path.Combine(EngineFixture.ConfigDir, "does-not-exist.json");

        Assert.False(Invoke("TryLoadConfig", path));
        Assert.False(Invoke("TryRecoverConfig", path));
    }
}
