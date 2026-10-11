using System.Drawing;
using Avalonia.Media.Imaging;
using GHelper.App.Backend;
using GHelper.App.ViewModels;
using GHelper.Battery;
using GHelper.Display;
using GHelper.Gpu;
using GHelper.Mode;
using GHelper.Overlay;
using GHelper.Platform;
using GHelper.Tests.Engine;
using Xunit;

namespace GHelper.App.Tests;

/// <summary>Records what <see cref="MainBackend"/> asks the toolkit for.</summary>
internal sealed class FakeShell : IMainShell
{
    public int MinRate;
    public readonly HashSet<string> Open = new();
    public readonly List<string> Closed = new();
    public (Color initial, bool random, Action<Color> changed)? Picker;

    public void SetMinRate(int minRate) => MinRate = minRate;
    public void ShowColorPicker(Color initial, bool allowRandom, Action<Color> changed) => Picker = (initial, allowRandom, changed);
    public bool IsSideWindowOpen(string name) => Open.Contains(name);
    public void CloseSideWindow(string name) { Closed.Add(name); Open.Remove(name); }
    public Bitmap? LoadIcon(string resourceKey) => null;
}

internal sealed class FakeOverlay : IHardwareOverlay
{
    public int Starts, Stops;
    public void StartOverlay() => Starts++;
    public void StopOverlay() => Stops++;
    public void SetDragKey(bool down) { }
    public void SuspendForDisplayOff() { }
    public void ResumeForDisplayOn() { }
}

/// <summary>
/// MainBackend against the fake platform of the engine tests (linked from GHelper.Tests): every button / slider handler,
/// the Visualise* ports and the sensor refresh, without a window and without hardware.
/// </summary>
public class MainBackendTests : EngineTestBase, IDisposable
{
    private readonly MainViewModel _vm = new();
    private readonly FakeShell _shell = new();
    private readonly MainBackend _backend;

    private static readonly string[] Keys =
    {
        "charge_limit", "charge_full", "gpu_mode", "gpu_auto", "screen_auto", "aura_mode", "aura_color", "aura_color2",
        "rear_mode", "matrix_brightness", "matrix_running", "overlay", "overlay_game_only", "bw_icon", "fn_lock",
        "visual", "color_temp", "gamut", "brightness", "donate_click", "donate_dismissed", "start_count", "performance_mode",
        "charge_watt", "fan_rpm", "performance_0", "performance_1", "overdrive", "frequency", "max_frequency",
    };

    public MainBackendTests(EngineFixture fixture) : base(fixture)
    {
        // the base class put the fake platform back; the process wide Core singletons are reset on top of it
        AppTestState.Reset();
        CleanConfig(Keys);
        BatteryControl.chargeFull = false;

        _backend = new MainBackend(_vm, _shell);
        _backend.RunInline();
        _vm.Backend = _backend;
        Program.hardwareOverlay = null;
    }

    public void Dispose()
    {
        _backend.Dispose();
        Program.hardwareOverlay = null;
        CleanConfig(Keys);
    }

    // ---- performance --------------------------------------------------------------------------------------

    [Fact]
    public void PerformanceButtonsSetTheMode()
    {
        Program.modeControl = new ModeControl();

        _vm.Turbo.Command!.Execute(null);
        Program.modeControl.WaitForApply();

        Assert.Equal(AsusACPI.PerformanceTurbo, Modes.GetCurrent());
        Assert.Contains("mode", Ui.Refreshes);
        Assert.Contains(AsusACPI.PerformanceTurbo, Asus.SetValues(AsusACPI.PerformanceMode));
    }

    [Fact]
    public void ModeRefreshShowsCurrentModeAndTrayEntries()
    {
        Modes.SetCurrent(AsusACPI.PerformanceSilent);

        _backend.RefreshArea("mode", _vm);
        _backend.RefreshArea("contextmenu", _vm);

        Assert.True(_vm.Silent.Activated);
        Assert.False(_vm.Turbo.Activated);
        Assert.Equal(AsusACPI.PerformanceSilent, _vm.CurrentMode);
        Assert.True(_vm.TrayModes.Count >= 3);
        Assert.Contains(_vm.TrayModes, m => m.Key == AsusACPI.PerformanceTurbo);
    }

