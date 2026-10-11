using GHelper.App.ViewModels;
using GHelper.Helpers;
using GHelper.Peripherals;
using GHelper.Properties;
using GHelper.USB;
using DrawingColor = System.Drawing.Color;

namespace GHelper.App.Backend;

// Keyboard backlight / rear light section (legacy InitAura, InitRearLight, SetAura, _VisualiseAura, SetColorPicker).
public sealed partial class MainBackend
{
    private string _auraModesKey = "";
    private string _rearModesKey = "";

    /// <summary>UI half of the legacy InitAura + InitRearLight; cheap to repeat (the backlight hotkey refreshes it).</summary>
    private void RefreshKeyboard()
    {
        // mode list (changes with the detected backlight type)
        var modes = Aura.GetModes().Select(m => new ChoiceItem((int)m.Key, m.Value)).ToList();
        string key = string.Join(",", modes.Select(m => m.Key));
        if (key != _auraModesKey || _vm.AuraModes.Count != modes.Count)
        {
            _auraModesKey = key;
            _vm.SetAuraModes(modes, (int)Aura.Mode);
        }
        else if (_vm.SelectedAuraMode?.Key != (int)Aura.Mode)
        {
            _vm.SelectAuraMode((int)Aura.Mode);
        }

        _vm.AuraModeVisible = !AppConfig.NoAura();
        _vm.KeyboardColorVisible = !Aura.isWhite;

        InitRearLight();
        VisualiseAura();

        _vm.SetKeyboardTitle(Strings.LaptopKeyboard, PeripheralsProvider.IsAuraSync);
    }

    private void InitRearLight()
    {
        if (!AppConfig.HasRearLight())
        {
            _vm.RearSectionVisible = false;
            return;
        }

        Aura.RearMode = (AuraMode)AppConfig.Get("rear_mode");
        Aura.SetRearColor(AppConfig.Get("rear_color"));

        var modes = Aura.GetRearModes().Select(m => new ChoiceItem((int)m.Key, m.Value)).ToList();
        string key = string.Join(",", modes.Select(m => m.Key));
        if (key != _rearModesKey || _vm.RearModes.Count != modes.Count)
        {
            _rearModesKey = key;
            _vm.SetRearModes(modes, (int)Aura.RearMode);
        }

        _vm.RearSwatch = ToAvalonia(Aura.RearColor);
        _vm.RearSectionVisible = true;
    }

    /// <summary>Legacy _VisualiseAura: swatches and the hint line below the combo box.</summary>
    private void VisualiseAura()
    {
        var color2 = Aura.HasSecondColor() ? ToAvalonia(Aura.Color2) : (Avalonia.Media.Color?)null;

        if (_vm.RearSectionVisible) _vm.RearSwatch = ToAvalonia(Aura.RearColor);

        bool dynamic = AppConfig.IsDynamicLighting() && DynamicLightingHelper.IsEnabled() && !AppConfig.IsDynamicLightingOnly();

        string hint = "";
        if (dynamic) hint = Strings.DisableDynamicLighting;
        else if (Aura.Mode == AuraMode.AMBIENT) hint = Strings.AmbientModeResources;

        _vm.VisualiseAura(ToAvalonia(Aura.Color1), color2, hint);
    }

    /// <summary>Legacy SetAura: apply on a worker, then repaint the swatches.</summary>
    private void ApplyAura()
    {
        Aura.ApplyAura();
        OnUi(VisualiseAura);
    }

    private void PickColor(string configKey, DrawingColor initial, bool allowRandom)
    {
        bool random = allowRandom && Aura.HasRandomColor();

        _shell.ShowColorPicker(initial, random, color =>
        {
            AppConfig.Set(configKey, color.ToArgb());
            Run("Aura colour", ApplyAura);
        });
    }
}
