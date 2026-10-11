using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using GHelper.App.Services;
using GHelper.App.Views.Extra;
using Xunit;

namespace GHelper.App.Tests;

/// <summary>Extra settings window: rendered layouts (PNG artifacts) and the legacy Extra.cs rules on a fake backend.</summary>
public class ExtraWindowTests
{
    private static async Task Settle()
    {
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
    }

    // ------------------------------------------------------------------ rendering

    [AvaloniaFact]
    public async Task Extra_window_renders_full_layout_dark()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var b = SampleExtraBackend.Create();
        b.Caps = b.Caps with { Debug = true };
        b.Strs["m1"] = "mute";
        b.Strs["m2"] = "custom";
        b.Strs["m2_custom"] = "notepad.exe";
        var window = new ExtraWindow(0, b);
        window.Show();
        await Settle();
        var frame = MainWindowSnapshotTests.Render(window, "extra-dark");

        Assert.Equal(506, window.Width);
        Assert.True(frame.PixelSize.Height > 500, "height " + frame.PixelSize.Height);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Extra_window_renders_light_without_optional_rows()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var b = SampleExtraBackend.Create();
        b.Caps = new ExtraCaps { IsAlly = true };
        b.Cores = null;
        b.Apu = null;
        b.Elmb = -1;
        b.BootSound = -1;
        b.StatusLed = -1;
        b.NumberPad = -1;
        b.Optimal = -1;
        var window = new ExtraWindow(0, b);
        window.Show();
        await Settle();
        MainWindowSnapshotTests.Render(window, "extra-ally-light");
        window.Close();
    }

    // ------------------------------------------------------------------ rules of Extra.cs

    [AvaloniaFact]
    public void Strix_shows_the_fifth_m_key_and_relabels_m4()
    {
        var vm = new ExtraViewModel(SampleExtraBackend.Create());
        var m4 = vm.KeyRows.Single(r => r.Name == "m4");
        var m5 = vm.KeyRows.Single(r => r.Name == "m5");
        Assert.Equal("M5/ROG", m4.Label);
        Assert.True(m5.IsVisible);
        Assert.Equal(9, vm.KeyRows.Count);
    }

    [AvaloniaFact]
    public void Ally_hides_fn_keys_and_binds_cc_and_paddle()
    {
        var b = SampleExtraBackend.Create();
        b.Caps = new ExtraCaps { IsAlly = true };
        var vm = new ExtraViewModel(b);

        Assert.False(vm.KeyRows[0].IsVisible); // M1
        Assert.False(vm.KeyRows[1].IsVisible); // M2
        Assert.Equal("Cmd Center", vm.KeyRows[2].Label);
        Assert.Equal("cc", vm.KeyRows[2].Name);
        Assert.Equal("ROG", vm.KeyRows[3].Label);
        Assert.Equal("Back Paddles", vm.KeyRows[5].Label);
        Assert.Equal("paddle", vm.KeyRows[5].Name);
        Assert.False(vm.KeyRows[6].IsVisible); // FN+C
        Assert.False(vm.KeyRows[7].IsVisible); // FN+V
        Assert.False(vm.GpuApps.IsVisible);
        Assert.False(vm.Clamshell.IsVisible);
    }

    [AvaloniaFact]
    public void Default_entry_is_named_after_the_key_function()
    {
        var vm = new ExtraViewModel(SampleExtraBackend.Create());
        var m1 = vm.KeyRows[0];
        Assert.DoesNotContain(m1.Actions, a => a.Key == "volume_down");
        Assert.Equal("", m1.Actions[0].Key);
        Assert.NotEqual("--------------", m1.Actions[0].Text);

        var fne = vm.KeyRows.Single(r => r.Name == "fne");
        Assert.Equal("Calculator", fne.Actions[0].Text);
    }

    [AvaloniaFact]
    public void Changing_a_binding_stores_it_and_reapplies_the_m_keys()
    {
        var b = SampleExtraBackend.Create();
        var vm = new ExtraViewModel(b);
        var m2 = vm.KeyRows.Single(r => r.Name == "m2");

        m2.SelectedAction = m2.Actions.First(a => a.Key == "mute");
        Assert.Equal("mute", b.Strs["m2"]);
        Assert.Contains("ApplyMKeys", b.Calls);

        b.Calls.Clear();
        m2.CustomText = "calc.exe";
        Assert.Equal("calc.exe", b.Strs["m2_custom"]);
        Assert.DoesNotContain("ApplyMKeys", b.Calls);

        var fnc = vm.KeyRows.Single(r => r.Name == "fnc");
        fnc.SelectedAction = fnc.Actions.First(a => a.Key == "aura");
        Assert.Equal("aura", b.Strs["fnc"]);
        Assert.DoesNotContain("ApplyMKeys", b.Calls); // only M1..M5 go through MKeyControl
    }

    [AvaloniaFact]
    public void Loading_does_not_write_config_and_unknown_values_fall_back_to_default()
    {
        var b = SampleExtraBackend.Create();
        b.Strs["m1"] = "does_not_exist";
        b.Strs["m3"] = "play";
        b.Calls.Clear();
        var vm = new ExtraViewModel(b);

        Assert.Equal("", vm.KeyRows[0].SelectedAction!.Key);
        Assert.Equal("play", vm.KeyRows[2].SelectedAction!.Key);
        Assert.Empty(b.Calls);
    }

    [AvaloniaFact]
    public void Reset_clears_only_the_m_keys()
    {
        var b = SampleExtraBackend.Create();
        b.Strs["m1"] = "mute";
        b.Strs["m4_custom"] = "x";
        b.Strs["fnc"] = "aura";
        var vm = new ExtraViewModel(b);

        vm.ResetBindingsCommand.Execute(null);

        Assert.Equal("", b.Strs["m1"]);
        Assert.Equal("", b.Strs["m4_custom"]);
        Assert.Equal("aura", b.Strs["fnc"]);
        Assert.Equal("", vm.KeyRows[0].SelectedAction!.Key);
        Assert.Contains("ResetMKeys", b.Calls);
    }

    [AvaloniaFact]
    public void Backlight_zone_visibility_follows_the_hardware()
    {
        var b = SampleExtraBackend.Create();
        b.Caps = b.Caps with { HasLightbar = false, HasLogo = true, HasRearglow = false, IsBacklightZones = false };
        var vm = new ExtraViewModel(b);

        Assert.True(vm.Zones[0].IsVisible);
        Assert.False(vm.Zones[0].LabelVisible);     // keyboard row label + battery check need backlight zones
        Assert.False(vm.Zones[0].Battery.IsVisible);
        Assert.True(vm.Zones[1].IsVisible);         // logo
        Assert.False(vm.Zones[2].IsVisible);        // lightbar
        Assert.False(vm.Zones[3].IsVisible);        // lid
    }

    [AvaloniaFact]
    public void Zone_check_writes_every_zone_and_applies_power()
    {
        var b = SampleExtraBackend.Create();
        var vm = new ExtraViewModel(b);
        b.Calls.Clear();

        vm.Zones[1].Boot.IsChecked = false;

        Assert.Equal(0, b.Ints["keyboard_boot_logo"]);
        Assert.Equal(0, b.Ints["keyboard_shutdown_logo"]);
        Assert.Equal(1, b.Ints["keyboard_awake"]);
        Assert.Equal(0, b.Ints["keyboard_awake_bat"]);
        Assert.Contains("ApplyAuraPower", b.Calls);
        Assert.DoesNotContain("ApplyAura", b.Calls); // only the old Strix re-applies the effect

        b.IsOldStrix = true;
        vm.Zones[0].Awake.IsChecked = false;
        Assert.Contains("ApplyAura", b.Calls);
    }

    [AvaloniaFact]
    public void Brightness_slider_saves_by_power_source_and_core_updates_do_not_loop()
    {
        var b = SampleExtraBackend.Create();
        var vm = new ExtraViewModel(b);
        Assert.Equal(2, vm.Brightness);

        vm.Brightness = 3;
        Assert.Equal(3, b.Ints["keyboard_brightness"]);
        Assert.Contains("ApplyBrightness(3)", b.Calls);

        b.OnBattery = true;
        vm.Brightness = 1;
        Assert.Equal(1, b.Ints["keyboard_brightness_ac"]); // legacy key naming

        b.Calls.Clear();
        b.Backlight = 0;
        vm.OnAreaRefreshed("keyboard");
        Assert.Equal(0, vm.Brightness);
        Assert.DoesNotContain(b.Calls, c => c.StartsWith("ApplyBrightness"));
    }

    [AvaloniaFact]
    public void Timeout_numbers_are_stored_together_and_restart_the_timer()
    {
        var b = SampleExtraBackend.Create();
        b.Ints["keyboard_timeout"] = 30;
        b.Ints["keyboard_ac_timeout"] = 0;
        var vm = new ExtraViewModel(b);
        Assert.Equal(30m, vm.BacklightTime);

        vm.BacklightPluggedTime = 120;
        Assert.Equal(120, b.Ints["keyboard_ac_timeout"]);
        Assert.Equal(30, b.Ints["keyboard_timeout"]);
        Assert.Contains("InitBacklightTimer", b.Calls);
    }

    [AvaloniaFact]
    public void Optional_checks_follow_capabilities()
    {
        var b = SampleExtraBackend.Create();
        b.Elmb = -1;
        b.BootSound = -1;
        b.StatusLed = -1;
        b.NumberPad = 1;
        b.Caps = b.Caps with { OverdriveSupported = false, GpuModeSupported = false, NvidiaGpu = false, Keystone = false };
        var vm = new ExtraViewModel(b);

        Assert.False(vm.Elmb.IsVisible);
        Assert.False(vm.BootSound.IsVisible);
        Assert.False(vm.StatusLed.IsVisible);
        Assert.True(vm.NumberPad.IsVisible);
        Assert.True(vm.NumberPad.IsChecked);
        Assert.False(vm.NoOverdrive.IsVisible);
        Assert.False(vm.GpuApps.IsVisible);
        Assert.False(vm.Usbc.IsVisible);
        Assert.False(vm.NvPlatform.IsVisible);
        Assert.False(vm.KeystoneSound.IsVisible);
        Assert.True(vm.Aspm.IsVisible);
        Assert.True(vm.StandbyNetworking.IsVisible);
    }

    [AvaloniaFact]
    public void Checks_store_their_config_key_and_call_core()
    {
        var b = SampleExtraBackend.Create();
        var vm = new ExtraViewModel(b);
        b.Calls.Clear();

        vm.Aspm.IsChecked = true;
        Assert.Equal(1, b.Ints["aspm"]);
        Assert.Contains("SetAspm(True)", b.Calls);

        vm.StandbyNetworking.IsChecked = true;
        Assert.Equal(1, b.Ints["standby_networking"]);
        Assert.Contains("SetStandbyNetworking(True)", b.Calls);

        vm.Topmost.IsChecked = true;
        Assert.Equal(1, b.Ints["topmost"]);
        Assert.Contains("SetTopmost(True)", b.Calls);

        vm.Elmb.IsVisible = true;
        vm.Elmb.IsChecked = true;
        Assert.Equal(1, b.Ints["elmb"]);
        Assert.Contains("SetElmb(True)", b.Calls);

        vm.BootSound.IsChecked = false;
        Assert.Equal(0, b.Ints["boot_sound"]);
        Assert.Contains("SetBootSound(False)", b.Calls);

        vm.Xgm.IsChecked = false;
        Assert.Equal(0, b.Ints["xmg_light"]);
        Assert.Contains("SetXgmLight(False)", b.Calls);
    }

    [AvaloniaFact]
    public void Hibernate_is_stored_and_cores_ask_before_restarting()
    {
        var b = SampleExtraBackend.Create();
        var vm = new ExtraViewModel(b);
        Assert.Equal(30m, vm.HibernateAfter);

        vm.HibernateAfter = 60;
        Assert.Contains("SetHibernateAfter(60)", b.Calls);

        int applied = -1;
        b.Cores = b.Cores! with { Apply = (e, p) => applied = e * 100 + p };
        vm = new ExtraViewModel(b);
        Assert.True(vm.CoresVisible);
        vm.CoresEIndex = 1;
        vm.CoresPIndex = 0;

        b.ConfirmAnswer = false;
        vm.ApplyCoresCommand.Execute(null);
        Assert.Equal(-1, applied);
        Assert.DoesNotContain("Restart", b.Calls);

        b.ConfirmAnswer = true;
        vm.ApplyCoresCommand.Execute(null);
        Assert.Equal(100, applied);
        Assert.Contains("Restart", b.Calls);
    }

    [AvaloniaFact]
    public void Apu_memory_change_asks_for_a_restart_only_when_applied()
    {
        var b = SampleExtraBackend.Create();
        var vm = new ExtraViewModel(b);
        Assert.True(vm.ApuVisible);
        Assert.Equal(0, vm.ApuIndex);

        b.Calls.Clear();
        vm.ApuIndex = 2;
        Assert.Contains("Confirm", b.Calls);
        Assert.Contains("Restart", b.Calls);

        // relaunch as administrator: no question
        b.Apu = new ApuMemory(new[] { "Auto", "1G" }, 0, _ => false);
        vm = new ExtraViewModel(b);
        b.Calls.Clear();
        vm.ApuIndex = 1;
        Assert.DoesNotContain("Confirm", b.Calls);
    }

    [AvaloniaFact]
    public async Task Services_toggle_stops_running_services_and_refreshes_the_label()
    {
        var b = SampleExtraBackend.Create();
        var vm = new ExtraViewModel(b);
        await vm.InitServicesAsync();
        Assert.True(vm.ServicesRunning);
        Assert.EndsWith("3", vm.ServicesText);

        await vm.ToggleServicesAsync();
        Assert.Contains("StopServices", b.Calls);
        Assert.Contains("ReinitInput", b.Calls);
        Assert.False(vm.ServicesRunning);
        Assert.True(vm.ServicesButtonEnabled);

        await vm.ToggleServicesAsync();
        Assert.Contains("StartServices", b.Calls);
        Assert.True(vm.ServicesRunning);
    }

    [AvaloniaFact]
    public void Services_button_asks_for_elevation_when_not_administrator()
    {
        var b = SampleExtraBackend.Create();
        b.Admin = false;
        var vm = new ExtraViewModel(b);

        vm.ServicesCommand.Execute(null);
        Assert.Contains("RunAsAdmin(services)", b.Calls);
        Assert.DoesNotContain("StopServices", b.Calls);
    }

    [AvaloniaFact]
    public void Acpi_test_panel_is_debug_only()
    {
        var b = SampleExtraBackend.Create();
        var vm = new ExtraViewModel(b);
        Assert.False(vm.AcpiVisible);

        b.Caps = b.Caps with { Debug = true };
        vm = new ExtraViewModel(b);
        Assert.True(vm.AcpiVisible);
        vm.AcpiSendCommand.Execute(null);
        Assert.Contains("AcpiTest(110034,0x0303)", b.Calls);
        Assert.Equal("ACPI DEVS Test : 1", vm.AcpiTitle);
    }

    [AvaloniaFact]
    public async Task Services_page_argument_toggles_services_when_the_window_opens()
    {
        var b = SampleExtraBackend.Create();
        var window = new ExtraWindow(1, b);
        window.Show();
        await Settle();
        Assert.Contains("StopServices", b.Calls);
        window.Close();
    }

    [AvaloniaFact]
    public void Windows_are_registered_with_the_registry()
    {
        // module initializers of the two windows run when the assembly loads
        Assert.True(WindowRegistry.IsRegistered("fans"));
        Assert.True(WindowRegistry.IsRegistered("extra"));
    }
}
