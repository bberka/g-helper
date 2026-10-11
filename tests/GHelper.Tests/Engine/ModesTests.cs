using GHelper.Mode;
using Xunit;

namespace GHelper.Tests.Engine;

public class ModesTests : EngineTestBase
{
    public ModesTests(EngineFixture fixture) : base(fixture) { }

    private static void RemoveCustomModes()
    {
        for (int i = 3; i < 20; i++) Modes.Remove(i);
        CleanConfig("performance_mode", "performance_0", "performance_1", "limit_total_0", "fan_profile_cpu_0");
    }

    [Fact]
    public void BuiltInModesComeInUiOrder()
    {
        RemoveCustomModes();

        Assert.Equal(new List<int> { 2, 0, 1 }, Modes.GetList());
        Assert.Equal(new[] { 2, 0, 1 }, Modes.GetDictonary().Keys.ToArray());
    }

    [Fact]
    public void BuiltInModesAreTheirOwnBase()
    {
        Assert.Equal(0, Modes.GetBase(0));
        Assert.Equal(1, Modes.GetBase(1));
        Assert.Equal(2, Modes.GetBase(2));
        Assert.True(Modes.Exists(0));
        Assert.False(Modes.Exists(3));
    }

    [Fact]
    public void AddCreatesCustomModeFromCurrentOne()
    {
        RemoveCustomModes();
        AppConfig.Set("performance_mode", 1);
        AppConfig.Set("limit_total_1", 90);
        AppConfig.Set("fan_profile_cpu_1", "1E-3F-44-48-4C-50-54-62-11-1A-22-29-34-43-51-5A");

        try
        {
            int id = Modes.Add();

            Assert.Equal(3, id);
            Assert.True(Modes.Exists(3));
            Assert.Equal(1, Modes.GetBase(3));
            Assert.Equal("Custom 1", Modes.GetName(3));
            Assert.Equal(90, AppConfig.Get("limit_total_3"));
            Assert.Equal("1E-3F-44-48-4C-50-54-62-11-1A-22-29-34-43-51-5A", AppConfig.GetString("fan_profile_cpu_3"));
            Assert.Contains(3, Modes.GetList());
            Assert.Equal("Custom 1", Modes.GetDictonary()[3]);
        }
        finally
        {
            Modes.Remove(3);
            CleanConfig("limit_total_1", "fan_profile_cpu_1", "performance_mode");
        }

        Assert.False(Modes.Exists(3));
    }

    [Fact]
    public void AddUsesNextFreeSlot()
    {
        RemoveCustomModes();
        AppConfig.Set("performance_mode", 0);

        try
        {
            Assert.Equal(3, Modes.Add());
            Assert.Equal(4, Modes.Add());
            Assert.Equal("Custom 2", Modes.GetName(4));
            Assert.True(Modes.IsCurrentCustom() == false);
        }
        finally
        {
            RemoveCustomModes();
        }
    }

    [Fact]
    public void GetNextWrapsAround()
    {
        RemoveCustomModes();
        AppConfig.Set("performance_mode", 1);

        Assert.Equal(2, Modes.GetNext());      // list is 2,0,1 -> after turbo comes silent
        Assert.Equal(0, Modes.GetNext(back: true));

        AppConfig.Set("performance_mode", 2);
        Assert.Equal(1, Modes.GetNext(back: true));
        Assert.Equal(0, Modes.GetNext());

        CleanConfig("performance_mode");
    }

    [Fact]
    public void SetCurrentStoresModePerPowerSource()
    {
        Program.usbcProfile = false;
        P.PowerService.Source = Platform.PowerSource.AC;
        Modes.SetCurrent(1);
        Assert.Equal(1, AppConfig.Get("performance_1"));
        Assert.Equal(1, Modes.GetCurrent());

        P.PowerService.Source = Platform.PowerSource.Battery;
        Modes.SetCurrent(2);
        Assert.Equal(2, AppConfig.Get("performance_0"));
        Assert.True(!Modes.IsCurrentCustom());

        CleanConfig("performance_mode", "performance_0", "performance_1");
    }

    [Fact]
    public void InitFullSpeedAddsModeOnlyOnce()
    {
        RemoveCustomModes();
        Asus.Values[AsusACPI.VivoBookMode] = 0x40000;

        try
        {
            Modes.InitFullSpeed();
            Modes.InitFullSpeed();

            Assert.True(Modes.Exists(3));
            Assert.Equal(AsusACPI.PerformanceFullSpeed, Modes.GetBase(3));
            Assert.Equal("Full Speed", Modes.GetName(3));
            Assert.False(Modes.Exists(4));
        }
        finally
        {
            RemoveCustomModes();
        }
    }

    [Fact]
    public void InitFullSpeedIgnoresUnsupportedBios()
    {
        RemoveCustomModes();
        Modes.InitFullSpeed();   // VivoBookMode not supported
        Assert.False(Modes.Exists(3));

        Asus.Values[AsusACPI.VivoBookMode] = 0x1;   // supported but no full speed flag
        Modes.InitFullSpeed();
        Assert.False(Modes.Exists(3));
    }
}
