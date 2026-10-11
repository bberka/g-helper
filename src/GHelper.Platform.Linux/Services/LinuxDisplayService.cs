using System.Globalization;
using System.Text.RegularExpressions;

namespace GHelper.Platform.Linux;

/// <summary>
/// IDisplayService for Linux.
///   internal panel / external displays : DRM connectors in /sys/class/drm
///   brightness                         : /sys/class/backlight (logind SetBrightness when not writable)
///   refresh rate                       : xrandr (X11), wlr-randr (wlroots), kscreen-doctor (KDE), Mutter DBus (GNOME)
///   gamma                              : xrandr --gamma (X11 only, exponent approximation of the ramp)
/// </summary>
public sealed partial class LinuxDisplayService : IDisplayService
{
    private const string DrmDir = "/sys/class/drm";
    private const string BacklightDir = "/sys/class/backlight";

    private readonly LinuxContext ctx;
    private readonly SysfsRoot root;
    private readonly IRefreshRateBackend[] backends;
    private string? cachedBackendName;

    public LinuxDisplayService(LinuxContext ctx)
    {
        this.ctx = ctx;
        root = ctx.Root;
        backends = [new MutterBackend(ctx), new KscreenBackend(ctx), new WlrRandrBackend(ctx), new XrandrBackend(ctx)];
    }

    // ------------------------------------------------------------------ connectors

    internal readonly record struct Connector(string Name, string SysfsDir, bool Connected, bool IsInternal);

    [GeneratedRegex(@"^card\d+-(?<name>.+)$")]
    private static partial Regex ConnectorDir();

    internal static bool IsInternalConnector(string name)
    {
        string upper = name.ToUpperInvariant();
        return upper.StartsWith("EDP", StringComparison.Ordinal)
               || upper.StartsWith("LVDS", StringComparison.Ordinal)
               || upper.StartsWith("DSI", StringComparison.Ordinal);
    }

    internal List<Connector> EnumerateConnectors()
    {
        var list = new List<Connector>();
        foreach (string dir in root.ListDirectories(DrmDir))
        {
            Match m = ConnectorDir().Match(Path.GetFileName(dir));
            if (!m.Success) continue;
            string name = m.Groups["name"].Value;
            if (name.StartsWith("Writeback", StringComparison.OrdinalIgnoreCase)) continue;
            bool connected = string.Equals(root.ReadText(dir + "/status"), "connected", StringComparison.OrdinalIgnoreCase);
            list.Add(new Connector(name, dir, connected, IsInternalConnector(name)));
        }
        return list;
    }

    public bool IsExternalDisplayConnected(bool log = false)
    {
        foreach (Connector c in EnumerateConnectors())
        {
            if (c.IsInternal || !c.Connected) continue;
            if (log) LinuxLog.Write("Found external screen: " + c.Name);
            return true;
        }
        return false;
    }

    public string? FindLaptopScreen(bool log = false)
    {
        List<Connector> all = EnumerateConnectors();
        Connector? panel = all.Where(c => c.IsInternal && c.Connected).Cast<Connector?>().FirstOrDefault()
                           ?? all.Where(c => c.IsInternal).Cast<Connector?>().FirstOrDefault();
        if (panel is null)
        {
            if (log) LinuxLog.Write("Internal screen not found");
            return null;
        }
        if (log) LinuxLog.Write("Internal screen: " + panel.Value.Name);
        return panel.Value.Name;
    }

    /// <summary>EDID monitor name of the internal panel (OLED detection, diagnostics).</summary>
    public string? GetInternalPanelName()
    {
        string? screen = FindLaptopScreen();
        if (screen is null) return null;
        Connector? c = EnumerateConnectors().Where(x => x.Name == screen).Cast<Connector?>().FirstOrDefault();
        return c is null ? null : Edid.MonitorName(root.ReadBytes(c.Value.SysfsDir + "/edid"));
    }

    // ------------------------------------------------------------------ refresh rate

    private IEnumerable<IRefreshRateBackend> CandidateBackends()
    {
        var available = backends.Where(b => b.IsAvailable).ToList();
        if (cachedBackendName is not null)
        {
            var preferred = available.FirstOrDefault(b => b.Name == cachedBackendName);
            if (preferred is not null) yield return preferred;
        }
        foreach (var b in available)
            if (b.Name != cachedBackendName) yield return b;
    }

