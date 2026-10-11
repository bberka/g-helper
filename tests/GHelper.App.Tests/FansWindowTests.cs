using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using GHelper.App.Views.Fans;
using Xunit;

namespace GHelper.App.Tests;

/// <summary>Fans and power window: rendered layouts (PNG artifacts) and the legacy Fans.cs rules on a fake backend.</summary>
public class FansWindowTests
{
    private static async Task<FansViewModel> Loaded(SampleFansBackend backend)
    {
        var vm = new FansViewModel(backend);
        await vm.InitGpuAsync();
        return vm;
    }

    private static async Task Settle()
    {
        await Task.Delay(450);
        Dispatcher.UIThread.RunJobs();
    }

    // ------------------------------------------------------------------ rendering

    [AvaloniaFact]
    public async Task Fans_window_renders_cpu_tab_dark()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new FansWindow(0, SampleFansBackend.Create());
        window.Show();
        await Settle();
        var frame = MainWindowSnapshotTests.Render(window, "fans-cpu-dark");

        Assert.Equal(675, window.Width);
        Assert.InRange(frame.PixelSize.Height, 480, 800);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Fans_window_renders_three_charts_light()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var window = new FansWindow(0, SampleFansBackend.Create(midFan: true));
        window.Show();
        await Settle();
        var frame = MainWindowSnapshotTests.Render(window, "fans-cpu-light-mid");

        // 3 charts need more room than the 2 chart layout
        Assert.True(frame.PixelSize.Height > 600, "height " + frame.PixelSize.Height);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Fans_window_renders_gpu_and_advanced_tabs()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        var gpu = new FansWindow(1, SampleFansBackend.Create(xgm: true));
        gpu.Show();
        await Settle();
        MainWindowSnapshotTests.Render(gpu, "fans-gpu-dark-xgm");
        Assert.True(gpu.ViewModel.IsGpuTab);
        gpu.Close();

