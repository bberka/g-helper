using Avalonia.Media;
using GHelper.App.Views.Mouse;
using GHelper.App.Views.Peripherals;
using GHelper.Peripherals.Mouse;
using Xunit;

namespace GHelper.App.Tests.Peripherals;

/// <summary>View model logic of the mouse window against a hardware free mouse.</summary>
public class MouseViewModelTests
{
    public MouseViewModelTests()
    {
        TestPlatform.Ensure();
    }

    private static MouseViewModel Create(AsusMouse mouse) => new(mouse, a => a(), a => a());

    [Fact]
    public void Loads_device_state()
    {
        var mouse = new FakeMouse();
        var vm = Create(mouse);

        Assert.Equal("ROG Test Mouse", vm.Title);
        Assert.True(vm.PerformanceVisible);
        Assert.True(vm.LightingVisible);
        Assert.True(vm.BatteryVisible);
        Assert.Equal("85%", vm.BatteryText);
        Assert.False(vm.BatteryCharging);

        Assert.Equal(1, vm.SelectedProfile);
        Assert.Equal(3, vm.ProfileItems.Count);
        Assert.Equal(1600, vm.Dpi);
        Assert.Equal(100, vm.DpiMin);
        Assert.Equal(26000, vm.DpiMax);
        Assert.Equal(PollingRate.PR1000Hz, mouse.SupportedPollingrates()[vm.PollingRateIndex]);
        Assert.Equal(10, vm.AngleAdjustment);
        Assert.Equal("10°", vm.AngleAdjustmentText);
        Assert.Equal((int)DebounceTime.MS16, vm.ButtonDebounce);
        Assert.Equal(4, vm.Acceleration);
        Assert.Equal(6, vm.Deceleration);
        Assert.Equal(20, vm.LowBatteryWarning);
        Assert.Equal(3, vm.AutoPowerOffIndex); // 5 minutes
        Assert.Equal(1, vm.LiftOffIndex);

        // refreshing the view never writes to the device
        Assert.DoesNotContain(mouse.Packets, p => p.Length > 2 && p[1] == 0x51);
    }

    [Fact]
    public void Capabilities_drive_visibility()
    {
        var mouse = new FakeMouse
        {
            Battery_ = false, Profiles = false, DpiColors = false, Rgb = false, AutoPowerOff = false, LowBattery = false,
            AngleSnap = false, AngleTune = false, Acceleration_ = false, Deceleration_ = false, LiftOff = false,
            Debounce_ = false, MotionSync_ = false, ZoneMode_ = false, Bindings = false,
        };
        var vm = Create(mouse);

        Assert.False(vm.BatteryVisible);
        Assert.False(vm.ProfilesVisible);
        Assert.False(vm.BottomButtonsVisible);
        Assert.False(vm.DpiColorVisible);
        Assert.False(vm.LightingVisible);
        Assert.False(vm.EnergyVisible);
        Assert.False(vm.AngleSnappingPanelVisible);
        Assert.False(vm.AccelerationVisible);
        Assert.False(vm.DecelerationVisible);
        Assert.False(vm.LiftOffVisible);
        Assert.False(vm.DebounceVisible);
        Assert.False(vm.MotionSyncVisible);
        Assert.False(vm.ZoneModeVisible);
        Assert.False(vm.BindingsVisible);
        Assert.Empty(vm.BindingRows);

        // without DPI colours the slots get the fixed legacy dot colours
        Assert.Equal(Colors.Red, vm.DpiSlots[0].DotColor);
        Assert.Equal(Colors.Purple, vm.DpiSlots[1].DotColor);
        Assert.Equal(Colors.Blue, vm.DpiSlots[2].DotColor);
        Assert.True(vm.DpiSlots[3].IsAdd); // 3 profiles, room for a fourth
    }

    [Fact]
    public void Zero_dpi_profiles_hides_the_whole_performance_section()
    {
        var vm = Create(new FakeMouse { DpiCount = 0 });
        Assert.False(vm.PerformanceVisible);
    }

    [Fact]
    public void Polling_rate_change_is_written_and_8000hz_disables_motion_sync()
    {
        var mouse = new FakeMouse();
        var vm = Create(mouse);
        int idx8000 = Array.IndexOf(mouse.SupportedPollingrates(), PollingRate.PR8000Hz);

        vm.PollingRateIndex = idx8000;

        Assert.Equal(PollingRate.PR8000Hz, mouse.PollingRate);
        Assert.False(vm.MotionSyncEnabled);
        Assert.False(vm.MotionSync);

        vm.PollingRateIndex = 2;
        Assert.Equal(PollingRate.PR1000Hz, mouse.PollingRate);
        Assert.True(vm.MotionSyncEnabled);
        Assert.True(vm.MotionSync);
    }

