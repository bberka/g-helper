using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using GHelper.App.Controls;
using GHelper.App.Views.Headset;
using GHelper.Peripherals.Headset;
using Xunit;

namespace GHelper.App.Tests.Peripherals;

/// <summary>Headset without hardware: Core's TestMode answers every packet with canned data.</summary>
internal sealed class FakeHeadset : AsusHeadset
{
    public bool Rgb = true;
    public bool Eq = true;
    public bool SideTone = true;
    public bool Noise = true;
    public bool NoiseLevel = true;
    public bool Mic = true;
    public bool Voice = true;
    public bool Anc_ = true;
    public bool Dirac_ = true;
    public bool Power = true;
    public bool Battery_ = true;

    public FakeHeadset() : base(0x0B05, 0x7779)
    {
        TestMode = true;
    }

    public override string GetDisplayName() => "ROG Test Headset";
    public override bool HasBattery() => Battery_;
    public override bool HasRGB() => Rgb;
    public override bool HasBrightness() => Rgb;
    public override bool HasEqualizer() => Eq;
    public override bool HasSidetone() => SideTone;
    public override int MaxSidetone() => 20;
    public override bool HasNoiseReduction() => Noise;
    public override bool HasNoiseReductionLevel() => NoiseLevel;
    public override bool HasMicrophoneType() => Mic;
    public override bool HasVoicePrompt() => Voice;
    public override bool HasAnc() => Anc_;
    public override bool HasDirac() => Dirac_;
    public override bool HasPowerSettings() => Power;
    public override bool HasLowBatteryWarning() => Power;
    public override bool HasReset() => true;
    public override int[] SleepTimers() => new[] { 0, 10, 30, 60 };
    public override HeadsetLightingMode[] SupportedLightingModes() => new[]
    {
        HeadsetLightingMode.Static, HeadsetLightingMode.Breathing, HeadsetLightingMode.Rainbow,
    };

    public override void ReadBattery()
    {
        Battery = 77;
        Charging = false;
        SetDeviceReady(true);
    }

    public override void SynchronizeDevice()
    {
        ReadBattery();
        ReadLighting();
        ReadAudio();
    }
}

public class HeadsetTests
{
    public HeadsetTests()
    {
        TestPlatform.Ensure();
    }

    private static HeadsetViewModel Create(FakeHeadset h) => new(h, a => a(), a => a());

    [Fact]
    public void Loads_state_and_capabilities()
    {
        var h = new FakeHeadset();
        var vm = Create(h);

        Assert.Equal("ROG Test Headset", vm.Title);
        Assert.True(vm.BatteryVisible);
        Assert.Equal("77%", vm.BatteryText);
        Assert.True(vm.LightingVisible);
        Assert.True(vm.AudioVisible);
        Assert.True(vm.AncVisible);
        Assert.True(vm.DiracVisible);
        Assert.True(vm.EnergyVisible);
        Assert.Equal(10, vm.Bands.Count);
        // off + the three supported modes
        Assert.Equal(4, vm.LightingModeItems.Count);
        // 8 factory presets + 3 custom slots
        Assert.Equal(h.EqualizerPresets.Length + AsusHeadset.CustomPresets, vm.PresetItems.Count);
        Assert.Equal(4, vm.AutoPowerOffItems.Count);
        Assert.Equal("Never", vm.AutoPowerOffItems[0]);
    }

    [Fact]
    public void Capabilities_hide_sections()
    {
        var vm = Create(new FakeHeadset
        {
            Rgb = false, Eq = false, SideTone = false, Noise = false, Mic = false, Voice = false,
            Anc_ = false, Dirac_ = false, Power = false, Battery_ = false,
        });

        Assert.False(vm.BatteryVisible);
        Assert.False(vm.LightingVisible);
        Assert.False(vm.AudioVisible);
        Assert.False(vm.AncVisible);
        Assert.False(vm.DiracVisible);
        Assert.False(vm.EnergyVisible);
        Assert.False(vm.LowBatteryWarningVisible);
    }

    [Fact]
    public void Toggles_and_combos_write_the_device_but_loading_does_not()
    {
        var h = new FakeHeadset();
        var vm = Create(h);

        vm.EqualizerEnabled = true;
        Assert.True(h.EqualizerEnabled);

        vm.SidetoneEnabled = true;
        Assert.True(h.SidetoneEnabled);

        vm.Sidetone = 12;
        Assert.NotEqual(12, h.Sidetone);
        vm.CommitSidetone();
        Assert.Equal(12, h.Sidetone);

        vm.AncMode = 1;
        Assert.Equal(1, h.AncMode);
        Assert.True(vm.AncLevelEnabled);
        vm.AncMode = 0;
        Assert.False(vm.AncLevelEnabled);

        vm.Dirac = true;
        Assert.True(h.Dirac);

        vm.VoicePromptIndex = 0; // English = prompt value 1
        Assert.Equal(1, h.VoicePrompt);
    }

    [Fact]
    public void Preset_selection_loads_gains_and_band_edit_updates_the_device()
    {
        var h = new FakeHeadset();
        var vm = Create(h);

        int rock = Array.FindIndex(h.EqualizerPresets, p => p.Name == "Rock");
        vm.PresetIndex = rock;
        Assert.Equal(h.EqualizerPresets[rock].Gains, h.EqualizerGains);
        Assert.True(vm.EqualizerEnabled);
        Assert.Equal(h.EqualizerPresets[rock].Gains[2], (byte)vm.Bands[2].Value);

        vm.Bands[4].Value = 90;
        vm.CommitBand(4);
        Assert.Equal(90, h.EqualizerGains[4]);
        // no longer matches a preset
        Assert.Equal(-1, vm.PresetIndex);
    }

    [Fact]
    public void Lighting_brightness_writes_on_commit()
    {
        var h = new FakeHeadset();
        var vm = Create(h);

        vm.Brightness = 40;
        Assert.NotEqual(40, h.Brightness);
        vm.CommitBrightness();
        Assert.Equal(40, h.Brightness);

        vm.SetLightingColor(Avalonia.Media.Color.FromRgb(5, 6, 7));
        Assert.Equal(System.Drawing.Color.FromArgb(255, 5, 6, 7), h.LightingColor);
    }

    [AvaloniaFact]
    public void Renders_dark_and_light()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new HeadsetWindow(new FakeHeadset());
        MainWindowSnapshotTests.Render(window, "headset-dark");

        Assert.Equal("ROG Test Headset", window.Title);
        var slider = window.FindControl<RSlider>("BrightnessSlider")!;
        Assert.Equal(100, slider.Maximum);
        window.Close();

        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        window = new HeadsetWindow(new FakeHeadset());
        MainWindowSnapshotTests.Render(window, "headset-light");
        window.Close();
    }
}
