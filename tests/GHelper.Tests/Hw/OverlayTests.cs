using System.Drawing;
using System.Globalization;
using GHelper.Overlay;
using Xunit;

namespace GHelper.Tests.Hw;

public class OverlayFormatTests
{
    [Theory]
    [InlineData(0.0, " -- ")]
    [InlineData(-1.0, " -- ")]
    [InlineData(62.4, " 62°")]
    [InlineData(100.0, "100°")]
    public void TemperatureIsPaddedToFourCharacters(double temp, string expected)
    {
        Assert.Equal(expected, OverlayFormat.FmtTemp(temp));
    }

    [Fact]
    public void PowerAndBatteryUseOneDecimal()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal("", OverlayFormat.FmtPow(0));
            Assert.Equal("45.3W", OverlayFormat.FmtPow(45.26));
            Assert.Equal("", OverlayFormat.FmtGpuPower(null));
            Assert.Equal("120.9W", OverlayFormat.FmtGpuPower(120.94f));
            Assert.Equal("+12.3", OverlayFormat.FormatBatteryRate(12.34m));
            Assert.Equal("-8.1", OverlayFormat.FormatBatteryRate(-8.1m));
            Assert.Equal("", OverlayFormat.FormatBatteryRate(0m));
            Assert.Equal("127.9GB", OverlayFormat.FormatMemGb(130970));
            Assert.Equal("", OverlayFormat.FormatMemGb(null));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void FanAndLevelFormatting()
    {
        Assert.Equal("", OverlayFormat.FormatFan(null));
        Assert.Equal("", OverlayFormat.FormatFan(-1));
        Assert.Equal("3200", OverlayFormat.FormatFan(3200));
        Assert.Equal("--", OverlayFormat.FormatBatteryLevel(0));
        Assert.Equal("56%", OverlayFormat.FormatBatteryLevel(55.6));
        Assert.Equal("37%", OverlayFormat.FormatUsage(37));
        Assert.Equal("", OverlayFormat.FormatUsage(null));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070 Laptop GPU", "RTX 4070")]
    [InlineData("NVIDIA GeForce RTX 4060 Ti Laptop GPU", "RTX 4060 Ti")]
    [InlineData("AMD Radeon RX 6850M XT", "RX 6850M")]
    [InlineData("Intel(R) Arc(TM) A370M Graphics", "Arc(TM) A370M")]
    [InlineData("Some GPU", "Some GPU")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void GpuNamesAreShortened(string? full, string expected)
    {
        Assert.Equal(expected, OverlayFormat.ShortGpuName(full));
    }

    [Theory]
    [InlineData("13th Gen Intel(R) Core(TM) i9-13980HX", "i9-13980HX")]
    [InlineData("Intel(R) Core(TM) Ultra 9 185H", "Ultra 185H")]
    [InlineData("AMD Ryzen 9 7945HX with Radeon Graphics", "Ryzen 7945HX")]
    [InlineData("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics", "Ryzen 7840HS")]
    [InlineData("AMD Ryzen AI 9 HX 370 w/ Radeon 890M", "Ryzen HX 370")]
    [InlineData("Snapdragon X Elite", "Snapdragon")]
    [InlineData("", "")]
    public void CpuNamesAreShortened(string name, string expected)
    {
        Assert.Equal(expected, OverlayFormat.ShortCpuName(name));
    }
}

public class OverlayLayoutTests
{
    private static readonly OverlayBlocks DefaultBlocks = new(Fps: true, Temp: true, Fans: true, Chart: true, Power: true, Usage: false, Ram: false, Names: false, Battery: false);

    [Fact]
    public void DefaultPresetAtBaseScaleHasTheExpectedColumns()
    {
        var l = OverlayLayout.Compute(OverlayLayout.BaseScale, DefaultBlocks, batteryVisible: false);

        Assert.Equal(90, l.Height);          // 2 * 8 pad + 2 * 36 lines + 2 gap
        Assert.Equal(136, l.LeftX);          // pad 16 + fps 104 + gap 16
        Assert.Equal(392, l.ChartX);         // + 256 temp/fan column
        Assert.Equal(640, l.PowerX);         // chart 240 + power gap 8
        Assert.Equal(748, l.Width);          // power column 92 + right pad 16
    }

    [Fact]
    public void LightPresetDropsChartAndFanColumn()
    {
        var blocks = DefaultBlocks with { Fans = false, Chart = false };

        var l = OverlayLayout.Compute(2f, blocks, false);

        // pad 16 + fps 104 + gap 16 + narrow temp column 128 + power gap 8 + power 92 + pad 16
        Assert.Equal(380, l.Width);
    }

    [Fact]
    public void CompleteBlocksAddUsageMemoryAndBatteryColumns()
    {
        var all = DefaultBlocks with { Usage = true, Ram = true, Names = true, Battery = true };

        var without = OverlayLayout.Compute(2f, all with { Usage = false, Ram = false, Names = false, Battery = false }, false);
        var full = OverlayLayout.Compute(2f, all, batteryVisible: true);

        Assert.True(full.Width > without.Width);
        Assert.True(full.BarX > full.UsageNumX);
        Assert.True(full.MemNumX > full.BarX);
        Assert.True(full.MemBarX > full.MemNumX);
        Assert.True(full.BatteryX > full.MemBarX);
        Assert.True(full.BatteryIconX > full.BatteryX);
        Assert.Equal(full.BatteryIconX + full.BatteryIconWidth + full.PadX, full.Width);
    }

    [Fact]
    public void BatteryColumnOnlyAppearsWhileOnBattery()
    {
        var blocks = DefaultBlocks with { Battery = true };

        var ac = OverlayLayout.Compute(2f, blocks, batteryVisible: false);
        var battery = OverlayLayout.Compute(2f, blocks, batteryVisible: true);

        Assert.True(battery.Width > ac.Width);
    }

    [Fact]
    public void UsageBarHasTenCellsAtDefaultScale()
    {
        var bar = OverlayLayout.ComputeBar(2f, lineHeight: 36);

        Assert.Equal(2, bar.CellHeight);
        Assert.Equal(1, bar.SeparatorHeight);
        Assert.Equal(10, bar.NumCells);
        Assert.Equal(29, bar.BarHeight);
        Assert.Equal(1, bar.BarYOffset);
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(100, 10, 10)]
    [InlineData(150, 10, 10)]
    public void LitCellsRoundUp(int usage, int cells, int expected)
    {
        Assert.Equal(expected, OverlayLayout.LitCells(usage, cells));
    }

    [Fact]
    public void PositionRoundTripsThroughAnchors()
    {
        var screen = new Rectangle(1920, 0, 2560, 1440);
        var window = new Size(748, 90);

        foreach (var location in new[] { new Point(1930, 10), new Point(2000, 1300), new Point(3600, 40), new Point(3700, 1330) })
        {
            var (anchor, ox, oy) = OverlayLayout.ToAnchor(screen, location, window);
            Assert.Equal(location, OverlayLayout.RestorePosition(screen, window, anchor, ox, oy));
        }
    }

    [Fact]
    public void AnchorBitsDescribeTheNearestCorner()
    {
        var screen = new Rectangle(0, 0, 1000, 800);
        var window = new Size(100, 50);

        Assert.Equal(0, OverlayLayout.ToAnchor(screen, new Point(10, 10), window).anchor);
        Assert.Equal(1, OverlayLayout.ToAnchor(screen, new Point(880, 10), window).anchor);
        Assert.Equal(2, OverlayLayout.ToAnchor(screen, new Point(10, 740), window).anchor);
        Assert.Equal(3, OverlayLayout.ToAnchor(screen, new Point(880, 740), window).anchor);
    }

    [Fact]
    public void RestoredPositionStaysOnScreen()
    {
        var screen = new Rectangle(0, 0, 1000, 800);
        var window = new Size(100, 50);

        Assert.Equal(new Point(10, 10), OverlayLayout.RestorePosition(screen, window, -1, 0, 0));
        Assert.Equal(new Point(5, 5), OverlayLayout.RestorePosition(screen, window, 0, -400, -400));
        Assert.Equal(new Point(895, 745), OverlayLayout.RestorePosition(screen, window, 0, 5000, 5000));
    }
}

public class OverlaySettingsTests
{
    [Theory]
    [InlineData("#00FF50", 0, 255, 80)]
    [InlineData("3CDCFF", 60, 220, 255)]
    [InlineData("#0f8", 0, 255, 136)]
    [InlineData("red", 255, 0, 0)]
    public void ColorsParseFromConfigStrings(string text, int r, int g, int b)
    {
        var c = OverlaySettings.ParseColor(text, Color.Black);

        Assert.Equal((255, r, g, b), (c.A, c.R, c.G, c.B));
    }

    [Fact]
    public void ArgbColorKeepsItsAlpha()
    {
        var c = OverlaySettings.ParseColor("#80102030", Color.Black);

        Assert.Equal((0x80, 0x10, 0x20, 0x30), (c.A, c.R, c.G, c.B));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12")]
    [InlineData("#GGHHII")]
    [InlineData("notacolor")]
    public void BadColorsFallBack(string? text)
    {
        var fallback = Color.FromArgb(1, 2, 3, 4);

        Assert.Equal(fallback, OverlaySettings.ParseColor(text, fallback));
    }

    [Fact]
    public void PresetsHaveTheirDefaultBlocks()
    {
        Assert.False(OverlaySettings.BlockDefault("overlay_show_fans", OverlayMode.Light));
        Assert.False(OverlaySettings.BlockDefault("overlay_show_chart", OverlayMode.Light));
        Assert.True(OverlaySettings.BlockDefault("overlay_show_fans", OverlayMode.Default));
        Assert.False(OverlaySettings.BlockDefault("overlay_show_usage", OverlayMode.Default));
        Assert.True(OverlaySettings.BlockDefault("overlay_show_usage", OverlayMode.Full));
        Assert.False(OverlaySettings.BlockDefault("overlay_show_ram", OverlayMode.Full));
        Assert.True(OverlaySettings.BlockDefault("overlay_show_ram", OverlayMode.Complete));
        Assert.False(OverlaySettings.BlockDefault("overlay_names", OverlayMode.Complete));
        Assert.True(OverlaySettings.BlockDefault("overlay_show_fps", OverlayMode.Light));
    }

    [Fact]
    public void ModeKeysAreSuffixedWithTheModeNumber()
    {
        Assert.Equal("overlay_show_fps_2", OverlaySettings.ModeKey("overlay_show_fps", 2));
    }

    [Fact]
    public void ChartFillIsAThirdOfTheBrightnessWithHalfAlpha()
    {
        var fill = OverlaySettings.ChartFill(Color.FromArgb(255, 0, 255, 80));

        Assert.Equal((128, 0, 85, 26), (fill.A, fill.R, fill.G, fill.B));
    }
}

[Collection("Hw platform")]
public class OverlayModelTests
{
    private const int NoSuchPid = 2_000_000_000;

    private static void Reset()
    {
        foreach (string key in new[] { "overlay_game_only", "overlay_mode", "overlay_scale_percent" })
            AppConfig.Remove(key);
    }

    [Fact]
    public void FpsCounterIsPinnedToTheForegroundProcess()
    {
        var platform = HwTestPlatform.Ensure();
        Reset();
        platform.FpsFactory.Created.Clear();
        platform.FpsFactory.ForegroundPid = NoSuchPid;

        using var model = new OverlayModel();
        model.Start();
        try
        {
            Assert.True(model.Active);
            Assert.Single(platform.FpsFactory.Created);
            var monitor = platform.FpsFactory.Created[0];

            // first tick saw the new foreground process and re-targeted the counter
            Assert.Equal(NoSuchPid, monitor.TargetPid);
            Assert.Equal(0, model.CurrentFps);

            monitor.Fps = 143.6;
            model.Tick();
            Assert.Equal(144, model.CurrentFps);
        }
        finally
        {
            model.Stop();
        }

        Assert.True(platform.FpsFactory.Created[0].Disposed);
        Assert.False(model.Active);
    }

    [Fact]
    public void GameOnlyModeShowsTheOverlayAfterTwoFramesTicks()
    {
        var platform = HwTestPlatform.Ensure();
        Reset();
        AppConfig.Set("overlay_game_only", 1);
        platform.FpsFactory.Created.Clear();
        platform.FpsFactory.ForegroundPid = NoSuchPid;

        var events = new List<bool>();
        using var model = new OverlayModel();
        model.VisibilityChanged += events.Add;
        model.Start();
        try
        {
            Assert.True(model.Hidden);
            Assert.Equal(new[] { true }, events);

            var monitor = platform.FpsFactory.Created[0];
            monitor.Fps = 90;
            model.Tick();               // fps now reads 90, first game tick
            Assert.True(model.Hidden);
            model.Tick();               // second game tick latches the process
            Assert.False(model.Hidden);
            Assert.Equal(new[] { true, false }, events);

            // another foreground app hides it again
            platform.FpsFactory.ForegroundPid = NoSuchPid + 1;
            model.Tick();
            Assert.True(model.Hidden);
        }
        finally
        {
            model.Stop();
            Reset();
        }
    }

    [Fact]
    public void ClickCyclesThroughThePresetsAndPersists()
    {
        HwTestPlatform.Ensure();
        Reset();

        using var model = new OverlayModel();
        model.Start();
        try
        {
            Assert.Equal(OverlayMode.Default, model.Mode);
            model.CycleMode();
            Assert.Equal(OverlayMode.Full, model.Mode);
            Assert.Equal(2, AppConfig.Get("overlay_mode"));
            model.CycleMode();
            Assert.Equal(OverlayMode.Complete, model.Mode);
            model.CycleMode();
            Assert.Equal(OverlayMode.Light, model.Mode);
            model.CycleMode();
            Assert.Equal(OverlayMode.Default, model.Mode);
        }
        finally
        {
            model.Stop();
            Reset();
        }
    }

    [Fact]
    public void ScaleIsClampedAndSaved()
    {
        HwTestPlatform.Ensure();
        Reset();

        using var model = new OverlayModel();

        Assert.Equal(300, model.ApplyScale(1000));
        Assert.Equal(300, AppConfig.Get("overlay_scale_percent"));
        Assert.Equal(35, model.ApplyScale(1));
        Reset();
    }

    [Fact]
    public void ChartPeakHasAMinimumAndStacksCpuOnGpu()
    {
        Assert.Equal(10f, OverlayModel.ChartPeak(new float[] { 1, 2 }, new float[] { 1, 2 }));
        Assert.Equal(90f, OverlayModel.ChartPeak(new float[] { 40, 10 }, new float[] { 50, 20 }));
    }

    [Fact]
    public void DesktopApplicationsAreNotGames()
    {
        Assert.True(OverlayModel.IsDesktopAppName("chrome"));
        Assert.True(OverlayModel.IsDesktopAppName("Firefox"));
        Assert.False(OverlayModel.IsDesktopAppName("eldenring"));
        Assert.False(OverlayModel.IsDesktopApp(NoSuchPid));
    }
}
