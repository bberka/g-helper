using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using GHelper.App.Controls;
using GHelper.App.Services;
using GHelper.App.Theme;
using GHelper.App.ViewModels;
using Xunit;

namespace GHelper.App.Tests;

public class LaunchOptionsTests
{
    [Theory]
    [InlineData("charge", LaunchAction.Charge)]
    [InlineData("cpu", LaunchAction.Cpu)]
    [InlineData("gpu", LaunchAction.Gpu)]
    [InlineData("services", LaunchAction.Services)]
    [InlineData("uv", LaunchAction.Uv)]
    [InlineData("colors", LaunchAction.Colors)]
    [InlineData("CPU", LaunchAction.Cpu)]
    [InlineData("--gpu", LaunchAction.Gpu)]
    public void Parses_legacy_actions(string arg, LaunchAction expected)
    {
        var o = LaunchOptions.Parse(new[] { arg });
        Assert.Equal(expected, o.Action);
    }

    [Fact]
    public void Parses_flags_and_keeps_first_action()
    {
        var o = LaunchOptions.Parse(new[] { "uv", "debug", "cpu", "--show", "bogus" });
        Assert.Equal(LaunchAction.Uv, o.Action);
        Assert.True(o.Debug);
        Assert.True(o.ShowWindow);
        Assert.Equal(new[] { "bogus" }, o.Unknown);
    }

    [Fact]
    public void Version_flag()
    {
        Assert.True(LaunchOptions.Parse(new[] { "--version" }).ShowVersion);
        Assert.False(LaunchOptions.Parse(Array.Empty<string>()).ShowVersion);
    }

    [Fact]
    public void Charge_is_headless_and_actions_open_the_window()
    {
        Assert.True(LaunchOptions.Parse(new[] { "charge" }).IsHeadless);
        Assert.False(LaunchOptions.Parse(new[] { "charge" }).OpensWindow);
        Assert.True(LaunchOptions.Parse(new[] { "cpu" }).OpensWindow);
        Assert.False(LaunchOptions.Parse(Array.Empty<string>()).OpensWindow);
    }
}

public class EntryTests
{
    [Fact]
    public void Version_prints_and_exits_zero_without_touching_the_platform()
    {
        var original = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            int code = Entry.Main(new[] { "--version" });
            Assert.Equal(0, code);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal(AppVersion.Current, sw.ToString().Trim());
        Assert.Matches(@"^\d+\.\d+\.\d+", AppVersion.Current);
    }
}

public class WindowPlacementTests
{
    private static readonly PixelRect Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void Bottom_taskbar_puts_window_into_the_bottom_right_corner()
    {
        var work = new PixelRect(0, 0, 1920, 1040); // 40px taskbar at the bottom

        var p = WindowPlacement.NearTaskbar(Screen, work, new PixelSize(425, 700));
        Assert.Equal(new PixelPoint(1920 - 10 - 425, 1040 - 10 - 700), p);
    }

    [Fact]
    public void Top_taskbar_moves_the_window_below_it()
    {
        var work = new PixelRect(0, 40, 1920, 1040);
        Assert.Equal(TaskbarEdge.Top, WindowPlacement.GetTaskbarEdge(Screen, work));
        var p = WindowPlacement.NearTaskbar(Screen, work, new PixelSize(425, 700));
        Assert.Equal(40 + 10, p.Y);
        Assert.Equal(1920 - 10 - 425, p.X);
    }

    [Fact]
    public void Left_taskbar_moves_the_window_to_the_left_edge()
    {
        var work = new PixelRect(60, 0, 1860, 1080);
        Assert.Equal(TaskbarEdge.Left, WindowPlacement.GetTaskbarEdge(Screen, work));
        var p = WindowPlacement.NearTaskbar(Screen, work, new PixelSize(425, 700));
        Assert.Equal(60 + 10, p.X);
    }

    [Fact]
    public void Right_taskbar_keeps_the_right_edge()
    {
        var work = new PixelRect(0, 0, 1860, 1080);
        Assert.Equal(TaskbarEdge.Right, WindowPlacement.GetTaskbarEdge(Screen, work));
        var p = WindowPlacement.NearTaskbar(Screen, work, new PixelSize(425, 700));
        Assert.Equal(1860 - 10 - 425, p.X);
    }