    private (IRefreshRateBackend backend, List<DisplayMode> modes)? QueryModes(string screen)
    {
        foreach (IRefreshRateBackend backend in CandidateBackends())
        {
            List<DisplayMode>? modes = backend.QueryModes(screen);
            if (modes is { Count: > 0 })
            {
                cachedBackendName = backend.Name;
                return (backend, modes);
            }
        }
        return null;
    }

    public int GetMaxRefreshRate(string? screen)
    {
        if (screen is null) return -1;
        var query = QueryModes(screen);
        if (query is null) return -1;
        // legacy semantics: highest frequency offered by the panel
        return query.Value.modes.Max(m => m.RoundedHz);
    }

    public int GetRefreshRate(string? screen)
    {
        if (screen is null) return -1;
        var query = QueryModes(screen);
        if (query is null) return -1;
        DisplayMode? current = query.Value.modes.Where(m => m.IsCurrent).Cast<DisplayMode?>().FirstOrDefault();
        return current?.RoundedHz ?? -1;
    }

    public int SetRefreshRate(string screen, int frequency)
    {
        var query = QueryModes(screen);
        if (query is null) return -1;

        DisplayMode? current = query.Value.modes.Where(m => m.IsCurrent).Cast<DisplayMode?>().FirstOrDefault();
        if (current is null) return -1;
        if (current.Value.RoundedHz == frequency) return 1;

        bool ok = query.Value.backend.SetRefreshRate(screen, current.Value, frequency);
        LinuxLog.Write($"Screen = {frequency}Hz via {query.Value.backend.Name} : {(ok ? "OK" : "failed")}");
        return ok ? 1 : -1;
    }

    // ------------------------------------------------------------------ brightness

    /// <summary>The panel backlight: firmware &gt; platform &gt; raw (kernel documentation), never the ScreenPad.</summary>
    internal string? FindBacklight()
    {
        string? best = null;
        int bestRank = int.MaxValue;
        foreach (string dir in root.ListDirectories(BacklightDir))
        {
            string name = Path.GetFileName(dir);
            if (name.Contains("screenpad", StringComparison.OrdinalIgnoreCase)) continue;
            if (!root.FileExists(dir + "/brightness") || root.ReadInt(dir + "/max_brightness") is not > 0) continue;

            int rank = (root.ReadText(dir + "/type") ?? "raw") switch
            {
                "firmware" => 0,
                "platform" => 1,
                _ => 2,
            };
            if (rank < bestRank)
            {
                best = dir;
                bestRank = rank;
            }
        }
        return best;
    }

    public int GetBrightness()
    {
        string? dir = FindBacklight();
        if (dir is null) return -1;
        int? value = root.ReadInt(dir + "/brightness");
        int? max = root.ReadInt(dir + "/max_brightness");
        if (value is null || max is not > 0) return -1;
        return (int)Math.Round(value.Value * 100.0 / max.Value);
    }

    public void SetBrightness(int brightness)
    {
        string? dir = FindBacklight();
        if (dir is null) return;
        int? max = root.ReadInt(dir + "/max_brightness");
        if (max is not > 0) return;

        brightness = Math.Clamp(brightness, 0, 100);
        // never switch the panel completely off from a slider
        int raw = Math.Max(1, (int)Math.Round(brightness * max.Value / 100.0));

        if (root.TryWrite(dir + "/brightness", raw.ToString())) return;

        // not writable for the user: logind grants backlight control to the active session
        string name = Path.GetFileName(dir);
        ctx.Runner.Run("busctl",
            ["call", "org.freedesktop.login1", "/org/freedesktop/login1/session/auto", "org.freedesktop.login1.Session",
             "SetBrightness", "ssu", "backlight", name, raw.ToString(CultureInfo.InvariantCulture)], 3000);
    }

    public int AdjustBrightness(int delta)
    {
        int current = GetBrightness();
        if (current < 0) return -1;
        int target = Math.Clamp(current + delta, 0, 100);
        if (target != current) SetBrightness(target);
        return target;
    }

    // ------------------------------------------------------------------ HDR / gamma / colour

