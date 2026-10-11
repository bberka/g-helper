using GHelper.Platform;
using GHelper.Platform.Windows.Power;
using Xunit;

namespace GHelper.Tests.Windows;

public class PowerSchemesTests
{
    [Theory]
    [InlineData(0, PowerSchemes.Balanced)]
    [InlineData(1, PowerSchemes.Turbo)]
    [InlineData(2, PowerSchemes.Silent)]
    [InlineData(3, PowerSchemes.PlanHighPerformance)]
    [InlineData(4, PowerSchemes.Balanced)]
    [InlineData(-1, PowerSchemes.Balanced)]
    public void DefaultPowerModeFollowsAsusMode(int mode, string expected)
    {
        Assert.Equal(expected, PowerSchemes.GetDefaultPowerMode(mode));
    }

    [Fact]
    public void OverlayGuidsAreRecognisedAndPlansAreNot()
    {
        Assert.True(PowerSchemes.IsOverlay(PowerSchemes.Balanced));
        Assert.True(PowerSchemes.IsOverlay(PowerSchemes.Turbo));
        Assert.True(PowerSchemes.IsOverlay(PowerSchemes.Silent));
        Assert.True(PowerSchemes.IsOverlay(PowerSchemes.Turbo.ToUpperInvariant()));

        Assert.False(PowerSchemes.IsOverlay(PowerSchemes.PlanBalanced));
        Assert.False(PowerSchemes.IsOverlay(PowerSchemes.PlanHighPerformance));
        Assert.False(PowerSchemes.IsOverlay(null));
        Assert.False(PowerSchemes.IsOverlay(""));
    }

    [Fact]
    public void GuidsAreWellFormed()
    {
        foreach (string guid in new[] { PowerSchemes.Silent, PowerSchemes.Balanced, PowerSchemes.Turbo, PowerSchemes.PlanBalanced, PowerSchemes.PlanHighPerformance })
            Assert.True(Guid.TryParse(guid, out _), guid);

        Assert.Equal(Guid.Empty, Guid.Parse(PowerSchemes.Balanced));
    }

    [Fact]
    public void HighPerformancePlanIgnoresConfiguration()
    {
        string plan = PowerSchemes.ResolvePlan(PowerSchemes.PlanHighPerformance, "custom", "usbc", usbcSource: true);
        Assert.Equal(PowerSchemes.PlanHighPerformance, plan);
    }

    [Fact]
    public void OverlayUsesConfiguredPlanOrBalanced()
    {
        Assert.Equal("custom", PowerSchemes.ResolvePlan(PowerSchemes.Turbo, "custom", null, usbcSource: false));
        Assert.Equal(PowerSchemes.PlanBalanced, PowerSchemes.ResolvePlan(PowerSchemes.Turbo, null, null, usbcSource: false));
        Assert.Equal(PowerSchemes.PlanBalanced, PowerSchemes.ResolvePlan(PowerSchemes.Turbo, "", null, usbcSource: false));
    }

    [Fact]
    public void UsbcPlanOnlyWinsOnUsbcCharger()
    {
        Assert.Equal("usbc", PowerSchemes.ResolvePlan(PowerSchemes.Silent, "custom", "usbc", usbcSource: true));
        Assert.Equal("custom", PowerSchemes.ResolvePlan(PowerSchemes.Silent, "custom", "usbc", usbcSource: false));
        // USB-C source without a dedicated plan keeps the configured one
        Assert.Equal("custom", PowerSchemes.ResolvePlan(PowerSchemes.Silent, "custom", null, usbcSource: true));
    }

    [Fact]
    public void NamesCoverOverlaysAndHighPerformance()
    {
        Assert.Equal("Best Performance", PowerSchemes.Names[PowerSchemes.Turbo]);
        Assert.Equal("Best Power Efficiency", PowerSchemes.Names[PowerSchemes.Silent]);
        Assert.Equal("Balanced", PowerSchemes.Names[PowerSchemes.Balanced]);
        Assert.True(PowerSchemes.Names.ContainsKey(PowerSchemes.PlanHighPerformance));
    }
}

public class PowerSettingDecoderTests
{
    [Theory]
    [InlineData(0, true)]   // closed
    [InlineData(1, false)]  // open
    public void LidSwitch(int data, bool closed)
    {
        var e = PowerSettingDecoder.Decode(PowerSettingDecoder.LidSwitchStateChange, data);
        Assert.Equal(PowerSettingKind.Lid, e.Kind);
        Assert.Equal(closed, e.Flag);
    }

    [Theory]
    [InlineData(0, PowerSettingKind.Display, false)]
    [InlineData(1, PowerSettingKind.Display, true)]
    [InlineData(2, PowerSettingKind.Unknown, false)] // dimmed is not reported
    public void ConsoleDisplayState(int data, PowerSettingKind kind, bool on)
    {
        var e = PowerSettingDecoder.Decode(PowerSettingDecoder.ConsoleDisplayState, data);
        Assert.Equal(kind, e.Kind);
        Assert.Equal(on, e.Flag);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void BatterySaverGuidsBothMapToTheSameEvent(int data, bool active)
    {
        foreach (Guid guid in new[] { PowerSettingDecoder.EnergySaverStatus, PowerSettingDecoder.PowerSavingStatus })
        {
            var e = PowerSettingDecoder.Decode(guid, data);
            Assert.Equal(PowerSettingKind.BatterySaver, e.Kind);
            Assert.Equal(active, e.Flag);
        }
    }

    [Theory]
    [InlineData(0, true)]   // AC
    [InlineData(1, false)]  // battery
    [InlineData(2, false)]  // short term (UPS)
    public void PowerSourceSetting(int data, bool onAc)
    {
        var e = PowerSettingDecoder.Decode(PowerSettingDecoder.AcdcPowerSource, data);
        Assert.Equal(PowerSettingKind.PowerSource, e.Kind);
        Assert.Equal(onAc, e.Flag);
    }

    [Fact]
    public void UnknownGuidIsIgnored()
    {
        Assert.Equal(PowerSettingKind.Unknown, PowerSettingDecoder.Decode(Guid.NewGuid(), 1).Kind);
    }

    [Fact]
    public void EveryRegisteredSettingIsDecodable()
    {
        foreach (Guid guid in PowerSettingDecoder.AllSettings)
            Assert.NotEqual(PowerSettingKind.Unknown, PowerSettingDecoder.Decode(guid, 0).Kind);
    }

    [Theory]
    [InlineData((byte)1, PowerSource.AC)]
    [InlineData((byte)0, PowerSource.Battery)]
    [InlineData((byte)255, PowerSource.Unknown)]
    public void AcLineStatusMapping(byte status, PowerSource expected)
    {
        Assert.Equal(expected, PowerSettingDecoder.ToPowerSource(status));
    }
}