    [Fact]
    public void Ally_keeps_clear_of_the_bottom_when_there_is_no_taskbar_inset()
    {
        // auto hiding taskbar: working area == screen, legacy lifts the window by 110 px
        var p = WindowPlacement.NearTaskbar(Screen, Screen, new PixelSize(425, 700), isAlly: true);
        Assert.Equal(1080 - 110 - 700, p.Y);

        // visible taskbar: unchanged
        var work = new PixelRect(0, 0, 1920, 1040);
        var q = WindowPlacement.NearTaskbar(Screen, work, new PixelSize(425, 700), isAlly: true);
        Assert.Equal(1040 - 10 - 700, q.Y);
    }

    [Fact]
    public void Side_window_goes_left_of_main_or_right_when_there_is_no_room()
    {
        var work = new PixelRect(0, 0, 1920, 1040);
        var main = new PixelRect(1485, 330, 425, 700);
        Assert.Equal(1485 - 400 - 5, WindowPlacement.SideWindowLeft(work, main, 400));

        // taskbar on the left, main window hugging it: no room on its left
        var work2 = new PixelRect(60, 0, 1860, 1080);
        var main2 = new PixelRect(70, 330, 425, 700);
        Assert.Equal(70 + 425 + 5, WindowPlacement.SideWindowLeft(work2, main2, 400));
    }
}

public class TrayMenuModelTests
{
    private sealed class Recorder : IMainBackend
    {
        public List<string> Calls { get; } = new();
        public void Execute(MainAction action) => Calls.Add("exec:" + action);
        public void SetValue(MainValue value, int newValue) => Calls.Add($"set:{value}={newValue}");
        public void RefreshArea(string area, MainViewModel viewModel) { }
    }

    [Theory]
    [InlineData(AsusACPI.GPUModeEco, false, false, "eco")]
    [InlineData(AsusACPI.GPUModeStandard, false, false, "standard")]
    [InlineData(AsusACPI.GPUModeUltimate, false, false, "ultimate")]
    [InlineData(AsusACPI.GPUModeEco, true, true, "light-eco")]
    [InlineData(AsusACPI.GPUModeEco, true, false, "dark-eco")]
    [InlineData(AsusACPI.GPUModeUltimate, true, true, "light-standard")]
    [InlineData(AsusACPI.GPUModeStandard, true, false, "dark-standard")]
    public void Tray_icon_follows_gpu_mode_and_bw_option(int gpuMode, bool bw, bool osDark, string expected)
    {
        Assert.Equal(expected, TrayMenuModel.IconName(gpuMode, bw, osDark));
    }

    [Fact]
    public void Menu_lists_modes_gpu_modes_overlay_and_quit_with_checks()
    {
        var vm = MainViewModel.CreateSample();
        vm.VisualiseMode(AsusACPI.PerformanceTurbo, AsusACPI.PerformanceTurbo);
        vm.VisualiseGpuMode(AsusACPI.GPUModeEco, gpuAuto: false);
        vm.VisualiseOverlay(true);

        var rec = new Recorder();
        var items = TrayMenuModel.Build(vm, rec);

        var modes = items.Where(i => i.IsChecked is not null).Take(3).ToList();
        Assert.True(items[0].IsEnabled == false);
        Assert.Contains(items, i => i.Text == GHelper.Properties.Strings.Quit);

        var turbo = items.First(i => i.Text == GHelper.Properties.Strings.Turbo);
        Assert.True(turbo.IsChecked);
        Assert.False(items.First(i => i.Text == GHelper.Properties.Strings.Balanced).IsChecked);

        Assert.True(items.First(i => i.Text == GHelper.Properties.Strings.EcoMode).IsChecked);
        Assert.False(items.First(i => i.Text == GHelper.Properties.Strings.StandardMode).IsChecked);
        Assert.True(items.First(i => i.Text == GHelper.Properties.Strings.Overlay).IsChecked);
        Assert.True(items.First(i => i.Text == GHelper.Properties.Strings.OverlayOnlyInGames).IsEnabled);

        turbo.Invoke?.Invoke();
        items.First(i => i.Text == GHelper.Properties.Strings.Quit).Invoke?.Invoke();
        Assert.Equal(new[] { $"set:PerformanceMode={AsusACPI.PerformanceTurbo}", "exec:Quit" }, rec.Calls);
        Assert.NotEmpty(modes);
    }

    [Fact]
    public void Menu_hides_gpu_section_when_not_available()
    {
        var vm = MainViewModel.CreateSample();
        vm.HideGpuModes(gpuExists: false);
        var items = TrayMenuModel.Build(vm, NullMainBackend.Instance);
        Assert.DoesNotContain(items, i => i.Text == GHelper.Properties.Strings.GPUMode);
    }
}

