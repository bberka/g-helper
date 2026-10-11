namespace GHelper.Platform.Linux;

/// <summary>
/// Everything the Linux services share: the filesystem root, the command runner and the process environment.
/// Constructed once by <see cref="LinuxPlatform"/>; tests build one over a fake tree.
/// </summary>
public sealed class LinuxContext
{
    public SysfsRoot Root { get; }
    public ICommandRunner Runner { get; }

    private readonly Func<string, string?> getEnv;
    private readonly Func<uint> getEuid;

    public LinuxContext(SysfsRoot? root = null, ICommandRunner? runner = null,
        Func<string, string?>? getEnv = null, Func<uint>? getEuid = null, string? processPath = null)
    {
        Root = root ?? SysfsRoot.Default;
        Runner = runner ?? new ProcessCommandRunner();
        this.getEnv = getEnv ?? Environment.GetEnvironmentVariable;
        this.getEuid = getEuid ?? Native.SafeEuid;
        ProcessPath = processPath ?? Environment.ProcessPath ?? "";
    }

    /// <summary>Path of the executable to relaunch (autostart entry, elevation).</summary>
    public string ProcessPath { get; }

    public string? Env(string name)
    {
        string? value = getEnv(name);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public uint Euid => getEuid();

    public bool IsRoot => Euid == 0;

    /// <summary>uid of the human behind a pkexec/sudo elevated process, null when not elevated that way.</summary>
    public uint? InvokingUid
    {
        get
        {
            if (!IsRoot) return null;
            foreach (string name in new[] { "PKEXEC_UID", "SUDO_UID" })
                if (uint.TryParse(Env(name), out uint uid) && uid != 0) return uid;
            return null;
        }
    }

    /// <summary>passwd lookup: (name, home) for a uid, null when unknown.</summary>
    public (string Name, string Home)? LookupUser(uint uid)
    {
        string[]? lines = Root.ReadLines("/etc/passwd");
        if (lines is null) return null;
        foreach (string line in lines)
        {
            string[] f = line.Split(':');
            if (f.Length >= 6 && uint.TryParse(f[2], out uint id) && id == uid) return (f[0], f[5]);
        }
        return null;
    }

    /// <summary>Home of the interactive user (the invoking user when elevated through pkexec/sudo).</summary>
    public string HomeDirectory
    {
        get
        {
            if (InvokingUid is uint uid && LookupUser(uid) is { } user && !string.IsNullOrEmpty(user.Home))
                return user.Home;

            string? home = Env("HOME");
            if (home is not null) return home;
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }

    /// <summary>$XDG_CONFIG_HOME or ~/.config of the interactive user.</summary>
    public string XdgConfigHome
    {
        get
        {
            // when elevated through pkexec the variable (if any) belongs to the user's session, HOME to root
            string? xdg = Env("XDG_CONFIG_HOME");
            if (xdg is not null && (InvokingUid is null || !xdg.StartsWith("/root"))) return xdg;
            return SysfsRoot.Combine(HomeDirectory, ".config");
        }
    }

    public string XdgDataHome
    {
        get
        {
            string? xdg = Env("XDG_DATA_HOME");
            if (xdg is not null && (InvokingUid is null || !xdg.StartsWith("/root"))) return xdg;
            return SysfsRoot.Combine(HomeDirectory, ".local/share");
        }
    }

    public string RuntimeDirectory
    {
        get
        {
            string? runtime = Env("XDG_RUNTIME_DIR");
            if (runtime is not null) return runtime;
            uint? uid = InvokingUid ?? (Euid == uint.MaxValue ? null : Euid);
            string candidate = $"/run/user/{uid ?? 0}";
            return Directory.Exists(Root.Map(candidate)) ? candidate : Path.GetTempPath().TrimEnd('/');
        }
    }

    public bool IsWaylandSession =>
        Env("WAYLAND_DISPLAY") is not null || string.Equals(Env("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);

    public bool IsX11Session => Env("DISPLAY") is not null;

    public string CurrentDesktop => (Env("XDG_CURRENT_DESKTOP") ?? "").ToUpperInvariant();

    /// <summary>Environment variables a relaunched GUI process needs to reach the user's display and session bus.</summary>
    public Dictionary<string, string> SessionEnvironment()
    {
        var result = new Dictionary<string, string>();
        foreach (string name in new[]
                 {
                     "DISPLAY", "XAUTHORITY", "WAYLAND_DISPLAY", "XDG_RUNTIME_DIR", "DBUS_SESSION_BUS_ADDRESS",
                     "XDG_SESSION_TYPE", "XDG_CURRENT_DESKTOP", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "HOME", "LANG"
                 })
        {
            string? value = Env(name);
            if (value is not null) result[name] = value;
        }
        return result;
    }
}