    [Fact]
    public void Dpi_slider_only_writes_on_commit()
    {
        var mouse = new FakeMouse();
        var vm = Create(mouse);
        mouse.Packets.Clear();

        vm.Dpi = 2400;
        Assert.Empty(mouse.Packets);
        Assert.Equal(1600u, mouse.DpiSettings[1].DPI);

        vm.CommitDpi();
        Assert.Equal(2400u, mouse.DpiSettings[1].DPI);
        Assert.NotEmpty(mouse.Packets);
        Assert.Equal("DPI 2\n2400", vm.DpiSlots[1].Text);

        // committing an unchanged value is a no-op
        mouse.Packets.Clear();
        vm.CommitDpi();
        Assert.Empty(mouse.Packets);
    }

    [Fact]
    public void Dpi_slots_switch_add_and_delete_profiles()
    {
        var mouse = new FakeMouse();
        var vm = Create(mouse);

        // 3 profiles, 4th slot is "+"
        Assert.True(vm.DpiSlots[0].IsVisible);
        Assert.True(vm.DpiSlots[2].IsVisible);
        Assert.True(vm.DpiSlots[3].IsAdd);
        Assert.Equal("+", vm.DpiSlots[3].Text);
        Assert.True(vm.DpiSlots[1].Activated);
        Assert.True(vm.DpiSlots[0].CanDelete);

        vm.DpiSlots[0].Command!.Execute(null);
        Assert.Equal(1, mouse.DpiProfile);
        Assert.True(vm.DpiSlots[0].Activated);
        Assert.False(vm.DpiSlots[1].Activated);

        vm.DpiSlots[3].Command!.Execute(null);
        Assert.Equal(4, mouse.CurrentDPIProfileCount);
        Assert.False(vm.DpiSlots[3].IsAdd);
        Assert.Equal("DPI 4\n3200", vm.DpiSlots[3].Text);

        vm.DpiSlots[2].DeleteCommand!.Execute(null);
        Assert.Equal(3, mouse.CurrentDPIProfileCount);
        Assert.True(vm.DpiSlots[3].IsAdd);
    }

    [Fact]
    public void Dpi_colour_is_applied_to_the_active_profile()
    {
        var mouse = new FakeMouse();
        var vm = Create(mouse);

        vm.SetDpiColor(Color.FromRgb(10, 20, 30));

        Assert.Equal(System.Drawing.Color.FromArgb(255, 10, 20, 30), mouse.DpiSettings[1].Color);
        Assert.Equal(Color.FromRgb(10, 20, 30), vm.DpiColor);
    }

    [Fact]
    public void Lighting_mode_zone_and_colour_use_the_visible_zone()
    {
        var mouse = new FakeMouse();
        var vm = Create(mouse);

        Assert.True(vm.ZonesVisible);
        Assert.True(vm.ZoneAllActivated);
        Assert.Equal(60, vm.Brightness);
        Assert.Equal("Static", vm.LightingModeItems[vm.SelectedLightingModeIndex]);

        vm.ZoneLogoCommand.Execute(null);
        Assert.True(vm.ZoneLogoActivated);
        Assert.False(vm.ZoneAllActivated);
        // zones other than "All" only support the static family
        Assert.DoesNotContain("Rainbow", vm.LightingModeItems);

        vm.SetLightingColor(Color.FromRgb(1, 2, 3));
        Assert.Equal(System.Drawing.Color.FromArgb(255, 1, 2, 3), mouse.LightingSetting[0].RGBColor);

        vm.ZoneAllCommand.Execute(null);
        int rainbow = vm.LightingModeItems.IndexOf("Rainbow");
        Assert.True(rainbow >= 0);
        vm.SelectedLightingModeIndex = rainbow;
        Assert.Equal(LightingMode.Rainbow, mouse.LightingSetting[0].LightingMode);
        Assert.True(vm.AnimationSpeedVisible);
        Assert.True(vm.AnimationDirectionVisible);
        Assert.False(vm.RandomColorVisible);
    }

    [Fact]
    public void Brightness_is_written_on_commit_only()
    {
        var mouse = new FakeMouse();
        var vm = Create(mouse);

        vm.Brightness = 90;
        Assert.Equal(60, mouse.LightingSetting[0].Brightness);

        vm.CommitBrightness();
        Assert.Equal(90, mouse.LightingSetting[0].Brightness);
    }

    [Fact]
    public void Zone_mode_toggles_which_controls_are_enabled()
    {
        var mouse = new FakeMouse();
        var vm = Create(mouse);

        Assert.False(vm.ZoneModeControlsEnabled);
        Assert.True(vm.NormalControlsEnabled);

        vm.ZoneMode = true;
        Assert.True(mouse.ZoneMode);
        Assert.True(vm.ZoneModeControlsEnabled);
        Assert.False(vm.NormalControlsEnabled);

        vm.ZoneModeDpi = 3200;
        vm.CommitZoneModeDpi();
        Assert.Equal(3200, mouse.ZoneModeDPI);

        vm.ZoneModePollingRateIndex = 3;
        Assert.Equal(PollingRate.PR8000Hz, mouse.ZoneModePollingRate);
    }

