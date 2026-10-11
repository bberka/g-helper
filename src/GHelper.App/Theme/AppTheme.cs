using Avalonia;
using Avalonia.Styling;

namespace GHelper.App.Theme;

/// <summary>
/// Applies the legacy "ui_mode" (dark | light | windows/auto) and "theme" (flat) config values to the running
/// Avalonia application. The palette itself is a pair of ThemeDictionaries in App.axaml.
/// </summary>
public static class AppTheme
{
    /// <summary>Legacy "theme" = "flat": no gradient borders / tinted backgrounds on buttons.</summary>
    public static bool Flat { get; private set; }

    public static event Action? Changed;

    public static ThemeVariant ParseUiMode(string? uiMode) => uiMode?.Trim().ToLowerInvariant() switch
    {
        "dark" => ThemeVariant.Dark,
        "light" => ThemeVariant.Light,
        _ => ThemeVariant.Default, // "windows", "auto", null: follow the OS
    };

    public static void Apply(string? uiMode, string? theme)
    {
        Flat = string.Equals(theme, "flat", StringComparison.OrdinalIgnoreCase);
        if (Application.Current is { } app)
            app.RequestedThemeVariant = ParseUiMode(uiMode);
        Changed?.Invoke();
    }

    public static bool IsDark(Application? app = null)
    {
        app ??= Application.Current;
        return app?.ActualThemeVariant == ThemeVariant.Dark;
    }
}
