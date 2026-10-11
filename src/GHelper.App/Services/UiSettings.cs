namespace GHelper.App.Services;

/// <summary>
/// The few config values the shell itself needs. Plain static state so the controls / windows / headless tests never
/// touch AppConfig (which needs registered platform services). <see cref="LoadFromConfig"/> fills it at start-up and is
/// safe to call when the platform is not available.
/// </summary>
public static class UiSettings
{
    /// <summary>Config "topmost": keep the settings window above others.</summary>
    public static bool Topmost { get; set; }

    /// <summary>Config "disable_osd": no toast popups.</summary>
    public static bool DisableOsd { get; set; }

    /// <summary>Handheld (ROG Ally) placement tweaks.</summary>
    public static bool IsAlly { get; set; }

    /// <summary>Config "ui_mode": dark | light | windows.</summary>
    public static string? UiMode { get; set; }

    /// <summary>Config "theme": "flat" or default.</summary>
    public static string? Theme { get; set; }

    public static void LoadFromConfig()
    {
        try
        {
            Topmost = AppConfig.Is("topmost");
            DisableOsd = AppConfig.Is("disable_osd");
            IsAlly = AppConfig.IsAlly();
            UiMode = AppConfig.GetString("ui_mode");
            Theme = AppConfig.GetString("theme");
        }
        catch (Exception ex)
        {
            AppLog.Write("UiSettings: config not available (" + ex.GetType().Name + ")");
        }
    }
}
