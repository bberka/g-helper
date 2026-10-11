namespace GHelper.Platform;

public interface ISystemInfo
{
    PlatformKind Kind { get; }

    /// <summary>Full OS description for the log.</summary>
    string OsDescription { get; }

    /// <summary>Directory for config.json, log.txt, ... Windows: %APPDATA%\GHelper. Linux: $XDG_CONFIG_HOME/ghelper (~/.config/ghelper).</summary>
    string ConfigDirectory { get; }

    /// <summary>Machine-wide fallback config dir (Windows %PROGRAMDATA%\GHelper, Linux /etc/ghelper).</summary>
    string SharedConfigDirectory { get; }

    /// <summary>Product/model string, e.g. "ROG Zephyrus G14 GA402RJ" (Win32_ComputerSystem.Model / DMI product_name).</summary>
    string GetModel();

    /// <summary>(BIOS version, short model) e.g. ("GA402RJ.320", "GA402RJ") (Win32_BIOS / DMI bios_version).</summary>
    (string Bios, string ModelShort) GetBiosAndModel();

    string GetSerialNumber();

    string? GetCpuName();

    /// <summary>Has administrator (Windows) / root (Linux) rights.</summary>
    bool IsElevated { get; }

    /// <summary>Runs as LocalSystem (Windows scheduled "charge" action). Always false on Linux.</summary>
    bool IsRunningAsSystem { get; }

    /// <summary>Is the OS dark mode active (for theming). Null if unknown.</summary>
    bool? IsDarkTheme();

    /// <summary>True on desktop (non laptop) machines (Win32_ComputerSystem.PCSystemType == 1 / DMI chassis_type).</summary>
    bool IsDesktop { get; }

    /// <summary>OLED panel flag legacy code read from the registry (AppConfig.IsOLED). Null if unknown.</summary>
    bool? IsOledPanel();
}