    [Fact]
    public void TrayModePickSetsPerformanceMode()
    {
        Program.modeControl = new ModeControl();

        _backend.SetValue(MainValue.PerformanceMode, AsusACPI.PerformanceSilent);
        Program.modeControl.WaitForApply();

        Assert.Equal(AsusACPI.PerformanceSilent, Modes.GetCurrent());
    }

    [Fact]
    public void ModeLabelRefresh()
    {
        Program.modeControl = new ModeControl();
        Modes.SetCurrent(AsusACPI.PerformanceBalanced);
        Program.modeControl.SetModeLabel();

        _backend.RefreshArea("modelabel", _vm);

        Assert.Equal(ModeControl.ModeLabel, _vm.PerformanceTitle);
        Assert.Contains(Properties.Strings.Balanced, _vm.PerformanceTitle);
    }

    // ---- battery ------------------------------------------------------------------------------------------

    [Fact]
    public void BatterySliderIsDebouncedToTheLastValue()
    {
        _backend.SetValue(MainValue.BatteryLimit, 60);
        _backend.SetValue(MainValue.BatteryLimit, 65);
        _backend.SetValue(MainValue.BatteryLimit, 70);

        Assert.Empty(Asus.SetValues(AsusACPI.BatteryLimit)); // nothing written while the slider moves

        Assert.True(WaitFor(() => Asus.SetValues(AsusACPI.BatteryLimit).Length > 0, 3000));
        Assert.Equal(new[] { 70 }, Asus.SetValues(AsusACPI.BatteryLimit));
        Assert.Equal(70, AppConfig.Get("charge_limit"));
    }

    [Fact]
    public void BatteryRefreshReadsLimitAndFullFlag()
    {
        AppConfig.Set("charge_limit", 80);
        BatteryControl.chargeFull = true;

        _backend.RefreshArea("battery", _vm);

        Assert.Equal(80, _vm.BatteryLimit);
        Assert.Contains("80%", _vm.BatteryTitle);
        Assert.NotNull(_vm.BatteryFull.FillColor);
    }

    [Fact]
    public void BatteryFullButtonTogglesFullCharge()
    {
        AppConfig.Set("charge_limit", 60);

        _vm.BatteryFull.Command!.Execute(null);

        Assert.True(BatteryControl.chargeFull);
        Assert.Contains(100, Asus.SetValues(AsusACPI.BatteryLimit));
    }

    [Fact]
    public void BatteryRefreshDoesNotMoveASliderThatIsBeingDragged()
    {
        AppConfig.Set("charge_limit", 80);
        _backend.RefreshArea("battery", _vm);

        // user drags to 70 (pending write), a refresh from an older write arrives
        _vm.BatteryLimit = 70;
        AppConfig.Set("charge_limit", 75);
        _backend.RefreshArea("battery", _vm);

        Assert.Equal(70, _vm.BatteryLimit);
    }

    // ---- screen -------------------------------------------------------------------------------------------

    [Fact]
    public void ScreenRefreshFollowsScreenControlState()
    {
        ScreenControl.InitScreen();
        _backend.RefreshArea("screen", _vm);

        Assert.StartsWith(Properties.Strings.LaptopScreen, _vm.ScreenTitle);
        Assert.Contains("Hz", _vm.ScreenTitle);
        Assert.True(_vm.Hz120.Text.Length > 0);
        Assert.Equal(ScreenControl.MIN_RATE, _shell.MinRate);
    }

    [Fact]
    public void ScreenButtonsSwitchRefreshRate()
    {
        _vm.Hz60.Command!.Execute(null);
        Assert.Equal(0, AppConfig.Get("screen_auto"));

        _vm.ScreenAuto.Command!.Execute(null);
        Assert.Equal(1, AppConfig.Get("screen_auto"));
    }

    // ---- GPU ----------------------------------------------------------------------------------------------