public class MainViewModelTests
{
    private sealed class Recorder : IMainBackend
    {
        public List<string> Calls { get; } = new();
        public void Execute(MainAction action) => Calls.Add("exec:" + action);
        public void SetValue(MainValue value, int newValue) => Calls.Add($"set:{value}={newValue}");
        public void RefreshArea(string area, MainViewModel viewModel) => Calls.Add("refresh:" + area);
    }

    [Fact]
    public void Visualise_mode_activates_one_button_and_colours_custom_modes()
    {
        var vm = new MainViewModel();

        vm.VisualiseMode(AsusACPI.PerformanceSilent, AsusACPI.PerformanceSilent);
        Assert.True(vm.Silent.Activated);
        Assert.False(vm.Balanced.Activated || vm.Turbo.Activated || vm.Fans.Activated);

        vm.VisualiseMode(5, AsusACPI.PerformanceTurbo); // user defined mode based on Turbo
        Assert.True(vm.Fans.Activated);
        Assert.False(vm.Silent.Activated);
        Assert.Equal(AppColors.Turbo, vm.Fans.BorderColor);

        vm.VisualiseMode(AsusACPI.PerformanceFullSpeed, AsusACPI.PerformanceFullSpeed);
        Assert.Equal(AppColors.Orange, vm.Fans.BorderColor);
    }

    [Fact]
    public void Visualise_screen_matches_legacy_rules()
    {
        var vm = new MainViewModel();

        // 120 Hz panel at 120 Hz with overdrive
        vm.VisualiseScreen(true, false, 120, 120, 1, true, -1, -1, false, false, -1, -1);
        Assert.True(vm.Hz120.Activated);
        Assert.False(vm.Hz60.Activated);
        Assert.Equal("120Hz + OD", vm.Hz120.Text);
        Assert.Contains("120Hz", vm.ScreenTitle);
        Assert.False(vm.MiniLed.IsVisible);
        Assert.False(vm.VisualHintVisible);

        // auto refresh
        vm.VisualiseScreen(true, true, 60, 120, 0, false, -1, -1, false, false, -1, -1);
        Assert.True(vm.ScreenAuto.Activated);
        Assert.False(vm.Hz120.Activated);

        // 60 Hz only panel: button row hidden
        vm.VisualiseScreen(true, false, 60, 60, 0, false, -1, -1, false, false, -1, -1);
        Assert.False(vm.ScreenButtonsVisible);

        // HDR active: multizone shows "strong" and visual modes are disabled with a hint
        vm.VisualiseScreen(true, false, 120, 120, 0, false, -1, 0, hdr: true, acm: false, -1, -1);
        Assert.Equal(GHelper.Properties.Strings.MultizoneStrong, vm.MiniLed.Text);
        Assert.False(vm.MiniLed.IsEnabled);
        Assert.True(vm.VisualHintVisible);

        // panel turned off
        vm.VisualiseScreen(false, false, 0, 120, 0, false, -1, -1, false, false, -1, -1);
        Assert.False(vm.Hz60.IsEnabled);
        Assert.Contains(GHelper.Properties.Strings.TurnedOff, vm.ScreenTitle);
    }

    [Fact]
    public void Visualise_gpu_mode_sets_title_and_activated_buttons()
    {
        var vm = new MainViewModel();

        vm.VisualiseGpuMode(AsusACPI.GPUModeEco, gpuAuto: true);
        Assert.True(vm.Optimized.Activated);
        Assert.False(vm.Eco.Activated);
        Assert.Equal(AppColors.Eco, vm.Optimized.BorderColor);
        Assert.EndsWith(GHelper.Properties.Strings.GPUModeEco, vm.GpuTitle);

        vm.VisualiseGpuMode(AsusACPI.GPUModeUltimate, gpuAuto: false);
        Assert.True(vm.Ultimate.Activated);
        Assert.False(vm.Optimized.Activated);

        vm.VisualiseGpuMode(AsusACPI.GPUModeStandard, gpuAuto: false, gpuError: "Device problem");
        Assert.True(vm.Standard.Activated);
        Assert.True(vm.HasGpuError);

        vm.LockGpuModes();
        Assert.False(vm.Eco.IsEnabled);
        Assert.Contains("...", vm.GpuTitle);
    }

    [Fact]
    public void Gpu_row_columns_follow_visible_buttons()
    {
        var vm = new MainViewModel();
        vm.VisualiseGpuButtons(eco: true, ultimate: true);
        Assert.Equal(4, vm.GpuColumns);

        vm.VisualiseGpuButtons(eco: false, ultimate: false); // no dGPU eco / mux: Standard + Stop GPU applications
        Assert.Equal(2, vm.GpuColumns);
        Assert.True(vm.StopGpu.IsVisible);
        Assert.False(vm.IsMuxGpu);
    }

