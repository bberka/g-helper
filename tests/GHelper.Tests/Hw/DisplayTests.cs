using GHelper.Ally;
using GHelper.Display;
using GHelper.Platform;
using Xunit;

namespace GHelper.Tests.Hw;

public sealed class RecordingUiHost : IUiHost
{
    public readonly List<string> Refreshed = new();
    public readonly List<(string text, string? icon)> Toasts = new();

    public void Post(Action action) => action();
    public void Toast(string text, string? iconKey = null) => Toasts.Add((text, iconKey));
    public bool Confirm(string title, string text) => true;
    public void Alert(string title, string text) { }
    public void ToggleSettings(bool checkForFocus = true, bool trayClick = false) { }
    public void Refresh(string area) => Refreshed.Add(area);
    public void Quit() { }
}

public class VisualControlTests
{
    [Fact]
    public void DefaultVisualModesPassWhiteBalanceAsSecondParameter()
    {
        var args = VisualControl.BuildVisualArguments(SplendidCommand.Racing, 30, isDuo: false, isVivoZenPro: false);

        Assert.Equal(new SplendidArguments(SplendidCommand.Racing, 0, 30, null), args);
    }

    [Fact]
    public void DisabledSendsParameterTwoInTheFirstSlot()
    {
        var args = VisualControl.BuildVisualArguments(SplendidCommand.Disabled, 50, false, false);

        Assert.Equal(new SplendidArguments(SplendidCommand.Disabled, 2, null, null), args);
    }

    [Fact]
    public void EyecareUsesFixedLevelFour()
    {
        var args = VisualControl.BuildVisualArguments(SplendidCommand.Eyecare, 10, false, false);

        Assert.Equal(4, args.Param2);
    }

    [Theory]
    [InlineData(50, 0)]    // neutral
    [InlineData(0, 4)]     // warmest: |0-50|*3/50 = 3, +1 below the neutral point
    [InlineData(25, 2)]    // 25*3/50 = 1, +1
    [InlineData(100, 3)]   // coldest: 3, no extra
    [InlineData(75, 1)]    // 25*3/50 = 1
    public void VivoEyecareLevelFollowsWhiteBalance(int whiteBalance, int expected)
    {
        var args = VisualControl.BuildVisualArguments(SplendidCommand.VivoEycare, whiteBalance, false, true);

        Assert.Equal(expected, args.Param2);
    }

    [Fact]
    public void EReadingSendsContrastAndTemperature()
    {
        var args = VisualControl.BuildVisualArguments(SplendidCommand.EReading, 35, false, false);

        Assert.Equal(new SplendidArguments(SplendidCommand.EReading, 0, 2, 35), args);
    }

    [Fact]
    public void EReadingOnVivobookDuoSwitchesToTheVivoCommand()
    {
        var args = VisualControl.BuildVisualArguments(SplendidCommand.EReading, 35, isDuo: true, isVivoZenPro: true);

        Assert.Equal(SplendidCommand.EReadingVivo, args.Command);
    }

    [Theory]
    [InlineData(SplendidCommand.VivoNormal, SplendidCommand.VivoNormal + 100)]
    [InlineData(SplendidCommand.VivoEycare, SplendidCommand.VivoEycare + 100)]
    [InlineData(SplendidCommand.Default, SplendidCommand.Default + 300)]
    [InlineData(SplendidCommand.Cinema, SplendidCommand.Cinema + 300)]
    [InlineData(SplendidCommand.DimmingVivo, SplendidCommand.DimmingDuo)]
    [InlineData(SplendidCommand.DimmingVisual, SplendidCommand.DimmingVisualDuo)]
    [InlineData(SplendidCommand.GamutMode, SplendidCommand.GamutModeDuo)]
    [InlineData(SplendidCommand.EReading, SplendidCommand.EReadingDuo)]
    [InlineData(SplendidCommand.EReadingVivo, SplendidCommand.EReadingVivoDuo)]
    [InlineData(SplendidCommand.Init, SplendidCommand.None)]
    public void DuoCommandIsTheSecondScreenTwin(SplendidCommand command, SplendidCommand expected)
    {
        Assert.Equal(expected, VisualControl.GetDuoCommand(command, isDuo: true));
    }

    [Fact]
    public void NonDuoDeviceHasNoSecondScreenCommand()
    {
        Assert.Equal(SplendidCommand.None, VisualControl.GetDuoCommand(SplendidCommand.Default, isDuo: false));
    }

    [Theory]
    [InlineData(0, 40)]
    [InlineData(50, 70)]
    [InlineData(100, 100)]
    public void DimmingLevelMapsSliderToSplendidRange(int brightness, int level)
    {
        Assert.Equal(level, VisualControl.GetDimmingLevel(brightness));
    }
}

[Collection("Hw platform")]
public class DisplayFacadeTests
{
    private static void UseDisplay(Action<StubProxy> configure)
    {
        var platform = HwTestPlatform.Ensure();
        platform.DisplayService = StubProxy.Create<IDisplayService>(configure);
    }