    [Fact]
    public void GpuRefreshShowsStandardMode()
    {
        Asus.Values[AsusACPI.GPUEco] = 0;
        Asus.Values[AsusACPI.GPUMux] = 1;
        Program.gpuControl = new GPUModeControl();

        Program.gpuControl.InitGPUMode();
        _backend.RefreshArea("gpu", _vm);

        Assert.True(_vm.Standard.Activated);
        Assert.False(_vm.Eco.Activated);
        Assert.Contains(Properties.Strings.GPUModeStandard, _vm.GpuTitle);
        Assert.True(_vm.IsGpuSection);
    }

    [Fact]
    public void GpuRefreshHidesUnsupportedModes()
    {
        Asus.Values[AsusACPI.GPUEco] = -1;
        Asus.Values[AsusACPI.GPUMux] = -1;
        Program.gpuControl = new GPUModeControl();

        Program.gpuControl.InitGPUMode();
        _backend.RefreshArea("gpu", _vm);

        Assert.False(_vm.IsGpuSection);
        Assert.False(_vm.Eco.IsVisible);
        Assert.False(_vm.Ultimate.IsVisible);
    }

    [Fact]
    public void OptimizedButtonTogglesAutoGpu()
    {
        Asus.Values[AsusACPI.GPUEco] = 0;
        Asus.Values[AsusACPI.GPUMux] = 1;
        Program.gpuControl = new GPUModeControl();
        AppConfig.Set("gpu_auto", 0);

        _vm.Optimized.Command!.Execute(null);

        Assert.Equal(1, AppConfig.Get("gpu_auto"));
        Assert.Contains("gpu", Ui.Refreshes);
    }

    // ---- keyboard / aura ----------------------------------------------------------------------------------

    [Fact]
    public void KeyboardRefreshFillsAuraModes()
    {
        AppConfig.Set("aura_mode", 0);
        _backend.RefreshArea("keyboard", _vm);

        Assert.NotEmpty(_vm.AuraModes);
        Assert.NotNull(_vm.SelectedAuraMode);
        Assert.StartsWith(Properties.Strings.LaptopKeyboard, _vm.KeyboardTitle);
    }

    [Fact]
    public void AuraModeComboWritesConfig()
    {
        _backend.RefreshArea("keyboard", _vm);
        var second = _vm.AuraModes[1];

        _vm.SelectedAuraMode = second;

        Assert.Equal(second.Key, AppConfig.Get("aura_mode"));
    }

    [Fact]
    public void CycleAuraModeWrapsAndToasts()
    {
        _backend.RefreshArea("keyboard", _vm);
        int count = _vm.AuraModes.Count;
        _vm.SelectedAuraMode = _vm.AuraModes[count - 1];
        Ui.Toasts.Clear();

        _backend.RunAction(GHelper.Input.UiActions.CycleAuraMode, 1);

        Assert.Equal(_vm.AuraModes[0], _vm.SelectedAuraMode);
        Assert.Single(Ui.Toasts);
        Assert.Equal(_vm.AuraModes[0].Text, Ui.Toasts[0].text);
    }

    [Fact]
    public void KeyboardColorPickerStoresArgb()
    {
        _vm.KeyboardColor.Command!.Execute(null);
        Assert.NotNull(_shell.Picker);

        _shell.Picker!.Value.changed(Color.FromArgb(255, 10, 20, 30));

        Assert.Equal(Color.FromArgb(255, 10, 20, 30).ToArgb(), AppConfig.Get("aura_color"));
    }

    [Fact]
    public void FnLockButtonToggles()
    {
        AppConfig.Set("fn_lock", 0);

        _vm.FnLock.Command!.Execute(null);

        Assert.Equal(1, AppConfig.Get("fn_lock"));
        Assert.Contains("fnlock", Ui.Refreshes);

        _backend.RefreshArea("fnlock", _vm);
        Assert.NotNull(_vm.FnLock.FillColor);
    }

    // ---- overlay ------------------------------------------------------------------------------------------

