using System.Text;
using System.Text.RegularExpressions;

namespace GHelper.Platform.Linux;

/// <summary>
/// IPowerService for Linux.
///   profiles : power-profiles-daemon (powerprofilesctl / busctl), platform_profile as fallback
///   events   : logind (PrepareForSleep, Lock/Unlock, LidClosed) through "gdbus monitor", power_supply polling,
///              gsettings monitor for theme changes
///   tunables : cpufreq boost, PCIe ASPM policy, logind lid action / sleep.conf (root only)
/// Every setter silently does nothing when the kernel/daemon does not offer the knob.
/// </summary>
public sealed class LinuxPowerService : IPowerService, IPowerModeCatalog, ISessionEndingEvents, IDisposable
{
    public const string PowerSaver = "power-saver";
    public const string Balanced = "balanced";
    public const string Performance = "performance";

    private const string PpdBus1 = "net.hadess.PowerProfiles";
    private const string PpdPath1 = "/net/hadess/PowerProfiles";
    private const string PpdBus2 = "org.freedesktop.UPower.PowerProfiles";
    private const string PpdPath2 = "/org/freedesktop/UPower/PowerProfiles";

    private readonly LinuxContext ctx;
    private readonly SysfsRoot root;
    private readonly AsusWmiLayout layout;
    private readonly object gate = new();
    private readonly List<IDisposable> monitors = new();
    private Timer? pollTimer;
    private bool monitoring;
    private PowerSource lastSource = PowerSource.Unknown;
    private bool? lastLid;

    private Action<PowerSource>? powerSourceChanged;
    private Action? suspending;
    private Action? resumed;
    private Action<bool>? lidChanged;
    private Action<bool>? displayStateChanged;
    private Action<bool>? sessionLockChanged;
    private Action<bool>? batterySaverChanged;
    private Action? userPreferencesChanged;
    private Action? sessionEnding;

    private readonly int pollIntervalMs;

    /// <param name="pollIntervalMs">power source / lid polling period (0 disables, tests drive events directly).</param>
    public LinuxPowerService(LinuxContext ctx, AsusWmiLayout? layout = null, int pollIntervalMs = 2000)
    {
        this.ctx = ctx;
        root = ctx.Root;
        this.layout = layout ?? new AsusWmiLayout(root);
        this.pollIntervalMs = pollIntervalMs;
    }

    // ------------------------------------------------------------------ profiles

    public static string? NormalizeProfile(string? name)
    {
        return name?.Trim().ToLowerInvariant() switch
        {
            "power-saver" or "quiet" or "low-power" or "cool" or "silent" => PowerSaver,
            "balanced" or "default" => Balanced,
            "performance" or "balanced-performance" or "max-power" or "turbo" => Performance,
            _ => null,
        };
    }

    public static string ProfileForMode(int mode) => mode switch
    {
        1 => Performance,
        2 => PowerSaver,
        _ => Balanced,
    };

    /// <summary>The profiles selectable per performance mode (the names <see cref="GetPowerMode"/> returns).</summary>
    public IReadOnlyDictionary<string, string> PowerModes { get; } = new Dictionary<string, string>
    {
        { PowerSaver, "Power Saver" },
        { Balanced, "Balanced" },
        { Performance, "Performance" },
    };

    public string GetPowerMode()
    {
        string? ppd = PpdGet();
        if (ppd is not null) return NormalizeProfile(ppd) ?? ppd;

        if (layout.ProfileFile is { } file && NormalizeProfile(root.ReadText(file)) is { } normalized)
            return normalized;

        return Balanced;
    }