    [Fact]
    public void MaxRefreshRateIsCachedInConfigWhenTheDriverReportsNothing()
    {
        int reported = 165;
        UseDisplay(p => p.Handlers["GetMaxRefreshRate"] = _ => reported);

        Assert.Equal(165, ScreenControl.GetMaxRate("eDP-1"));
        Assert.Equal(165, AppConfig.Get("screen_max"));

        reported = 0;
        Assert.Equal(165, ScreenControl.GetMaxRate("eDP-1"));
    }

    [Fact]
    public void NoScreenMeansNoRefreshRate()
    {
        UseDisplay(p => p.Handlers["GetMaxRefreshRate"] = _ => 240);

        Assert.Equal(-1, ScreenControl.GetMaxRate(null));
    }

    [Fact]
    public void BrightnessAdjustIsClampedToThePercentRange()
    {
        int current = 95;
        UseDisplay(p =>
        {
            p.Handlers["GetBrightness"] = _ => current;
            p.Handlers["SetBrightness"] = a => { current = (int)a![0]!; return null; };
        });

        Assert.Equal(100, ScreenBrightness.Adjust(10));
        Assert.Equal(100, current);
        Assert.Equal(0, ScreenBrightness.Adjust(-500));
        Assert.Equal(0, current);
    }

    [Fact]
    public void UncontrollableBacklightReadsAsZero()
    {
        UseDisplay(p => p.Handlers["GetBrightness"] = _ => -1);

        Assert.Equal(0, ScreenBrightness.Get());
    }

    [Fact]
    public void HdrStatusIsForwardedWithAdvancedColorFlag()
    {
        UseDisplay(p => p.Handlers["GetHdrStatus"] = a =>
        {
            a![0] = true;   // acm
            return false;   // hdr
        });

        Assert.False(ScreenCCD.GetHDRStatus(out bool acm, false));
        Assert.True(acm);
        Assert.False(ScreenCCD.IsHDR());
    }

    [Fact]
    public void HdrFailureCountsAsNotHdr()
    {
        UseDisplay(p => p.Handlers["GetHdrStatus"] = _ => throw new InvalidOperationException("display config"));

        Assert.False(ScreenCCD.IsHDR());
    }

    [Fact]
    public void ElmbIsGatedByTheModel()
    {
        bool called = false;
        UseDisplay(p => p.Handlers["ElmbAccess"] = _ => { called = true; return 1; });

        // test model string is empty: not an ELMB panel, the platform must not be touched
        Assert.Equal(-1, ScreenELMB.Get());
        Assert.False(called);
    }

    [Fact]
    public void GameVisualFollowsTheRegistryStatus()
    {
        UseDisplay(p => p.Handlers["GetGameVisualStatus"] = _ => 0);
        Assert.False(VisualControl.IsEnabled());

        UseDisplay(p => p.Handlers["GetGameVisualStatus"] = _ => 1);
        Assert.True(VisualControl.IsEnabled());
    }
}

[Collection("Hw platform")]
public class AllyFpsLimitTests
{
    [Fact]
    public void FpsLimitCyclesThroughThePresetsAndNotifiesTheUi()
    {
        var platform = HwTestPlatform.Ensure();
        var driver = new FakeAmdDriver { HasDiscrete = false, HasIntegrated = true, FpsLimit = -1 };
        platform.GpuProvider.AmdDriver = driver;
        var ui = new RecordingUiHost();
        var previous = Program.Ui;
        Program.Ui = ui;

        try
        {
            var ally = new AllyControl();
            var seen = new List<int>();
            for (int i = 0; i < 10; i++)
            {
                ally.ToggleFPSLimit(toast: true);
                seen.Add(driver.FpsLimit);
            }

            Assert.Equal(new[] { 30, 40, 45, 50, 60, 75, 90, 120, 240, 30 }, seen);
            Assert.Equal(30, AllyControl.FpsLimit);
            Assert.Equal(10, ui.Refreshed.Count(a => a == "fpslimit"));
            Assert.Equal("FPS Limit 30", ui.Toasts[0].text);
            Assert.Equal("FPS Limit OFF", ui.Toasts[8].text);
        }
        finally
        {
            Program.Ui = previous;
            platform.GpuProvider.AmdDriver = null;
        }
    }

    [Fact]
    public void BindingTableIsComplete()
    {
        var groups = AllyControl.BindingGroups;

        Assert.Contains(groups, g => g.GroupLabel == "Controller");
        var codes = groups.SelectMany(g => g.Items).Select(i => i.Code).ToList();
        Assert.Contains("00-00", codes);
        Assert.Contains(AllyControl.BindA, codes);
        Assert.Contains(AllyControl.BindToggleMode, codes);
        Assert.All(codes, c => Assert.Matches("^[0-9A-F]{2}(-[0-9A-F]{2})+$|^$", c));
    }
}
