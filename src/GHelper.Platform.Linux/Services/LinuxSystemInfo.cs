using System.Text.RegularExpressions;

namespace GHelper.Platform.Linux;

/// <summary>DMI (/sys/class/dmi/id), os-release, cpuinfo and desktop theme based machine description.</summary>
public sealed class LinuxSystemInfo : ISystemInfo
{
    private const string Dmi = "/sys/class/dmi/id";

    private static readonly HashSet<int> DesktopChassis = [3, 4, 5, 6, 7, 13, 15, 16, 17, 23, 24, 35, 36];

    private readonly LinuxContext ctx;
    private readonly SysfsRoot root;
    private readonly Lazy<string> configDirectory;
    private readonly Func<LinuxDisplayService?>? display;

    public LinuxSystemInfo(LinuxContext ctx, Func<LinuxDisplayService?>? display = null)
    {
        this.ctx = ctx;
        root = ctx.Root;
        this.display = display;
        configDirectory = new Lazy<string>(PrepareConfigDirectory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public PlatformKind Kind => PlatformKind.Linux;

    public string OsDescription
    {
        get
        {
            string name = "Linux";
            string[]? lines = root.ReadLines("/etc/os-release");
            if (lines is not null)
            {
                string? pretty = lines.FirstOrDefault(l => l.StartsWith("PRETTY_NAME=", StringComparison.Ordinal));
                if (pretty is not null) name = pretty["PRETTY_NAME=".Length..].Trim('"');
            }
            string? kernel = root.ReadText("/proc/sys/kernel/osrelease");
            return kernel is null ? name : $"{name} (kernel {kernel})";
        }
    }

    public string ConfigDirectory => configDirectory.Value;

    private string PrepareConfigDirectory()
    {
        string dir = SysfsRoot.Combine(ctx.XdgConfigHome, "ghelper");
        try
        {
            Directory.CreateDirectory(root.Map(dir));
            if (ctx.InvokingUid is not null)
            {
                // running through pkexec: keep the user's own config readable/writable for the non elevated instance
                ctx.FixOwnership(ctx.XdgConfigHome);
                ctx.FixOwnership(dir);
                foreach (string file in root.ListFiles(dir)) ctx.FixOwnership(file);
            }
        }
        catch (Exception ex)
        {
            LinuxLog.Write("Config directory: " + ex.Message);
        }
        return root.Map(dir);
    }

    public string SharedConfigDirectory => root.Map("/etc/ghelper");

    private string? Dmi_(string file)
    {
        string? text = root.ReadText($"{Dmi}/{file}");
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public string GetModel() => Dmi_("product_name") ?? Dmi_("board_name") ?? string.Empty;

    public (string Bios, string ModelShort) GetBiosAndModel()
    {
        string raw = Dmi_("bios_version") ?? string.Empty;
        string[] parts = raw.Split('.');
        // "GA402RJ.320" -> (version "320", model "GA402RJ"), same as the Windows Win32_BIOS parsing
        return parts.Length > 1 ? (parts[1], parts[0]) : (string.Empty, raw);
    }

    public string GetSerialNumber()
        => Dmi_("product_serial") ?? Dmi_("board_serial") ?? Dmi_("chassis_serial") ?? string.Empty;

    public string? GetCpuName() => CpuIdentity.Read(root)?.ModelName is { Length: > 0 } name ? name : null;

    public bool IsElevated => ctx.IsRoot;

    public bool IsRunningAsSystem => false;

    public bool IsDesktop
    {
        get
        {
            int? chassis = root.ReadInt($"{Dmi}/chassis_type");
            return chassis is int c && DesktopChassis.Contains(c);
        }
    }

    public bool? IsOledPanel()
    {
        string? panel = display?.Invoke()?.GetInternalPanelName();
        return Edid.LooksLikeOled(panel) ? true : null;
    }

    // ------------------------------------------------------------------ theme

    public bool? IsDarkTheme()
    {
        var env = ctx.SessionEnvironment();

        string? scheme = Gsettings("org.gnome.desktop.interface", "color-scheme", env);
        if (scheme is not null)
        {
            if (scheme.Contains("prefer-dark", StringComparison.Ordinal)) return true;
            if (scheme.Contains("prefer-light", StringComparison.Ordinal)) return false;
        }

        string? theme = Gsettings("org.gnome.desktop.interface", "gtk-theme", env);
        if (theme is not null && theme.Contains("dark", StringComparison.OrdinalIgnoreCase)) return true;

        // KDE Plasma
        string[]? kdeglobals = root.ReadLines(SysfsRoot.Combine(ctx.XdgConfigHome, "kdeglobals"));
        if (kdeglobals is not null)
        {
            string? schemeLine = kdeglobals.FirstOrDefault(l => l.StartsWith("ColorScheme=", StringComparison.Ordinal));
            if (schemeLine is not null) return schemeLine.Contains("dark", StringComparison.OrdinalIgnoreCase);
        }

        string? gtk = ctx.Env("GTK_THEME");
        if (gtk is not null) return gtk.Contains("dark", StringComparison.OrdinalIgnoreCase);

        if (theme is not null) return false;
        return scheme is not null ? false : null;
    }

    private string? Gsettings(string schema, string key, Dictionary<string, string> env)
    {
        if (!ctx.Runner.CommandExists("gsettings")) return null;
        CommandResult r = ctx.Runner.Run("gsettings", ["get", schema, key], 2000, environment: env);
        if (!r.Success) return null;
        string value = r.StdOut.Trim().Trim('\'');
        return Regex.IsMatch(value, "^[A-Za-z0-9_.:-]*$") ? value : null;
    }
}