    public bool GetHdrStatus(out bool acm, bool log = true)
    {
        acm = false;
        return false;
    }

    public bool SupportsGammaRamp => ctx.IsX11Session && !ctx.IsWaylandSession && ctx.Runner.CommandExists("xrandr");

    /// <summary>Exponent g such that out = in^(1/g) best fits the given 256 entry ramp (sampled at 50 %).</summary>
    internal static double FitGamma(ushort[] ramp)
    {
        if (ramp.Length < 256) return 1.0;
        double output = ramp[128] / 65535.0;
        double input = 128 / 255.0;
        if (output <= 0.001 || output >= 0.999) return 1.0;
        double gamma = Math.Log(input) / Math.Log(output);
        return Math.Clamp(gamma, 0.1, 10.0);
    }

    public void SetGammaRamp(string? screen, ushort[] red, ushort[] green, ushort[] blue)
    {
        if (!SupportsGammaRamp || screen is null) return;

        string value = string.Create(CultureInfo.InvariantCulture,
            $"{FitGamma(red):0.###}:{FitGamma(green):0.###}:{FitGamma(blue):0.###}");
        ctx.Runner.Run("xrandr", ["--output", screen, "--gamma", value], 4000);
    }

    private string IccDirectory => SysfsRoot.Combine(ctx.XdgDataHome, "icc");

    public bool IsColorProfileInstalled(string profileName)
    {
        string name = SafeFileName(profileName);
        return root.FileExists(SysfsRoot.Combine(IccDirectory, name))
               || root.FileExists("/usr/share/color/icc/" + name)
               || root.FileExists("/usr/local/share/color/icc/" + name);
    }

    public Task InstallColorProfile(string profileName, byte[] iccData)
    {
        string name = SafeFileName(profileName);
        string path = SysfsRoot.Combine(IccDirectory, name);
        try
        {
            string physical = root.Map(path);
            Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
            File.WriteAllBytes(physical, iccData);

            if (ctx.Runner.CommandExists("colormgr"))
                ctx.Runner.Run("colormgr", ["import-profile", path], 10000);
        }
        catch (Exception ex)
        {
            LinuxLog.Write("Can't install colour profile: " + ex.Message);
        }
        return Task.CompletedTask;
    }

    private static string SafeFileName(string name) => Path.GetFileName(name.Replace('\\', '/'));

    // ------------------------------------------------------------------ touchscreen (X11 xinput)

    private int? FindTouchscreenId()
    {
        if (!ctx.IsX11Session || !ctx.Runner.CommandExists("xinput")) return null;
        CommandResult r = ctx.Runner.Run("xinput", ["list", "--short"], 3000);
        if (!r.Success) return null;
        return ParseTouchscreenId(r.StdOut);
    }

    [GeneratedRegex(@"id=(?<id>\d+)")]
    private static partial Regex IdToken();

    internal static int? ParseTouchscreenId(string xinputShortList)
    {
        foreach (string line in xinputShortList.Split('\n'))
        {
            if (!line.Contains("touchscreen", StringComparison.OrdinalIgnoreCase)) continue;
            if (!line.Contains("slave", StringComparison.OrdinalIgnoreCase) && !line.Contains("pointer", StringComparison.OrdinalIgnoreCase)) continue;
            Match m = IdToken().Match(line);
            if (m.Success) return int.Parse(m.Groups["id"].Value, CultureInfo.InvariantCulture);
        }
        return null;
    }

    public bool? GetTouchscreenStatus()
    {
        int? id = FindTouchscreenId();
        if (id is null) return null;

        CommandResult r = ctx.Runner.Run("xinput", ["list-props", id.Value.ToString(CultureInfo.InvariantCulture)], 3000);
        if (!r.Success) return null;
        Match m = Regex.Match(r.StdOut, @"Device Enabled \(\d+\):\s*(\d)");
        return m.Success ? m.Groups[1].Value == "1" : null;
    }

    public void SetTouchscreenStatus(bool enabled)
    {
        int? id = FindTouchscreenId();
        if (id is null) return;
        ctx.Runner.Run("xinput", [enabled ? "enable" : "disable", id.Value.ToString(CultureInfo.InvariantCulture)], 3000);
    }
}
