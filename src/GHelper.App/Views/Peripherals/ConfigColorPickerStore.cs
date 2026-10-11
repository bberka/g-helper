using GHelper.App.Controls;

namespace GHelper.App.Views.Peripherals;

/// <summary>Custom swatches of the colour picker, persisted like the legacy RColorPicker (config "aura_color_custom").</summary>
public sealed class ConfigColorPickerStore : IColorPickerStore
{
    private const string CustomKey = "aura_color_custom";

    public static readonly ConfigColorPickerStore Instance = new();

    public string? LoadCustomColors()
    {
        try { return AppConfig.GetString(CustomKey, ""); }
        catch { return null; }
    }

    public void SaveCustomColors(string value)
    {
        try { AppConfig.Set(CustomKey, value); }
        catch { /* config not available (design time / tests) */ }
    }
}