    [Fact]
    public void OverlayToggleStartsAndStopsTheOverlay()
    {
        var overlay = new FakeOverlay();
        Program.hardwareOverlay = overlay;

        _backend.Execute(MainAction.OverlayToggleOnly);
        Assert.Equal(1, AppConfig.Get("overlay"));
        Assert.Equal(1, overlay.Starts);
        Assert.True(_vm.OverlayEnabled);

        _backend.Execute(MainAction.OverlayToggleOnly);
        Assert.Equal(0, AppConfig.Get("overlay"));
        Assert.Equal(1, overlay.Stops);
        Assert.False(_vm.OverlayEnabled);
    }

    [Fact]
    public void OverlayButtonOpensTheConfigWindowAndClosesIt()
    {
        _vm.Overlay.Command!.Execute(null);
        Assert.Contains(("overlay", 0), Ui.Windows);

        _shell.Open.Add("overlay");
        _vm.Overlay.Command!.Execute(null);
        Assert.Equal(new[] { "overlay" }, _shell.Closed);
    }

    [Fact]
    public void OverlayGameOnlyRestartsARunningOverlay()
    {
        var overlay = new FakeOverlay();
        Program.hardwareOverlay = overlay;
        AppConfig.Set("overlay", 1);

        _backend.SetValue(MainValue.OverlayGameOnly, 1);

        Assert.Equal(1, AppConfig.Get("overlay_game_only"));
        Assert.Equal(1, overlay.Stops);
        Assert.Equal(1, overlay.Starts);
    }

    // ---- side windows / footer ----------------------------------------------------------------------------

    [Theory]
    [InlineData(MainAction.FansAndPower, "fans")]
    [InlineData(MainAction.KeyboardExtra, "extra")]
    [InlineData(MainAction.Updates, "updates")]
    [InlineData(MainAction.AllyController, "handheld")]
    public void ButtonsOpenSideWindowsByRegistryName(MainAction action, string name)
    {
        _backend.Execute(action);
        Assert.Contains((name, 0), Ui.Windows);
    }

    [Fact]
    public void QuitAsksTheUiToExit()
    {
        _vm.Quit.Command!.Execute(null);
        Assert.Equal(1, Ui.Quits);
    }

    [Fact]
    public void DonateClickRecordsAndOpensTheSupportPage()
    {
        AppConfig.Set("start_count", 100);

        _vm.Donate.Command!.Execute(null);

        Assert.Equal(100, AppConfig.Get("donate_click"));
        Assert.Equal(Properties.Strings.ThankYou, _vm.Donate.Text);
        Assert.Contains("OpenUrl:https://g-helper.com/support", P.ProcessService.Calls);
    }

    [Fact]
    public void DonateDismissIsPersisted()
    {
        _backend.Execute(MainAction.DonateNotInterested);

        Assert.True(AppConfig.Is("donate_dismissed"));
        Assert.Equal(0, _vm.Donate.Badge);
    }

    [Theory]
    [InlineData(10, 0, 0)]
    [InlineData(20, 0, 1)]
    [InlineData(70, 20, 1)]
    [InlineData(120, 20, 2)]
    [InlineData(1000, 20, 5)]
    public void DonateBadgeGrowsWithStartCount(int start, int click, int expected) =>
        Assert.Equal(expected, MainBackend.GetDonateBadge(start, click));

    [Fact]
    public void RunOnStartupCheckBoxSchedulesTheTask()
    {
        _vm.RunOnStartup = true;
        _vm.RunOnStartup = false;

        Assert.Equal(new[] { "Schedule", "UnSchedule" }, P.StartupService.Calls.ToArray());
    }

    [Fact]
    public void VersionLabelFollowsTheUpdateControl()
    {
        _backend.Initialize();
        _backend.RefreshArea("version", _vm);

        Assert.StartsWith(Properties.Strings.VersionLabel, _vm.VersionText);
    }

    // ---- matrix -------------------------------------------------------------------------------------------

    [Fact]
    public void MatrixSectionHiddenWithoutDevice()
    {
        _backend.RefreshArea("matrix", _vm);
        Assert.False(_vm.MatrixSectionVisible);
    }

