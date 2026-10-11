namespace GHelper.Platform.Linux;

/// <summary>
/// Locates the sysfs/debugfs directories of the asus-wmi driver family once. All paths are logical paths (see
/// <see cref="SysfsRoot"/>) and null when the feature is not exposed by the running kernel.
/// </summary>
public sealed class AsusWmiLayout
{
    // /sys/devices/platform/<name>: asus-nb-wmi (notebooks), eeepc-wmi (netbooks/desktops), asus-wmi (generic)
    private static readonly string[] PlatformNames = ["asus-nb-wmi", "eeepc-wmi", "asus-wmi"];
    private static readonly string[] BatteryNames = ["BAT0", "BAT1", "BATC", "BATT"];

    public const string ArmouryAttributes = "/sys/class/firmware-attributes/asus-armoury/attributes";

    private readonly SysfsRoot root;

    public AsusWmiLayout(SysfsRoot root)
    {
        this.root = root;
        Refresh();
    }

    /// <summary>/sys/devices/platform/asus-nb-wmi (or eeepc-wmi): deprecated attributes, hwmon, leds.</summary>
    public string? PlatformDir { get; private set; }

    /// <summary>/sys/class/firmware-attributes/asus-armoury/attributes (kernel 6.19+) or null.</summary>
    public string? ArmouryDir { get; private set; }

    /// <summary>"profile" file of the asus-wmi platform_profile handler.</summary>
    public string? ProfileFile { get; private set; }
    public string? ProfileChoicesFile { get; private set; }

    /// <summary>hwmon device named "asus" (fan RPM, temp1_input).</summary>
    public string? FanHwmonDir { get; private set; }

    /// <summary>hwmon device named "asus_custom_fan_curve" (pwmN_auto_pointM_*, pwmN_enable).</summary>
    public string? FanCurveHwmonDir { get; private set; }

    /// <summary>power_supply directory exposing charge_control_end_threshold.</summary>
    public string? BatteryDir { get; private set; }

    /// <summary>/sys/class/leds/asus::kbd_backlight</summary>
    public string? KeyboardLedDir { get; private set; }

    public string? ScreenpadBacklightDir { get; private set; }

    /// <summary>/sys/kernel/debug/asus-nb-wmi when debugfs is mounted and readable.</summary>
    public string? DebugfsDir { get; private set; }

    public bool IsAsusWmiPresent => PlatformDir is not null || ArmouryDir is not null;

    public void Refresh()
    {
        PlatformDir = PlatformNames
            .Select(n => "/sys/devices/platform/" + n)
            .FirstOrDefault(root.DirectoryExists);

        ArmouryDir = root.DirectoryExists(ArmouryAttributes) ? ArmouryAttributes : null;

        ResolveProfile();
        ResolveHwmon();
        ResolveBattery();
        ResolveLeds();
        ResolveDebugfs();
    }

    private void ResolveProfile()
    {
        ProfileFile = null;
        ProfileChoicesFile = null;

        // kernel >= 6.14: one class device per handler
        foreach (string dir in root.ListDirectories("/sys/class/platform-profile"))
        {
            if (root.ReadText(dir + "/name") == "asus-wmi")
            {
                ProfileFile = dir + "/profile";
                ProfileChoicesFile = dir + "/choices";
                return;
            }
        }

        // older kernels: single global handler. Only trusted on ASUS machines.
        if (PlatformDir is not null && root.FileExists("/sys/firmware/acpi/platform_profile"))
        {
            ProfileFile = "/sys/firmware/acpi/platform_profile";
            ProfileChoicesFile = "/sys/firmware/acpi/platform_profile_choices";
        }
    }

    private void ResolveHwmon()
    {
        FanHwmonDir = null;
        FanCurveHwmonDir = null;
        foreach (string dir in root.ListDirectories("/sys/class/hwmon"))
        {
            string? name = root.ReadText(dir + "/name");
            if (name == "asus" && FanHwmonDir is null) FanHwmonDir = dir;
            else if (name == "asus_custom_fan_curve" && FanCurveHwmonDir is null) FanCurveHwmonDir = dir;
        }
    }

    private void ResolveBattery()
    {
        BatteryDir = null;
        foreach (string name in BatteryNames)
        {
            string dir = "/sys/class/power_supply/" + name;
            if (root.FileExists(dir + "/charge_control_end_threshold"))
            {
                BatteryDir = dir;
                return;
            }
        }
    }

    private void ResolveLeds()
    {
        KeyboardLedDir = root.DirectoryExists("/sys/class/leds/asus::kbd_backlight")
            ? "/sys/class/leds/asus::kbd_backlight"
            : root.ListDirectories("/sys/class/leds")
                .FirstOrDefault(d => Path.GetFileName(d).StartsWith("asus", StringComparison.Ordinal)
                                     && Path.GetFileName(d).Contains("kbd_backlight", StringComparison.Ordinal));

        ScreenpadBacklightDir = root.DirectoryExists("/sys/class/backlight/asus_screenpad")
            ? "/sys/class/backlight/asus_screenpad"
            : null;
    }

    private void ResolveDebugfs()
    {
        DebugfsDir = null;
        foreach (string name in new[] { "asus-nb-wmi", "eeepc-wmi", "asus-wmi" })
        {
            string dir = "/sys/kernel/debug/" + name;
            if (root.FileExists(dir + "/dev_id") && root.FileExists(dir + "/dsts"))
            {
                DebugfsDir = dir;
                return;
            }
        }
    }

    /// <summary>First existing file among the candidates (null when none).</summary>
    public string? FirstFile(params string?[] candidates)
        => candidates.FirstOrDefault(c => c is not null && root.FileExists(c));

    /// <summary>asus-armoury attribute file ("current_value" by default) or null.</summary>
    public string? Armoury(string attribute, string file = "current_value")
        => ArmouryDir is null ? null : FirstFile(ArmouryDir + "/" + attribute + "/" + file);

    /// <summary>Attribute directly in the platform device directory (pre armoury interface).</summary>
    public string? Platform(string attribute)
        => PlatformDir is null ? null : FirstFile(PlatformDir + "/" + attribute);
}
