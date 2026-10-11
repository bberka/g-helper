using GHelper.Mode;
using Xunit;

namespace GHelper.Tests.Engine;

public class PowerNativeTests : EngineTestBase
{
    public PowerNativeTests(EngineFixture fixture) : base(fixture) { }

    [Fact]
    public void FacadeDelegatesEveryCallToThePowerService()
    {
        PowerNative.SetPowerMode("scheme-a");
        PowerNative.SetPowerMode(2);
        PowerNative.SetPowerPlan("plan");
        PowerNative.SetCPUBoost(3);
        PowerNative.SetASPM(1);
        PowerNative.SetBalancedASPM(2);
        PowerNative.SetConnectivityInStandby(1, 2);
        PowerNative.SetLidAction(3, true);
        PowerNative.SetHibernateAfter(30);

        Assert.Equal(new[]
        {
            "SetPowerMode:scheme-a", "SetPowerModeInt:2", "SetPowerPlan:plan", "SetCPUBoost:3", "SetASPM:1",
            "SetBalancedASPM:2", "SetConnectivityInStandby:1,2", "SetLidAction:3,True", "SetHibernateAfter:30",
        }, P.PowerService.Calls.ToArray());
    }

    [Fact]
    public void FacadeReadsFromThePowerService()
    {
        P.PowerService.CpuBoost = 2;
        P.PowerService.Aspm = 1;
        P.PowerService.LidAction = 3;
        P.PowerService.BatterySaver = true;

        Assert.Equal(2, PowerNative.GetCPUBoost());
        Assert.Equal(1, PowerNative.GetASPM());
        Assert.Equal(3, PowerNative.GetLidAction(true));
        Assert.True(PowerNative.GetBatterySaverStatus());
        Assert.Equal("balanced", PowerNative.GetPowerMode());
        Assert.Equal("mode1", PowerNative.GetDefaultPowerMode(1));
    }

    [Fact]
    public void PowerModesDefaultToWindowsOverlays()
    {
        var modes = PowerNative.powerModes;

        Assert.Equal(4, modes.Count);
        Assert.Equal("Best Power Efficiency", modes["961cc777-2547-4f9d-8174-7d86181b8a7a"]);
        Assert.Equal("Balanced", modes["00000000-0000-0000-0000-000000000000"]);
        Assert.Equal("Best Performance", modes["ded574b5-45a0-4f42-8737-46345c09c238"]);
    }

    [Fact]
    public void PowerModesCanBeOverriddenByThePlatform()
    {
        P.PowerService.Catalog = new Dictionary<string, string> { ["low-power"] = "Low power", ["performance"] = "Performance" };

        var modes = PowerNative.powerModes;

        Assert.Equal(2, modes.Count);
        Assert.Equal("Low power", modes["low-power"]);
    }
}
