using GHelper.Battery;
using Xunit;

namespace GHelper.Tests.Engine;

public class BatteryControlTests : EngineTestBase
{
    public BatteryControlTests(EngineFixture fixture) : base(fixture)
    {
        BatteryControl.chargeFull = false;
        CleanConfig("charge_limit");
    }

    [Fact]
    public void SetsConfiguredLimitThroughAcpi()
    {
        BatteryControl.SetBatteryChargeLimit(80);

        Assert.Equal(new[] { 80 }, Asus.SetValues(AsusACPI.BatteryLimit));
        Assert.Equal(80, AppConfig.Get("charge_limit"));
        Assert.False(BatteryControl.chargeFull);
        Assert.Contains("battery", Ui.Refreshes);
    }

    [Fact]
    public void UsesStoredLimitWhenNoneGiven()
    {
        AppConfig.Set("charge_limit", 70);

        BatteryControl.SetBatteryChargeLimit();

        Assert.Equal(new[] { 70 }, Asus.SetValues(AsusACPI.BatteryLimit));
    }

    [Theory]
    [InlineData(39)]
    [InlineData(101)]
    public void IgnoresLimitsOutsideBounds(int limit)
    {
        BatteryControl.SetBatteryChargeLimit(limit);

        Assert.Empty(Asus.SetValues(AsusACPI.BatteryLimit));
        Assert.Equal(-1, AppConfig.Get("charge_limit"));
    }

    [Fact]
    public void NoStoredLimitDoesNothing()
    {
        BatteryControl.SetBatteryChargeLimit();
        Assert.Empty(Asus.Sets);
    }

    [Fact]
    public void FullChargeToggleRoundTrip()
    {
        AppConfig.Set("charge_limit", 60);

        BatteryControl.ToggleBatteryLimitFull();

        Assert.True(BatteryControl.chargeFull);
        Assert.Equal(1, AppConfig.Get("charge_full"));
        Assert.Equal(new[] { 100 }, Asus.SetValues(AsusACPI.BatteryLimit));

        Asus.ClearSets();
        BatteryControl.ToggleBatteryLimitFull();

        Assert.False(BatteryControl.chargeFull);
        Assert.Equal(new[] { 60 }, Asus.SetValues(AsusACPI.BatteryLimit));
    }

    [Fact]
    public void AutoBatteryKeepsFullChargeExceptOnInit()
    {
        AppConfig.Set("charge_limit", 60);
        BatteryControl.chargeFull = true;

        BatteryControl.AutoBattery();
        Assert.Equal(new[] { 100 }, Asus.SetValues(AsusACPI.BatteryLimit));

        Asus.ClearSets();
        BatteryControl.AutoBattery(init: true);
        Assert.Equal(new[] { 60 }, Asus.SetValues(AsusACPI.BatteryLimit));
        Assert.False(BatteryControl.chargeFull);
    }

    [Fact]
    public void UnsetFullClearsFlagAndNotifiesUi()
    {
        BatteryControl.chargeFull = true;

        BatteryControl.UnSetBatteryLimitFull();

        Assert.False(BatteryControl.chargeFull);
        Assert.Contains("battery", Ui.Refreshes);
    }

    [Fact]
    public void MirrorsLimitToAsusServiceOnlyWhenElevated()
    {
        BatteryControl.SetBatteryChargeLimit(75);
        Assert.Null(P.VendorService.ChargingRate);

        P.SystemInfo.IsElevated = true;
        BatteryControl.SetBatteryChargeLimit(75);
        Assert.Equal(75, P.VendorService.ChargingRate);
    }

    [Fact]
    public void BatteryReportIsWindowsOnly()
    {
        P.SystemInfo.Kind = Platform.PlatformKind.Linux;
        BatteryControl.BatteryReport();
        Thread.Sleep(50);
        Assert.DoesNotContain(P.ProcessService.Calls, c => c.StartsWith("Run:"));

        P.SystemInfo.Kind = Platform.PlatformKind.Windows;
        BatteryControl.BatteryReport();
        Assert.True(WaitFor(() => P.ProcessService.Calls.Any(c => c.StartsWith("Run:powershell powercfg /batteryreport"))));
    }
}