    public void SetPowerMode(string scheme)
    {
        string? target = NormalizeProfile(scheme);
        if (target is null)
        {
            LinuxLog.Write($"Unknown power profile '{scheme}'");
            return;
        }

        if (PpdSet(target)) return;

        // no daemon: drive the kernel platform profile directly
        if (layout.ProfileFile is not { } file) return;
        string[] choices = (root.ReadText(layout.ProfileChoicesFile ?? "") ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string[] candidates = target switch
        {
            PowerSaver => ["low-power", "quiet", "cool"],
            Performance => ["performance", "balanced-performance", "max-power"],
            _ => ["balanced"],
        };

        foreach (string candidate in candidates)
        {
            if (choices.Length == 0 || choices.Contains(candidate))
            {
                root.TryWrite(file, candidate);
                return;
            }
        }
    }

    public void SetPowerMode(int mode) => SetPowerMode(GetDefaultPowerMode(mode));

    public string GetDefaultPowerMode(int mode) => ProfileForMode(mode);

    public void SetPowerPlan(string scheme) => SetPowerMode(scheme);

    private string? PpdGet()
    {
        if (ctx.Runner.CommandExists("powerprofilesctl"))
        {
            CommandResult r = ctx.Runner.Run("powerprofilesctl", ["get"], 3000);
            if (r.Success && r.StdOut.Trim().Length > 0) return r.StdOut.Trim();
        }

        foreach (var (bus, path) in new[] { (PpdBus1, PpdPath1), (PpdBus2, PpdPath2) })
        {
            CommandResult r = ctx.Runner.Run("busctl", ["--system", "get-property", bus, path, bus, "ActiveProfile"], 3000);
            if (!r.Success) continue;
            Match m = Regex.Match(r.StdOut, "\"([^\"]+)\"");
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }

    private bool PpdSet(string profile)
    {
        if (ctx.Runner.CommandExists("powerprofilesctl"))
        {
            if (ctx.Runner.Run("powerprofilesctl", ["set", profile], 5000).Success) return true;
        }

        foreach (var (bus, path) in new[] { (PpdBus1, PpdPath1), (PpdBus2, PpdPath2) })
        {
            if (ctx.Runner.Run("busctl", ["--system", "set-property", bus, path, bus, "ActiveProfile", "s", profile], 5000).Success)
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ CPU boost / ASPM

    private const string IntelNoTurbo = "/sys/devices/system/cpu/intel_pstate/no_turbo";
    private const string CpufreqBoost = "/sys/devices/system/cpu/cpufreq/boost";

    /// <summary>0 = disabled, 1 = enabled, -1 = not controllable.</summary>
    public int GetCPUBoost()
    {
        if (root.ReadInt(IntelNoTurbo) is int noTurbo) return noTurbo == 0 ? 1 : 0;
        if (root.ReadInt(CpufreqBoost) is int boost) return boost != 0 ? 1 : 0;
        return -1;
    }

    public void SetCPUBoost(int boost = 0)
    {
        bool enable = boost > 0;
        if (root.FileExists(IntelNoTurbo))
        {
            if (GetCPUBoost() == (enable ? 1 : 0)) return;
            root.TryWrite(IntelNoTurbo, enable ? "0" : "1");
        }
        else if (root.FileExists(CpufreqBoost))
        {
            if (GetCPUBoost() == (enable ? 1 : 0)) return;
            root.TryWrite(CpufreqBoost, enable ? "1" : "0");
        }
        else return;

        LinuxLog.Write("Boost " + boost);
    }

    private const string AspmPolicy = "/sys/module/pcie_aspm/parameters/policy";

    /// <summary>0 = off (performance), 1 = BIOS default, 2 = powersave / powersupersave, -1 unsupported.</summary>
    public int GetASPM()
    {
        string? text = root.ReadText(AspmPolicy);
        if (text is null) return -1;

        Match m = Regex.Match(text, @"\[([a-z]+)\]");
        return (m.Success ? m.Groups[1].Value : text) switch
        {
            "performance" => 0,
            "default" => 1,
            "powersave" or "powersupersave" => 2,
            _ => -1,
        };
    }

    public void SetASPM(int status = 0)
    {
        if (!root.FileExists(AspmPolicy)) return;
        int current = GetASPM();
        if (current == status) return;

        string policy = status switch { <= 0 => "performance", 1 => "default", _ => "powersave" };
        if (root.TryWrite(AspmPolicy, policy)) LinuxLog.Write($"Changed ASPM {current} -> {status}");
    }

    public void SetBalancedASPM(int status = 0)
    {
        if (GetPowerMode() != Balanced) return;
        SetASPM(status);
    }

    public void SetConnectivityInStandby(int ac = 0, int dc = 0)
    {
        // no equivalent: network wake / standby networking is a NetworkManager / driver policy on Linux
    }

    // ------------------------------------------------------------------ lid / sleep

    /// <summary>Windows numbering: 0 nothing, 1 sleep, 2 hibernate, 3 shutdown.</summary>
    public int GetLidAction(bool ac)
    {
        string? value = null;
        if (ac) value = SystemdConf.Read(root, "logind.conf", "Login", "HandleLidSwitchExternalPower");
        value ??= SystemdConf.Read(root, "logind.conf", "Login", "HandleLidSwitch");
        return LidActionFromLogind(value);
    }

    public static int LidActionFromLogind(string? value) => (value ?? "suspend").ToLowerInvariant() switch
    {
        "ignore" or "lock" => 0,
        "suspend" or "suspend-then-hibernate" or "hybrid-sleep" => 1,
        "hibernate" => 2,
        "poweroff" or "halt" or "kexec" or "reboot" => 3,
        _ => 1,
    };

    public static string LogindFromLidAction(int action) => action switch
    {
        0 => "ignore",
        2 => "hibernate",
        3 => "poweroff",
        _ => "suspend",
    };

    public void SetLidAction(int action, bool acOnly = false)
    {
        if (!ctx.IsRoot)
        {
            LinuxLog.Write("Changing the lid action needs root (logind.conf.d drop-in)");
            return;
        }

        string logind = LogindFromLidAction(action);
        var sb = new StringBuilder("[Login]\n");
        sb.Append("HandleLidSwitchExternalPower=").Append(logind).Append('\n');
        if (!acOnly)
        {
            sb.Append("HandleLidSwitch=").Append(logind).Append('\n');
            sb.Append("HandleLidSwitchDocked=").Append(logind).Append('\n');
        }

        if (root.TryCreateFile("/etc/systemd/logind.conf.d/50-ghelper-lid.conf", sb.ToString()))
        {
            // logind re-reads its configuration on SIGHUP without dropping the sessions
            ctx.Runner.Run("systemctl", ["kill", "-s", "HUP", "systemd-logind.service"], 5000);
            LinuxLog.Write("Changed Lid Action to " + action);
        }
    }

    public int GetHibernateAfter()
    {
        string? text = SystemdConf.Read(root, "sleep.conf", "Sleep", "HibernateDelaySec");
        if (text is null) return 120; // systemd default 2h
        long? seconds = SysfsRoot.ParseLong(Regex.Replace(text, @"[^0-9].*$", ""));
        return seconds is null ? 120 : (int)(seconds.Value / 60);
    }

    public void SetHibernateAfter(int minutes)
    {
        if (!ctx.IsRoot)
        {
            LinuxLog.Write("Changing the hibernate delay needs root (sleep.conf.d drop-in)");
            return;
        }

        const string file = "/etc/systemd/sleep.conf.d/50-ghelper-hibernate.conf";
        if (minutes <= 0)
        {
            root.TryDelete(file);
            return;
        }

        root.TryCreateFile(file, $"[Sleep]\nHibernateDelaySec={minutes * 60}\n");
    }

    /// <summary>
    /// Always false: the closest Linux concept is the power-saver profile, but the ASUS "Silent" mode selects that
    /// profile itself and must not be read back as an OS imposed battery saver.
    /// </summary>
    public bool GetBatterySaverStatus() => false;

    // ------------------------------------------------------------------ state

    public PowerSource GetPowerSource()
    {
        bool? mains = PowerSupplyInfo.IsOnMains(root);
        if (mains is null)
        {
            // no mains supply reported: a machine without battery is always on AC
            return PowerSupplyInfo.FindBattery(root) is null ? PowerSource.AC : PowerSource.Unknown;
        }
        return mains.Value ? PowerSource.AC : PowerSource.Battery;
    }

    public bool IsLidClosed()
    {
        foreach (string dir in root.ListDirectories("/proc/acpi/button/lid"))
        {
            string? state = root.ReadText(dir + "/state");
            if (state is not null) return state.Contains("closed", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public void Sleep()
    {
        if (!ctx.Runner.Run("systemctl", ["suspend"], 10000).Success)
            ctx.Runner.Run("loginctl", ["suspend"], 10000);
    }

    public void LockWorkstation()
    {
        if (!ctx.Runner.Run("loginctl", ["lock-session"], 5000).Success)
            ctx.Runner.Run("xdg-screensaver", ["lock"], 5000);
    }

    // ------------------------------------------------------------------ events

    public event Action<PowerSource>? PowerSourceChanged
    {
        add { powerSourceChanged += value; EnsureMonitoring(); }
        remove => powerSourceChanged -= value;
    }

    public event Action? Suspending
    {
        add { suspending += value; EnsureMonitoring(); }
        remove => suspending -= value;
    }

    public event Action? Resumed
    {
        add { resumed += value; EnsureMonitoring(); }
        remove => resumed -= value;
    }

    public event Action<bool>? LidChanged
    {
        add { lidChanged += value; EnsureMonitoring(); }
        remove => lidChanged -= value;
    }

    public event Action<bool>? DisplayStateChanged
    {
        add { displayStateChanged += value; EnsureMonitoring(); }
        remove => displayStateChanged -= value;
    }

    public event Action<bool>? SessionLockChanged
    {
        add { sessionLockChanged += value; EnsureMonitoring(); }
        remove => sessionLockChanged -= value;
    }

    public event Action<bool>? BatterySaverChanged
    {
        add { batterySaverChanged += value; }
        remove => batterySaverChanged -= value;
    }

    public event Action? UserPreferencesChanged
    {
        add { userPreferencesChanged += value; EnsureMonitoring(); }
        remove => userPreferencesChanged -= value;
    }

    /// <summary>logind PrepareForShutdown(true): the machine is going down (reboot / poweroff).</summary>
    public event Action? SessionEnding
    {
        add { sessionEnding += value; EnsureMonitoring(); }
        remove => sessionEnding -= value;
    }

    private void EnsureMonitoring()
    {
        lock (gate)
        {
            if (monitoring) return;
            monitoring = true;
        }

        lastSource = GetPowerSource();
        lastLid = IsLidClosed();
        if (pollIntervalMs > 0)
            pollTimer = new Timer(_ => Poll(), null, pollIntervalMs, pollIntervalMs);

        if (ctx.Runner.CommandExists("gdbus"))
        {
            AddMonitor(ctx.Runner.StartLineStream("gdbus", ["monitor", "--system", "--dest", "org.freedesktop.login1"], OnLogindLine));
            AddMonitor(ctx.Runner.StartLineStream("gdbus", ["monitor", "--system", "--dest", "org.freedesktop.UPower"], OnUpowerLine));
        }

        if (ctx.Runner.CommandExists("gsettings"))
            AddMonitor(ctx.Runner.StartLineStream("gsettings", ["monitor", "org.gnome.desktop.interface"], _ => userPreferencesChanged?.Invoke()));
    }

    private void AddMonitor(IDisposable? monitor)
    {
        if (monitor is not null) lock (monitors) monitors.Add(monitor);
    }

    private void Poll()
    {
        try
        {
            PowerSource source = GetPowerSource();
            if (source != PowerSource.Unknown && source != lastSource)
            {
                lastSource = source;
                powerSourceChanged?.Invoke(source);
            }

            bool lid = IsLidClosed();
            if (lastLid != lid)
            {
                lastLid = lid;
                lidChanged?.Invoke(lid);
            }
        }
        catch (Exception ex)
        {
            LinuxLog.Write("power poll: " + ex.Message);
        }
    }

    internal void OnLogindLine(string line)
    {
        DbusSignal? parsed = DbusSignal.Parse(line);
        if (parsed is not { } s) return;

        switch (s.Member)
        {
            case "PrepareForSleep":
                if (s.FirstBool == true) suspending?.Invoke();
                else if (s.FirstBool == false) resumed?.Invoke();
                break;
            case "PrepareForShutdown":
                if (s.FirstBool == true) sessionEnding?.Invoke();
                break;
            case "Lock":
                sessionLockChanged?.Invoke(true);
                break;
            case "Unlock":
                sessionLockChanged?.Invoke(false);
                break;
            case "PropertiesChanged":
                if (s.PropertyBool("LidClosed") is bool lid && lastLid != lid)
                {
                    lastLid = lid;
                    lidChanged?.Invoke(lid);
                }
                if (s.PropertyBool("LockedHint") is bool locked)
                    sessionLockChanged?.Invoke(locked);
                break;
        }
    }

    internal void OnUpowerLine(string line)
    {
        DbusSignal? parsed = DbusSignal.Parse(line);
        if (parsed is not { } s || s.Member != "PropertiesChanged") return;

        bool? onBattery = s.PropertyBool("OnBattery");
        if (onBattery is bool battery)
        {
            PowerSource source = battery ? PowerSource.Battery : PowerSource.AC;
            if (source != lastSource)
            {
                lastSource = source;
                powerSourceChanged?.Invoke(source);
            }
        }

        if (s.PropertyBool("LidIsClosed") is bool lid && lastLid != lid)
        {
            lastLid = lid;
            lidChanged?.Invoke(lid);
        }
    }

    public void Dispose()
    {
        pollTimer?.Dispose();
        lock (monitors)
        {
            foreach (IDisposable monitor in monitors) monitor.Dispose();
            monitors.Clear();
        }
    }
}
