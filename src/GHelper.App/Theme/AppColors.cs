using Avalonia.Media;

namespace GHelper.App.Theme;

/// <summary>
/// Theme independent accent colours (legacy app/UI/RForm.cs). The theme dependent palette
/// (formBack, buttonMain, ...) lives in App.axaml as ThemeDictionaries so it follows the OS / config override.
/// </summary>
public static class AppColors
{
    public static readonly Color Eco = Color.FromArgb(255, 6, 180, 138);
    public static readonly Color Standard = Color.FromArgb(255, 58, 174, 239);
    public static readonly Color Turbo = Color.FromArgb(255, 255, 32, 32);
    public static readonly Color Custom = Color.FromArgb(255, 255, 128, 0);
    public static readonly Color Gray = Color.FromArgb(255, 168, 168, 168);
    public static readonly Color Orange = Colors.Orange;

    /// <summary>SystemColors.ControlLightLight - text on coloured buttons (Fn-Lock, Overlay, Energy Saver ...).</summary>
    public static readonly Color OnAccent = Color.FromArgb(255, 255, 255, 255);

    /// <summary>SystemColors.ControlDark - inactive small buttons / version label.</summary>
    public static readonly Color ControlDark = Color.FromArgb(255, 160, 160, 160);

    /// <summary>SystemColors.GrayText - tooltips below the button rows.</summary>
    public static readonly Color GrayText = Color.FromArgb(255, 109, 109, 109);
}