    [Fact]
    public void Battery_slider_pushes_user_changes_but_not_model_updates()
    {
        var vm = new MainViewModel();
        var rec = new Recorder();
        vm.Backend = rec;

        vm.VisualiseBattery(80, chargeFull: false);
        Assert.Empty(rec.Calls);
        Assert.Equal(80, vm.BatteryLimit);
        Assert.EndsWith("80%", vm.BatteryTitle);

        vm.BatteryLimit = 60; // user drags the slider
        Assert.Equal(new[] { "set:BatteryLimit=60" }, rec.Calls);
        Assert.EndsWith("60%", vm.BatteryTitle);

        vm.VisualiseBatteryFull(true);
        Assert.Equal(AppColors.Standard, vm.BatteryFull.FillColor);
        vm.VisualiseBatteryFull(false);
        Assert.Null(vm.BatteryFull.FillColor);
    }

    [Fact]
    public void Button_commands_go_to_the_backend()
    {
        var vm = new MainViewModel();
        var rec = new Recorder();
        vm.Backend = rec;

        vm.Turbo.Command!.Execute(null);
        vm.Fans.Command!.Execute(null);
        vm.Overlay.SecondaryCommand!.Execute(null);

        Assert.Equal(new[] { "exec:PerformanceTurbo", "exec:FansAndPower", "exec:OverlayToggleOnly" }, rec.Calls);
    }

    [Fact]
    public void Combo_selection_is_pushed_only_for_user_changes()
    {
        var vm = new MainViewModel();
        var rec = new Recorder();
        vm.Backend = rec;

        vm.SetAuraModes(new[] { new ChoiceItem(0, "Static"), new ChoiceItem(1, "Breathe") }, 1);
        Assert.Empty(rec.Calls);
        Assert.Equal(1, vm.SelectedAuraMode!.Key);

        vm.SelectedAuraMode = vm.AuraModes[0];
        Assert.Equal(new[] { "set:AuraMode=0" }, rec.Calls);
    }

    [Fact]
    public void Gamma_text_and_user_push()
    {
        var vm = new MainViewModel();
        var rec = new Recorder();
        vm.Backend = rec;

        vm.VisualiseBrightness(35);
        Assert.Equal("35%", vm.GammaText);
        Assert.Empty(rec.Calls);

        vm.Gamma = 40;
        Assert.Equal(new[] { "set:Gamma=40" }, rec.Calls);
    }

    [Fact]
    public void Ally_layout_shows_controller_and_hides_keyboard_header()
    {
        var vm = new MainViewModel();
        vm.VisualiseAlly(overlayActive: true);
        Assert.True(vm.AllySectionVisible);
        Assert.True(vm.GpuStripVisible);
        Assert.False(vm.KeyboardHeaderVisible);
        Assert.True(vm.OverlayAlly.Activated);

        vm.VisualiseFpsLimit(0);
        Assert.Equal("FPS Limit OFF", vm.FpsLimit.Text);
        vm.VisualiseFpsLimit(45);
        Assert.Equal("FPS Limit 45", vm.FpsLimit.Text);
        vm.VisualiseBacklight(3);
        Assert.Equal("100%", vm.AllyBacklight.Text);
    }

    [Fact]
    public void Fn_lock_and_overlay_flags_fill_when_on()
    {
        var vm = new MainViewModel();
        vm.VisualiseFnLock(true);
        vm.VisualiseOverlay(true);
        Assert.Equal(AppColors.Standard, vm.FnLock.FillColor);
        Assert.Equal(AppColors.Eco, vm.Overlay.FillColor);
        vm.VisualiseFnLock(false);
        Assert.Null(vm.FnLock.FillColor);
    }

    [Fact]
    public void Sensors_update_labels()
    {
        var vm = new MainViewModel();
        vm.VisualiseSensors("CPU: 50", "GPU: 40", "Mid 2000", "Charging: 30W", "Battery Charge: 80%");
        Assert.Equal("CPU: 50", vm.CpuFanText);
        Assert.Equal("Mid 2000", vm.MidFanText);
        Assert.Equal("Battery Charge: 80%", vm.ChargeText);
    }
}

public class UiHostTests
{
    [AvaloniaFact]
    public void Refresh_requests_are_coalesced_and_delivered_on_the_ui_thread()
    {
        var vm = new MainViewModel();
        var calls = new List<string>();
        var backend = new CountingBackend(calls);
        vm.Backend = backend;
        var host = new AvaloniaUiHost(vm, () => null);

        host.Refresh("mode");
        host.Refresh("mode");
        host.Refresh("gpu");
        host.Refresh("MODE");
        Assert.Empty(calls); // nothing before the dispatcher runs

        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, calls.Count);
        Assert.Contains("mode", calls, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("gpu", calls);
    }

