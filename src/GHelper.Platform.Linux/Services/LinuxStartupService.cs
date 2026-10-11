using System.Text;

namespace GHelper.Platform.Linux;

/// <summary>
/// Run at login: XDG autostart entry (~/.config/autostart/ghelper.desktop), optionally also a systemd user unit
/// (~/.config/systemd/user/ghelper.service). The legacy "charge" boot task becomes the system unit
/// ghelper-charge.service (written/enabled only when running as root).
/// </summary>
public sealed class LinuxStartupService : IStartupService
{
    public const string DesktopFileName = "ghelper.desktop";
    public const string UserUnitName = "ghelper.service";
    public const string ChargeUnitName = "ghelper-charge.service";
    public const string ChargeUnitPath = "/etc/systemd/system/" + ChargeUnitName;

    private readonly LinuxContext ctx;
    private readonly bool useSystemdUserUnit;

    public LinuxStartupService(LinuxContext ctx, bool? useSystemdUserUnit = null)
    {
        this.ctx = ctx;
        this.useSystemdUserUnit = useSystemdUserUnit
            ?? string.Equals(ctx.Env("GHELPER_STARTUP"), "systemd", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsSupported => true;

    public string DesktopFilePath => SysfsRoot.Combine(ctx.XdgConfigHome, "autostart/" + DesktopFileName);
    public string UserUnitPath => SysfsRoot.Combine(ctx.XdgConfigHome, "systemd/user/" + UserUnitName);

    // ------------------------------------------------------------------ content

    internal static string QuoteExec(string argument)
    {
        // Desktop Entry spec: quote with "..." and escape " ` $ \ ; a literal % is %%
        var sb = new StringBuilder("\"");
        foreach (char c in argument)
        {
            if (c is '"' or '`' or '$' or '\\') sb.Append('\\');
            if (c == '%') sb.Append('%');
            sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    internal string BuildDesktopEntry(IEnumerable<string>? extraArgs = null)
    {
        var tokens = ctx.LaunchCommand.Concat(extraArgs ?? []).Select(QuoteExec);
        return string.Join('\n',
            "[Desktop Entry]",
            "Type=Application",
            "Name=G-Helper",
            "Comment=Control utility for ASUS laptops",
            "Exec=" + string.Join(' ', tokens),
            "Icon=ghelper",
            "Terminal=false",
            "Categories=Utility;System;",
            "StartupNotify=false",
            "X-GNOME-Autostart-enabled=true",
            "X-GNOME-Autostart-Delay=3",
            "") ;
    }

    internal string BuildUserUnit()
    {
        string exec = string.Join(' ', ctx.LaunchCommand.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a));
        return string.Join('\n',
            "[Unit]",
            "Description=G-Helper ASUS laptop control",
            "After=graphical-session.target",
            "PartOf=graphical-session.target",
            "",
            "[Service]",
            "ExecStart=" + exec,
            "Restart=on-failure",
            "RestartSec=3",
            "",
            "[Install]",
            "WantedBy=graphical-session.target",
            "");
    }

    internal string BuildChargeUnit()
    {
        string exec = string.Join(' ', ctx.LaunchCommand.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a));
        return string.Join('\n',
            "[Unit]",
            "Description=G-Helper battery charge limit",
            "After=multi-user.target",
            "",
            "[Service]",
            "Type=oneshot",
            "ExecStart=" + exec + " charge",
            "",
            "[Install]",
            "WantedBy=multi-user.target",
            "");
    }

    /// <summary>Executable path of the Exec= line of an existing autostart entry.</summary>
    internal static string? ParseExecTarget(string? desktopFile)
    {
        if (desktopFile is null) return null;
        foreach (string raw in desktopFile.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith("Exec=", StringComparison.Ordinal)) continue;

            string value = line[5..].Trim();
            if (value.StartsWith('"'))
            {
                var sb = new StringBuilder();
                for (int i = 1; i < value.Length; i++)
                {
                    char c = value[i];
                    if (c == '\\' && i + 1 < value.Length) { sb.Append(value[++i]); continue; }
                    if (c == '"') break;
                    sb.Append(c);
                }
                return sb.ToString();
            }
            return value.Split(' ', 2)[0];
        }
        return null;
    }

    // ------------------------------------------------------------------ IStartupService

    public bool IsScheduled()
    {
        if (ctx.Root.FileExists(DesktopFilePath))
        {
            string? text = ctx.Root.ReadText(DesktopFilePath);
            if (text is null || !text.Contains("Hidden=true", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return ctx.Root.FileExists(SysfsRoot.Combine(ctx.XdgConfigHome,
            "systemd/user/graphical-session.target.wants/" + UserUnitName));
    }

    public void Schedule()
    {
        if (string.IsNullOrEmpty(ctx.ProcessPath)) return;

        if (ctx.Root.TryCreateFile(DesktopFilePath, BuildDesktopEntry()))
            ctx.FixOwnership(DesktopFilePath);

        if (useSystemdUserUnit && ctx.Root.TryCreateFile(UserUnitPath, BuildUserUnit()))
        {
            ctx.FixOwnership(UserUnitPath);
            RunUserSystemctl("daemon-reload");
            RunUserSystemctl("enable", UserUnitName);
        }
    }

    public void UnSchedule()
    {
        ctx.Root.TryDelete(DesktopFilePath);

        if (ctx.Root.FileExists(UserUnitPath) || useSystemdUserUnit)
        {
            RunUserSystemctl("disable", UserUnitName);
            ctx.Root.TryDelete(UserUnitPath);
        }
    }

    public void ReScheduleAdmin()
    {
        // no scheduled-task run level on Linux: elevation happens on demand through pkexec
    }

    public void StartupCheck()
    {
        if (!ctx.Root.FileExists(DesktopFilePath)) return;
        if (string.IsNullOrEmpty(ctx.ProcessPath)) return;

        string? target = ParseExecTarget(ctx.Root.ReadText(DesktopFilePath));
        string expected = ctx.LaunchCommand[0];
        if (target is null || string.Equals(target, expected, StringComparison.Ordinal)) return;

        // only repair entries pointing to a file that no longer exists (moved / uninstalled build)
        bool missing = !ctx.Root.FileExists(target);
        if (missing)
        {
            LinuxLog.Write($"Rescheduling autostart: {target} -> {expected}");
            UnSchedule();
            Schedule();
        }
    }

    public void ScheduleCharge()
    {
        if (string.IsNullOrEmpty(ctx.ProcessPath)) return;
        if (!ctx.IsRoot)
        {
            // needs a system unit: packaging installs ghelper-charge.service, enabling it needs root
            return;
        }

        if (ctx.Root.TryCreateFile(ChargeUnitPath, BuildChargeUnit()))
        {
            ctx.Runner.Run("systemctl", ["daemon-reload"], 10000);
            ctx.Runner.Run("systemctl", ["enable", ChargeUnitName], 10000);
        }
    }

    public void UnscheduleCharge()
    {
        if (!ctx.IsRoot) return;
        ctx.Runner.Run("systemctl", ["disable", ChargeUnitName], 10000);
        ctx.Root.TryDelete(ChargeUnitPath);
    }

    private void RunUserSystemctl(params string[] args)
    {
        var all = new List<string> { "--user" };
        all.AddRange(args);
        ctx.Runner.Run("systemctl", all, 8000, environment: ctx.SessionEnvironment());
    }
}