    [Fact]
    public void Energy_settings_are_written()
    {
        var mouse = new FakeMouse();
        var vm = Create(mouse);

        vm.AutoPowerOffIndex = AutoPowerOffNever(vm);
        Assert.Equal(PowerOffSetting.Never, mouse.PowerOffSetting);

        vm.LowBatteryWarning = 50;
        Assert.Equal(20, mouse.LowBatteryWarning);
        vm.CommitLowBatteryWarning();
        Assert.Equal(50, mouse.LowBatteryWarning);
        Assert.Equal("50%", vm.LowBatteryWarningText);
    }

    private static int AutoPowerOffNever(MouseViewModel vm) => vm.AutoPowerOffItems.Count - 1;

    [Fact]
    public void Button_bindings_normal_and_shift_layer()
    {
        var mouse = new FakeMouse { SpeedShift = true };
        var vm = Create(mouse);

        Assert.True(vm.BindingsVisible);
        Assert.True(vm.HasSpeedShift);
        Assert.Equal(8, vm.BindingRows.Count);
        Assert.Equal("Left Click", vm.BindingRows[0].Name);
        Assert.Equal((ushort)0x01F0, vm.BindingRows[0].Selected!.Code);

        // headings are part of the list but never selected
        Assert.Contains(vm.BindingRows[0].Entries, e => e.IsSeparator);
        Assert.All(vm.BindingRows, r => Assert.False(r.Selected!.IsSeparator));

        var disabled = vm.BindingRows[0].Entries.First(e => !e.IsSeparator && e.Code == 0x0000);
        vm.BindingRows[0].Selected = disabled;
        Assert.Equal((ushort)0x0000, mouse.ButtonBindings[0]);

        // shift layer: the button bound to Speed Shift is locked
        vm.BindingRows[1].Selected = vm.BindingRows[1].Entries.First(e => !e.IsSeparator && e.Code == AsusMouse.SpeedShiftCode);
        Assert.Equal(AsusMouse.SpeedShiftCode, mouse.ButtonBindings[1]);

        vm.ShiftLayerCommand.Execute(null);
        Assert.True(vm.ShiftLayer);
        Assert.False(vm.BindingRows[1].IsEnabled);
        Assert.True(vm.BindingRows[0].IsEnabled);

        // reset restores the factory slots
        vm.NormalLayerCommand.Execute(null);
        vm.ResetBindingsCommand.Execute(null);
        Assert.Equal((ushort)0x01F0, mouse.ButtonBindings[0]);
        Assert.Equal((ushort)0x01F0, vm.BindingRows[0].Selected!.Code);
    }

    [Fact]
    public async Task Import_rejects_foreign_data_and_applies_exported_profiles()
    {
        var mouse = new FakeMouse();
        var vm = Create(mouse);
        var alerts = new List<string>();
        PeripheralHost.AlertOverride = (_, text) => alerts.Add(text);
        try
        {
            vm.PickImportData = () => Task.FromResult<byte[]?>(new byte[] { 1, 2, 3 });
            await vm.ImportAsync();
            Assert.Single(alerts);

            byte[]? saved = null;
            vm.SaveExportData = data => { saved = data; return Task.CompletedTask; };
            await vm.ExportAsync();
            Assert.NotNull(saved);

            alerts.Clear();
            vm.PickImportData = () => Task.FromResult<byte[]?>(saved);
            await vm.ImportAsync();
            Assert.Empty(alerts);
        }
        finally
        {
            PeripheralHost.AlertOverride = null;
        }
    }

    [Fact]
    public void Device_not_ready_requests_close()
    {
        var mouse = new NotReadyMouse();
        var vm = Create(mouse);
        Assert.True(vm.CloseWasRequested);
    }

    private sealed class NotReadyMouse : AsusMouseBase
    {
        public override void SynchronizeDevice() => IsDeviceReady = false;
    }

    // minimal concrete mouse for the not-ready case
    private abstract class AsusMouseBase : AsusMouse
    {
        protected AsusMouseBase() : base(0x0B05, 0x7778, "fake", false) { }
        public override string GetDisplayName() => "Sleeping mouse";
        public override int ProfileCount() => 1;
        public override int DPIProfileCount() => 1;
        public override PollingRate[] SupportedPollingrates() => new[] { PollingRate.PR1000Hz };
        protected override byte[]? WriteForResponse(byte[] packet, int matchLength = 3) => null;
    }
}