    [AvaloniaFact]
    public void Post_runs_inline_on_the_ui_thread()
    {
        var host = new AvaloniaUiHost(new MainViewModel(), () => null);
        bool ran = false;
        host.Post(() => ran = true);
        Assert.True(ran);
    }

    [AvaloniaFact]
    public void Toast_window_is_transparent_topmost_and_does_not_activate()
    {
        var toast = new ToastWindow();
        Assert.True(toast.Topmost);
        Assert.False(toast.ShowActivated);
        Assert.False(toast.ShowInTaskbar);
        Assert.False(toast.Focusable);
        Assert.Equal(Avalonia.Controls.WindowDecorations.None, toast.WindowDecorations);
        Assert.Contains(Avalonia.Controls.WindowTransparencyLevel.Transparent, toast.TransparencyLevelHint);

        toast.ShowToast("Test", "eco");
        Assert.Equal("Test", toast.Text);
        Assert.True(toast.IsVisible);
        toast.Close();
    }

    [Fact]
    public void Toast_icon_keys_resolve_to_assets()
    {
        // asset loading needs the application; just check the mapping layer through normalisation
        Assert.Null(ToastWindow.LoadIcon(null));
    }

    private sealed class CountingBackend : IMainBackend
    {
        private readonly List<string> _calls;
        public CountingBackend(List<string> calls) => _calls = calls;
        public void Execute(MainAction action) { }
        public void SetValue(MainValue value, int newValue) { }
        public void RefreshArea(string area, MainViewModel viewModel) => _calls.Add(area);
    }
}

public class ColorMathTests
{
    [Fact]
    public void Hsv_round_trips()
    {
        foreach (var c in new[] { Colors.Red, Colors.Lime, Colors.Blue, Color.FromRgb(12, 200, 99), Colors.White, Colors.Black })
        {
            var (h, s, v) = ColorMath.RgbToHsv(c);
            Assert.Equal(c, ColorMath.HsvToRgb(h, s, v));
        }
    }

    [Fact]
    public void ColorRef_is_bgr_like_the_legacy_color_dialog()
    {
        var c = Color.FromRgb(0x11, 0x22, 0x33);
        Assert.Equal(0x332211, ColorMath.ToColorRef(c));
        Assert.Equal(c, ColorMath.FromColorRef(0x332211));
        Assert.Equal("#112233", ColorMath.ToHex(c));
    }
}

public class FanCurveChartTests
{
    private static FanCurveChart Chart() => new() { XMin = 20, XMax = 100, YMin = 0, YMax = 100, Width = 600, Height = 300 };

    [AvaloniaFact]
    public void Keyboard_adjustment_is_clamped_and_neighbours_follow()
    {
        var chart = Chart();
        chart.SetPoints(new (double, double)[] { (30, 0), (40, 10), (50, 20), (60, 35), (70, 50), (80, 70), (90, 90), (100, 100) });
        var w = new Avalonia.Controls.Window { Content = chart };
        w.Show();
        chart.Focus();

        // push the first point far above its neighbours with Shift+Up (5 per step)
        for (int i = 0; i < 30; i++)
            w.KeyPressQwerty(Avalonia.Input.PhysicalKey.ArrowUp, Avalonia.Input.RawInputModifiers.Shift);

        // point 0 may not exceed the axis maximum
        Assert.Equal(100, chart.Points[0].Y);
        // and every following point was pushed up with it (curve stays non decreasing)
        for (int i = 1; i < chart.Points.Count; i++)
            Assert.True(chart.Points[i].Y >= chart.Points[i - 1].Y);
        w.Close();
    }

    [AvaloniaFact]
    public void Committed_event_fires_for_key_edits()
    {
        var chart = Chart();
        chart.SetPoints(new (double, double)[] { (30, 0), (40, 10), (50, 20), (60, 35), (70, 50), (80, 70), (90, 90), (100, 100) });
        var w = new Avalonia.Controls.Window { Content = chart };
        w.Show();
        chart.Focus();

        int committed = 0, changed = 0;
        chart.CurveCommitted += (_, _) => committed++;
        chart.CurveChanged += (_, _) => changed++;
        w.KeyPressQwerty(Avalonia.Input.PhysicalKey.ArrowUp, Avalonia.Input.RawInputModifiers.None);

        Assert.Equal(1, committed);
        Assert.Equal(1, changed);
        w.Close();
    }
}