        var advanced = new FansWindow(2, SampleFansBackend.Create());
        advanced.Show();
        await Settle();
        MainWindowSnapshotTests.Render(advanced, "fans-advanced-dark");
        Assert.True(advanced.ViewModel.IsAdvancedTab);
        advanced.Close();
    }

    [AvaloniaFact]
    public async Task Fans_window_without_pawnio_offers_the_download()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var backend = SampleFansBackend.Create();
        backend.PawnAvailable = false;
        backend.PawnInstalled = false;
        var window = new FansWindow(2, backend);
        window.Show();
        await Settle();
        MainWindowSnapshotTests.Render(window, "fans-advanced-nopawn");
        Assert.True(window.ViewModel.DownloadVisible);
        Assert.False(window.ViewModel.PawnVisible);
        window.Close();
    }

    // ------------------------------------------------------------------ rules of Fans.cs

    [AvaloniaFact]
    public async Task Power_sliders_keep_spl_sppt_fppt_ordered_and_save_to_the_mode()
    {
        var b = SampleFansBackend.Create();
        var vm = await Loaded(b);
        Assert.True(vm.TotalVisible);
        Assert.True(vm.FastVisible);

        // raising SPL pulls sPPT / fPPT up (TrackTotal_Scroll)
        vm.Total = 120;
        Assert.Equal(120, vm.Slow);
        Assert.Equal(120, vm.Fast);
        Assert.Equal(120, b.ModeInts["limit_total"]);
        Assert.Equal(120, b.ModeInts["limit_slow"]);
        Assert.Equal(120, b.ModeInts["limit_fast"]);

        // lowering fPPT pulls both below it down
        vm.Fast = 50;
        Assert.Equal(50, vm.Slow);
        Assert.Equal(50, vm.Total);
        Assert.Equal(50, b.ModeInts["limit_total"]);
    }

    [AvaloniaFact]
    public async Task Total_limit_below_cpu_limit_lowers_the_cpu_limit()
    {
        var b = SampleFansBackend.Create();
        b.AllAmd = true;
        var vm = await Loaded(b);

        Assert.True(vm.CpuLimitVisible);
        Assert.False(vm.SlowVisible);
        Assert.False(vm.FastVisible);
        Assert.Equal("Platform (CPU + GPU)", vm.TotalTitle);

        vm.CpuLimit = 90;
        Assert.True(vm.Total >= 90); // TrackCPU_Scroll: total follows the CPU limit
        vm.Total = 40;
        Assert.Equal(40, vm.CpuLimit);
    }

    [AvaloniaFact]
    public async Task Loaded_power_values_are_clamped_to_the_acpi_range()
    {
        var b = SampleFansBackend.Create();
        b.ModeInts["limit_total"] = 9999;
        b.ModeInts["limit_slow"] = 1;
        var vm = await Loaded(b);

        Assert.Equal(AsusACPI.MaxTotal, vm.Total);
        Assert.Equal(AsusACPI.MinTotal, vm.Slow);
    }

    [AvaloniaFact]
    public async Task Sliders_do_not_save_while_loading_but_power_is_stored_once()
    {
        var b = SampleFansBackend.Create();
        var vm = await Loaded(b);
        b.Calls.Clear();

        vm.OnAreaRefreshed("fans");

        // InitPower -> SavePower writes the four limits once; no scroll side effects (no AutoPower)
        Assert.DoesNotContain("AutoPower", b.Calls);
        Assert.Equal(1, b.Calls.Count(c => c.StartsWith("SetMode(limit_total,")));
    }

    [AvaloniaFact]
    public async Task Power_slider_release_asks_core_to_apply()
    {
        var b = SampleFansBackend.Create();
        var vm = await Loaded(b);
        vm.CommitPower();

        // legacy ran AutoPower on a worker thread
        for (int i = 0; i < 50 && !b.Calls.Contains("AutoPower"); i++) await Task.Delay(20);
        Assert.Contains("AutoPower", b.Calls);
    }

    [AvaloniaFact]
    public async Task Hysteresis_is_hidden_when_unsupported_and_defaults_come_from_the_bios()
    {
        var b = SampleFansBackend.Create();
        b.Hysteresis = (-1, -1);
        var vm = await Loaded(b);
        Assert.False(vm.HysteresisVisible);

        b.Hysteresis = (2, 0);
        vm.OnAreaRefreshed("fans");
        Assert.True(vm.HysteresisVisible);
        Assert.Equal(2, vm.HysteresisUp);
        Assert.Equal(3, vm.HysteresisDown); // BIOS reports 0: legacy falls back to 3 (Medium)

        vm.HysteresisUp = 5;
        Assert.Equal(5, b.ModeInts["hysteresis_up"]);
        vm.CommitHysteresis();
        Assert.Contains("SetFanHysteresis(5,3)", b.Calls);
    }

    [AvaloniaFact]
    public async Task Fan_curves_load_from_the_bios_default_and_are_stored()
    {
        var b = SampleFansBackend.Create(midFan: true);
        var vm = await Loaded(b);

        Assert.True(vm.Cpu.IsVisible);
        Assert.True(vm.Mid.IsVisible);
        Assert.False(vm.Xgm.IsVisible);
        Assert.Equal(3, vm.ChartCount);
        Assert.Equal(1, b.Config["mid_fan"]);
        Assert.Equal(0, b.Config["xgm_fan"]);

        Assert.Equal(16, b.StoredCurves[AsusFan.CPU].Length);
        Assert.Equal(58d, vm.Cpu.Points[0].x); // 0x3A
        Assert.Equal(8d, vm.Cpu.Points[0].y);
    }

    [AvaloniaFact]
    public async Task Committing_a_chart_stores_the_curve_and_applies_fans()
    {
        var b = SampleFansBackend.Create();
        var vm = await Loaded(b);
        b.Calls.Clear();

        var points = Enumerable.Range(0, 8).Select(i => (x: 30.0 + i * 10, y: 10.0 + i * 12.6)).ToArray();
        vm.CommitCurve(AsusFan.GPU, points);

        var stored = b.StoredCurves[AsusFan.GPU];
        Assert.Equal(30, stored[0]);
        Assert.Equal(100, stored[7]);
        Assert.Equal(98, stored[15]); // (byte)98.2 truncates like the legacy SaveProfile
        Assert.Contains("AutoFans", b.Calls);
    }

    [AvaloniaFact]
    public async Task Chart_labels_follow_the_rpm_toggle_and_units()
    {
        var b = SampleFansBackend.Create();
        var vm = await Loaded(b);

        Assert.Equal("OFF", vm.FormatSpeed(0, AsusFan.CPU));
        // fanMin 18 / fanMax 58: 200 * floor((1800 + 40 * 50) / 200) = 3800
        Assert.Equal("3800", vm.FormatSpeed(50, AsusFan.CPU));
        Assert.Equal("3800 RPM", vm.FormatSpeed(50, AsusFan.CPU, " RPM"));

        vm.ToggleFanUnits();
        Assert.Equal("50%", vm.FormatSpeed(50, AsusFan.CPU));
        vm.ToggleFanUnits();

        Assert.Equal("50", vm.AxisXLabel(50));
        b.IsFahrenheit = true;
        Assert.Equal("122", vm.AxisXLabel(50));
    }

    [AvaloniaFact]
    public async Task Auto_apply_checkboxes_store_the_flag_and_reapply_the_mode()
    {
        var b = SampleFansBackend.Create();
        var vm = await Loaded(b);
        b.Calls.Clear();

        vm.ApplyFans = false;
        Assert.Equal(0, b.ModeInts["auto_apply"]);
        Assert.Contains("SetPerformanceMode(-1)", b.Calls);

        vm.ApplyPower = false;
        Assert.Equal(0, b.ModeInts["auto_apply_power"]);
    }

    [AvaloniaFact]
    public async Task Undervolt_slider_stores_values_and_clears_auto_apply()
    {
        var b = SampleFansBackend.Create();
        b.ModeInts["auto_uv"] = 1;
        var vm = await Loaded(b);
        Assert.True(vm.ApplyUV);
        Assert.True(vm.ApplyUVEnabled);

        vm.Uv = -25;
        Assert.Equal(-25, b.ModeInts["cpu_uv"]);
        Assert.Equal(0, b.ModeInts["auto_uv"]);
        Assert.False(vm.ApplyUV);
        Assert.False(vm.ApplyUVEnabled);

        vm.ApplyAdvancedCommand.Execute(null);
        Assert.Contains("ApplyRyzen", b.Calls);
        Assert.True(vm.ApplyUVEnabled);
    }

    [AvaloniaFact]
    public async Task Temperature_label_shows_default_at_the_maximum()
    {
        var b = SampleFansBackend.Create();
        var vm = await Loaded(b);
        Assert.Equal("Default", vm.TempText); // 96 = CpuInfo default

        vm.Temp = 85;
        Assert.Equal("85°C", vm.TempText);
        Assert.Equal(85, b.ModeInts["cpu_temp"]);
    }

    [AvaloniaFact]
    public async Task Advanced_tab_is_amd_only_and_uv_rows_follow_cpu_support()
    {
        var b = SampleFansBackend.Create();
        b.Amd = false;
        var vm = await Loaded(b);
        Assert.False(vm.AdvancedButtonVisible);

        var amd = SampleFansBackend.Create();
        amd.UvSupported = false;
        amd.IGpuUvSupported = true;
        var vm2 = await Loaded(amd);
        Assert.True(vm2.AdvancedButtonVisible);
        Assert.False(vm2.UvVisible);
        Assert.True(vm2.IGpuUvVisible);
    }

    [AvaloniaFact]
    public async Task Gpu_tab_is_hidden_without_an_nvidia_gpu_and_values_are_rounded_to_5()
    {
        var b = SampleFansBackend.Create();
        b.Gpu = null;
        var vm = await Loaded(b);
        Assert.False(vm.GpuButtonVisible);

        var b2 = SampleFansBackend.Create();
        var vm2 = await Loaded(b2);
        Assert.True(vm2.GpuButtonVisible);
        Assert.Equal("NVIDIA GeForce RTX 4060 Laptop GPU", vm2.GpuTitle);
        Assert.Equal("Default", vm2.GpuClockLimitText);

        vm2.GpuCore = 103;
        Assert.Equal(105, vm2.GpuCore);
        Assert.Equal(105, b2.ModeInts["gpu_core"]);
        vm2.GpuClockLimit = 1998;
        Assert.Equal(2000, vm2.GpuClockLimit);
        Assert.Equal("2000 MHz", vm2.GpuClockLimitText);
    }

    [AvaloniaFact]
    public async Task Gpu_power_slider_appears_with_a_tgp_base_and_shows_base_plus_offset()
    {
        var b = SampleFansBackend.Create();
        b.GpuBase = 80;
        b.GpuPowerVar = 15;
        var vm = await Loaded(b);

        Assert.True(vm.GpuPowerVisible);
        Assert.Equal(15, vm.GpuPower);
        Assert.Equal("95W", vm.GpuPowerText);

        vm.GpuBoost = 20;
        Assert.Equal(20, b.ModeInts["gpu_boost"]);
        Assert.Equal(15, b.ModeInts["gpu_power"]);
    }

    [AvaloniaFact]
    public async Task Factory_defaults_resets_curves_flags_and_gpu_settings()
    {
        var b = SampleFansBackend.Create(midFan: true);
        b.Config["mid_fan"] = 1;
        var vm = await Loaded(b);
        vm.Uv = -30;
        vm.GpuCore = 100;
        b.Calls.Clear();

        vm.ResetCommand.Execute(null);

        Assert.False(vm.ApplyFans);
        Assert.False(vm.ApplyPower);
        Assert.Equal(0, vm.Uv);
        Assert.Equal(0, vm.GpuCore);
        Assert.Contains("ResetPerformanceMode", b.Calls);
        Assert.Contains("XgmReset", b.Calls);
        Assert.Contains("RemoveMode(gpu_core)", b.Calls);
        Assert.Contains("SetGpuClocks(True)", b.Calls);
        Assert.Contains("SetGpuPower", b.Calls);
        Assert.Equal(0, b.ModeInts["auto_apply"]);
        Assert.False(b.ModeInts.ContainsKey("cpu_temp"));
        Assert.All(vm.Curves.Where(c => c.IsVisible), c => Assert.False(c.IsActive));
    }

    [AvaloniaFact]
    public async Task Custom_modes_can_be_added_renamed_and_removed()
    {
        var b = SampleFansBackend.Create();
        var vm = await Loaded(b);
        Assert.False(vm.RenameVisible);
        Assert.False(vm.RemoveVisible);

        vm.AddModeCommand.Execute(null);
        Assert.Contains("AddCustomMode()=3", b.Calls);
        Assert.Contains("SetPerformanceMode(3)", b.Calls);
        Assert.Equal(4, vm.Modes.Count);

        vm.OnAreaRefreshed("fans"); // Core refresh after the mode switch
        Assert.True(vm.RenameVisible);
        Assert.Equal(3, vm.SelectedMode!.Key);

        vm.RenameModeCommand.Execute(null);
        Assert.True(vm.IsRenaming);
        vm.ModeName = "Quiet work";
        Assert.Contains("SetCurrentModeName(Quiet work)", b.Calls);
        vm.RenameModeCommand.Execute(null);
        Assert.False(vm.IsRenaming);
        Assert.Equal("Quiet work", vm.Modes.First(m => m.Key == 3).Text);

        vm.RemoveModeCommand.Execute(null);
        Assert.Contains("RemoveCustomMode(3)", b.Calls);
        Assert.Contains("SetPerformanceMode(0)", b.Calls); // back to Balanced
        Assert.Equal(3, vm.Modes.Count);
    }

    [AvaloniaFact]
    public async Task Picking_another_mode_switches_the_performance_mode_once()
    {
        var b = SampleFansBackend.Create();
        var vm = await Loaded(b);
        b.Calls.Clear();

        vm.SelectedMode = vm.Modes.First(m => m.Key == 1);
        Assert.Equal(new[] { "SetPerformanceMode(1)" }, b.Calls.Where(c => c.StartsWith("SetPerformanceMode")).ToArray());

        // re-reading the current mode from Core must not switch again
        b.Calls.Clear();
        vm.OnAreaRefreshed("fans");
        Assert.DoesNotContain(b.Calls, c => c.StartsWith("SetPerformanceMode"));
    }

    [AvaloniaFact]
    public async Task Power_plan_battery_saver_disables_the_picker_and_changes_are_stored()
    {
        var b = SampleFansBackend.Create();
        var vm = await Loaded(b);
        Assert.True(vm.PowerModeEnabled);
        Assert.Equal("Balanced", vm.SelectedPowerMode!.Text);

        // default of the current mode: the override is removed instead of stored
        vm.SelectedPowerMode = vm.PowerModes.First(p => p.Text == "Best Performance");
        Assert.Equal("ded574b5-45a0-4f42-8737-46345c09c238", b.ModeStrings["powermode"]);
        vm.SelectedPowerMode = vm.PowerModes.First(p => p.Text == "Balanced");
        Assert.False(b.ModeStrings.ContainsKey("powermode"));

        b.BatterySaver = true;
        vm.InitPowerPlan();
        Assert.False(vm.PowerModeEnabled);
    }

    [AvaloniaFact]
    public async Task Cpu_boost_change_is_applied_once_and_remembered()
    {
        var b = SampleFansBackend.Create();
        var vm = await Loaded(b);
        b.Calls.Clear();

        vm.BoostIndex = 4;
        Assert.Contains("SetCpuBoost(4)", b.Calls);
        Assert.Equal(4, b.ModeInts["auto_boost"]);
    }

    [AvaloniaFact]
    public async Task Fan_drag_hint_and_calibration_result_share_the_status_line()
    {
        var b = SampleFansBackend.Create();
        var vm = await Loaded(b);
        Assert.False(vm.StatusVisible);

        b.Result = "Measured - CPU: 5800";
        vm.OnAreaRefreshed("fansresult");
        Assert.True(vm.StatusVisible);
        Assert.Equal("Measured - CPU: 5800", vm.StatusText);

        vm.FanDragHint(true);
        Assert.True(vm.StatusIsHint);
        Assert.NotEqual("Measured - CPU: 5800", vm.StatusText);
        vm.FanDragHint(false);
        Assert.Equal("Measured - CPU: 5800", vm.StatusText);

        vm.CalibrateCommand.Execute(null);
        Assert.False(vm.CalibrateEnabled);
        Assert.Contains("StartCalibration", b.Calls);
        vm.OnAreaRefreshed("fansaxis");
        Assert.True(vm.CalibrateEnabled);
    }
}