    [Fact]
    public void MatrixBrightnessAndRunningWriteConfig()
    {
        _backend.SetValue(MainValue.MatrixBrightness, 2);
        _backend.SetValue(MainValue.MatrixRunning, 1);

        Assert.Equal(2, AppConfig.Get("matrix_brightness"));
        Assert.Equal(1, AppConfig.Get("matrix_running"));
    }

    [Fact]
    public void ChoosingMatrixTextOpensTheMatrixWindow()
    {
        _backend.SetValue(MainValue.MatrixRunning, (int)GHelper.AnimeMatrix.MatrixMode.Text);
        Assert.Contains(("matrix", 0), Ui.Windows);
    }

    // ---- sensors / tray -----------------------------------------------------------------------------------

    [Fact]
    public void SensorRefreshPublishesTextAndTrayTip()
    {
        P.SensorService.CpuTemp = 55;

        _backend.OnWindowVisibilityChanged(true);
        _backend.RefreshSensors(true);

        Assert.StartsWith("CPU", _vm.CpuFanText);
        Assert.Contains("55", _vm.CpuFanText);
        Assert.Contains("55", _vm.TrayToolTip);
        Assert.StartsWith("GPU", _vm.GpuFanText);
    }

    [Fact]
    public void BatteryHoverKeepsItsTextAcrossSensorRefreshes()
    {
        _backend.Execute(MainAction.BatteryFullHoverEnter);
        Assert.Equal(Properties.Strings.BatteryLimitFull, _vm.ChargeText);

        _backend.RefreshSensors(true);
        Assert.Equal(Properties.Strings.BatteryLimitFull, _vm.ChargeText);

        _backend.Execute(MainAction.BatteryFullHoverLeave);
        Assert.NotEqual(Properties.Strings.BatteryLimitFull, _vm.ChargeText);
    }

    [Fact]
    public void ClickOnFanTextTogglesRpmAndPercent()
    {
        bool before = GHelper.Fan.FanSensorControl.fanRpm;

        _backend.Execute(MainAction.ToggleFanUnits);

        Assert.NotEqual(before, GHelper.Fan.FanSensorControl.fanRpm);
        GHelper.Fan.FanSensorControl.fanRpm = before;
    }

    [Fact]
    public void BwTrayIconIsStored()
    {
        _backend.SetValue(MainValue.BwTrayIcon, 1);

        Assert.True(_vm.BwTrayIcon);
        Assert.Equal(1, AppConfig.Get("bw_icon"));
    }

    [Fact]
    public void PeripheralsHiddenWithoutDevices()
    {
        _backend.RefreshArea("peripherals", _vm);
        Assert.False(_vm.PeripheralsVisible);
    }

    [Fact]
    public void UnknownAreasAreIgnored()
    {
        _backend.RefreshArea("something-new", _vm);
        _backend.RefreshArea("fans", _vm);
    }

    // ---- helpers ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 4, 1, 1)]
    [InlineData(3, 4, 1, 0)]
    [InlineData(0, 4, -1, 3)]
    [InlineData(2, 4, -1, 1)]
    [InlineData(-1, 4, 1, 1)]
    public void CycleWraps(int current, int count, int delta, int expected) =>
        Assert.Equal(expected, MainBackend.Cycle(current, count, delta));

    [Theory]
    [InlineData(LaunchAction.None, new string[0])]
    [InlineData(LaunchAction.Cpu, new[] { "cpu" })]
    [InlineData(LaunchAction.Gpu, new[] { "gpu" })]
    [InlineData(LaunchAction.Services, new[] { "services" })]
    [InlineData(LaunchAction.Uv, new[] { "uv" })]
    [InlineData(LaunchAction.Colors, new[] { "colors" })]
    public void LaunchActionsMapToCoreStartArguments(LaunchAction action, string[] expected)
    {
        var options = new LaunchOptions { Action = action };
        Assert.Equal(expected, GHelper.App.App.StartArguments(options));
    }
}
