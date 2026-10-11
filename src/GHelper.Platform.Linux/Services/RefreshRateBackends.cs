using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GHelper.Platform.Linux;

internal readonly record struct DisplayMode(string Id, int Width, int Height, double RefreshHz, bool IsCurrent)
{
    public int RoundedHz => (int)Math.Round(RefreshHz);
}

/// <summary>Reads / changes the refresh rate of one connector through a desktop specific tool.</summary>
internal interface IRefreshRateBackend
{
    string Name { get; }
    bool IsAvailable { get; }
    List<DisplayMode>? QueryModes(string connector);
    bool SetRefreshRate(string connector, DisplayMode current, int hz);
}

// ---------------------------------------------------------------------------------------------- X11 / xrandr

internal sealed partial class XrandrBackend(LinuxContext ctx) : IRefreshRateBackend
{
    public string Name => "xrandr";
    public bool IsAvailable => ctx.IsX11Session && ctx.Runner.CommandExists("xrandr");

    public List<DisplayMode>? QueryModes(string connector)
    {
        CommandResult r = ctx.Runner.Run("xrandr", ["--query"], 4000);
        return r.Success ? ParseOutput(r.StdOut, connector) : null;
    }

    public bool SetRefreshRate(string connector, DisplayMode current, int hz)
    {
        DisplayMode? target = null;
        List<DisplayMode>? modes = QueryModes(connector);
        if (modes is not null)
            target = modes.FirstOrDefault(m => m.Width == current.Width && m.Height == current.Height && m.RoundedHz == hz);

        string rate = target is { } t ? t.RefreshHz.ToString("0.00", CultureInfo.InvariantCulture) : hz.ToString(CultureInfo.InvariantCulture);
        return ctx.Runner.Run("xrandr",
            ["--output", connector, "--mode", $"{current.Width}x{current.Height}", "--rate", rate], 8000).Success;
    }

    [GeneratedRegex(@"^(?<name>\S+)\s+(?<state>connected|disconnected)\b")]
    private static partial Regex OutputLine();

    [GeneratedRegex(@"^\s+(?<w>\d+)x(?<h>\d+)[a-z]?\s+(?<rest>.*)$")]
    private static partial Regex ModeLine();

    [GeneratedRegex(@"(?<rate>\d+(?:\.\d+)?)(?<flags>[*+]*)")]
    private static partial Regex RateToken();

    /// <summary>Modes of one output from "xrandr --query" text.</summary>
    internal static List<DisplayMode> ParseOutput(string text, string connector)
    {
        var modes = new List<DisplayMode>();
        bool inside = false;

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            Match header = OutputLine().Match(line);
            if (header.Success)
            {
                inside = header.Groups["name"].Value == connector;
                continue;
            }
            if (!inside) continue;

            Match mode = ModeLine().Match(line);
            if (!mode.Success)
            {
                if (line.Length > 0 && !char.IsWhiteSpace(line[0])) inside = false;
                continue;
            }

            int w = int.Parse(mode.Groups["w"].Value, CultureInfo.InvariantCulture);
            int h = int.Parse(mode.Groups["h"].Value, CultureInfo.InvariantCulture);
            foreach (Match token in RateToken().Matches(mode.Groups["rest"].Value))
            {
                if (!double.TryParse(token.Groups["rate"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate)) continue;
                bool current = token.Groups["flags"].Value.Contains('*');
                modes.Add(new DisplayMode($"{w}x{h}", w, h, rate, current));
            }
        }
        return modes;
    }

    /// <summary>Output names of "xrandr --query" with connection state.</summary>
    internal static Dictionary<string, bool> ParseOutputs(string text)
    {
        var result = new Dictionary<string, bool>();
        foreach (string raw in text.Split('\n'))
        {
            Match m = OutputLine().Match(raw.TrimEnd('\r'));
            if (m.Success) result[m.Groups["name"].Value] = m.Groups["state"].Value == "connected";
        }
        return result;
    }
}

// ---------------------------------------------------------------------------------------------- wlroots

internal sealed partial class WlrRandrBackend(LinuxContext ctx) : IRefreshRateBackend
{
    public string Name => "wlr-randr";
    public bool IsAvailable => ctx.IsWaylandSession && ctx.Runner.CommandExists("wlr-randr");

    public List<DisplayMode>? QueryModes(string connector)
    {
        CommandResult r = ctx.Runner.Run("wlr-randr", [], 4000);
        return r.Success ? ParseOutput(r.StdOut, connector) : null;
    }

    public bool SetRefreshRate(string connector, DisplayMode current, int hz)
    {
        string mode = $"{current.Width}x{current.Height}@{hz}Hz";
        List<DisplayMode>? modes = QueryModes(connector);
        DisplayMode? exact = modes?.FirstOrDefault(m => m.Width == current.Width && m.Height == current.Height && m.RoundedHz == hz);
        if (exact is { } e) mode = $"{e.Width}x{e.Height}@{e.RefreshHz.ToString("0.000000", CultureInfo.InvariantCulture)}Hz";

        return ctx.Runner.Run("wlr-randr", ["--output", connector, "--mode", mode], 8000).Success;
    }

    [GeneratedRegex(@"^\s+(?<w>\d+)x(?<h>\d+)\s*px,\s*(?<hz>\d+(?:\.\d+)?)\s*Hz(?:\s*\((?<flags>[^)]*)\))?")]
    private static partial Regex ModeLine();

    internal static List<DisplayMode> ParseOutput(string text, string connector)
    {
        var modes = new List<DisplayMode>();
        bool inside = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            if (!char.IsWhiteSpace(line[0]))
            {
                inside = line.Split(' ', 2)[0] == connector;
                continue;
            }
            if (!inside) continue;

            Match m = ModeLine().Match(line);
            if (!m.Success) continue;
            int w = int.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture);
            int h = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
            double hz = double.Parse(m.Groups["hz"].Value, CultureInfo.InvariantCulture);
            bool current = m.Groups["flags"].Value.Contains("current", StringComparison.Ordinal);
            modes.Add(new DisplayMode($"{w}x{h}", w, h, hz, current));
        }
        return modes;
    }
}

// ---------------------------------------------------------------------------------------------- KDE

internal sealed partial class KscreenBackend(LinuxContext ctx) : IRefreshRateBackend
{
    public string Name => "kscreen-doctor";
    public bool IsAvailable => ctx.Runner.CommandExists("kscreen-doctor");

    public List<DisplayMode>? QueryModes(string connector)
    {
        CommandResult r = ctx.Runner.Run("kscreen-doctor", ["-o"], 5000);
        return r.Success ? ParseOutput(r.StdOut, connector) : null;
    }

    public bool SetRefreshRate(string connector, DisplayMode current, int hz)
    {
        List<DisplayMode>? modes = QueryModes(connector);
        DisplayMode? target = modes?.FirstOrDefault(m => m.Width == current.Width && m.Height == current.Height && m.RoundedHz == hz);
        if (target is not { } t) return false;
        return ctx.Runner.Run("kscreen-doctor", [$"output.{connector}.mode.{t.Id}"], 8000).Success;
    }

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Ansi();

    [GeneratedRegex(@"^Output:\s+\d+\s+(?<name>\S+)")]
    private static partial Regex OutputLine();

    [GeneratedRegex(@"(?<id>[^\s:]+):(?<w>\d+)x(?<h>\d+)@(?<hz>\d+(?:\.\d+)?)(?<flags>[*!]*)")]
    private static partial Regex ModeToken();

    /// <summary>
    /// "Output: 1 eDP-1 enabled connected ... Modes:  0:2560x1600@60.00!  1:2560x1600@165.00*" (colour codes removed).
    /// The id used by "output.NAME.mode.ID" is the token before the colon.
    /// </summary>
    internal static List<DisplayMode> ParseOutput(string text, string connector)
    {
        var modes = new List<DisplayMode>();
        text = Ansi().Replace(text, "");
        bool inside = false;

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            Match header = OutputLine().Match(line);
            if (header.Success) inside = header.Groups["name"].Value == connector;
            if (!inside) continue;

            int idx = line.IndexOf("Modes:", StringComparison.Ordinal);
            if (idx < 0) continue;

            foreach (Match m in ModeToken().Matches(line[(idx + 6)..]))
            {
                modes.Add(new DisplayMode(
                    m.Groups["id"].Value,
                    int.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture),
                    int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture),
                    double.Parse(m.Groups["hz"].Value, CultureInfo.InvariantCulture),
                    m.Groups["flags"].Value.Contains('*')));
            }
        }
        return modes;
    }
}

// ---------------------------------------------------------------------------------------------- GNOME / Mutter

internal sealed class MutterBackend(LinuxContext ctx) : IRefreshRateBackend
{
    private const string Dest = "org.gnome.Mutter.DisplayConfig";
    private const string Path = "/org/gnome/Mutter/DisplayConfig";

    public string Name => "mutter";
    public bool IsAvailable => ctx.IsWaylandSession && ctx.Runner.CommandExists("gdbus")
                               && ctx.CurrentDesktop.Contains("GNOME", StringComparison.Ordinal);

    private string? ReadState()
    {
        CommandResult r = ctx.Runner.Run("gdbus",
            ["call", "--session", "--dest", Dest, "--object-path", Path, "--method", Dest + ".GetCurrentState"], 5000);
        return r.Success ? r.StdOut : null;
    }

    public List<DisplayMode>? QueryModes(string connector)
    {
        string? text = ReadState();
        if (text is null) return null;
        MutterState? state = MutterState.Parse(text);
        return state?.Monitors.FirstOrDefault(m => m.Connector == connector)?.Modes;
    }

    public bool SetRefreshRate(string connector, DisplayMode current, int hz)
    {
        string? text = ReadState();
        MutterState? state = text is null ? null : MutterState.Parse(text);
        if (state is null) return false;

        MutterMonitor? monitor = state.Monitors.FirstOrDefault(m => m.Connector == connector);
        DisplayMode? target = monitor?.Modes.FirstOrDefault(m => m.Width == current.Width && m.Height == current.Height && m.RoundedHz == hz);
        if (monitor is null || target is not { } t) return false;

        string config = state.BuildApplyConfig(connector, t.Id);
        if (config.Length == 0) return false;

        // method 1 = temporary (not written to monitors.xml)
        return ctx.Runner.Run("gdbus",
            ["call", "--session", "--dest", Dest, "--object-path", Path, "--method", Dest + ".ApplyMonitorsConfig",
             state.Serial.ToString(CultureInfo.InvariantCulture), "1", config, "{}"], 10000).Success;
    }
}

internal sealed class MutterMonitor
{
    public string Connector = "";
    public List<DisplayMode> Modes = new();
}

internal sealed class MutterLogicalMonitor
{
    public double X, Y, Scale;
    public long Transform;
    public bool Primary;
    public List<string> Connectors = new();
}

/// <summary>Result of org.gnome.Mutter.DisplayConfig.GetCurrentState (printed by gdbus as GVariant text).</summary>
internal sealed class MutterState
{
    public long Serial;
    public List<MutterMonitor> Monitors = new();
    public List<MutterLogicalMonitor> Logical = new();

    public static MutterState? Parse(string text)
    {
        try
        {
            GVariantText.Node? root = GVariantText.Parse(text);
            if (root is not GVariantText.Tuple top || top.Items.Count < 3) return null;

            var state = new MutterState { Serial = GVariantText.AsLong(top.Items[0]) };

            foreach (GVariantText.Node m in GVariantText.AsList(top.Items[1]))
            {
                if (m is not GVariantText.Tuple mt || mt.Items.Count < 2) continue;
                if (mt.Items[0] is not GVariantText.Tuple id || id.Items.Count < 1) continue;

                var monitor = new MutterMonitor { Connector = GVariantText.AsString(id.Items[0]) };
                foreach (GVariantText.Node modeNode in GVariantText.AsList(mt.Items[1]))
                {
                    if (modeNode is not GVariantText.Tuple mode || mode.Items.Count < 7) continue;
                    string modeId = GVariantText.AsString(mode.Items[0]);
                    int w = (int)GVariantText.AsLong(mode.Items[1]);
                    int h = (int)GVariantText.AsLong(mode.Items[2]);
                    double hz = GVariantText.AsDouble(mode.Items[3]);
                    bool isCurrent = GVariantText.DictBool(mode.Items[6], "is-current");
                    monitor.Modes.Add(new DisplayMode(modeId, w, h, hz, isCurrent));
                }
                state.Monitors.Add(monitor);
            }

            foreach (GVariantText.Node l in GVariantText.AsList(top.Items[2]))
            {
                if (l is not GVariantText.Tuple lt || lt.Items.Count < 6) continue;
                var logical = new MutterLogicalMonitor
                {
                    X = GVariantText.AsDouble(lt.Items[0]),
                    Y = GVariantText.AsDouble(lt.Items[1]),
                    Scale = GVariantText.AsDouble(lt.Items[2]),
                    Transform = GVariantText.AsLong(lt.Items[3]),
                    Primary = lt.Items[4] is GVariantText.Bool { Value: true },
                };
                foreach (GVariantText.Node c in GVariantText.AsList(lt.Items[5]))
                    if (c is GVariantText.Tuple ct && ct.Items.Count > 0)
                        logical.Connectors.Add(GVariantText.AsString(ct.Items[0]));
                state.Logical.Add(logical);
            }

            return state.Monitors.Count > 0 ? state : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>GVariant text for the "logical_monitors" argument with one monitor switched to <paramref name="modeId"/>.</summary>
    public string BuildApplyConfig(string connector, string modeId)
    {
        var sb = new StringBuilder("[");
        bool first = true;
        foreach (MutterLogicalMonitor l in Logical)
        {
            if (!first) sb.Append(", ");
            first = false;

            sb.Append('(')
              .Append(((int)l.X).ToString(CultureInfo.InvariantCulture)).Append(", ")
              .Append(((int)l.Y).ToString(CultureInfo.InvariantCulture)).Append(", ")
              .Append(l.Scale.ToString("0.0###", CultureInfo.InvariantCulture)).Append(", ")
              .Append("uint32 ").Append(l.Transform.ToString(CultureInfo.InvariantCulture)).Append(", ")
              .Append(l.Primary ? "true" : "false").Append(", [");

            for (int i = 0; i < l.Connectors.Count; i++)
            {
                string name = l.Connectors[i];
                MutterMonitor? monitor = Monitors.FirstOrDefault(m => m.Connector == name);
                string mode = name == connector
                    ? modeId
                    : monitor?.Modes.FirstOrDefault(m => m.IsCurrent).Id ?? "";
                if (mode.Length == 0) return "";
                if (i > 0) sb.Append(", ");
                sb.Append("('").Append(name).Append("', '").Append(mode).Append("', {})");
            }
            sb.Append("])");
        }
        sb.Append(']');
        return Logical.Count == 0 ? "" : sb.ToString();
    }
}

/// <summary>Parser for the subset of GVariant text that "gdbus call" prints (tuples, arrays, dicts, scalars).</summary>
internal static class GVariantText
{
    public abstract record Node;
    public sealed record Tuple(List<Node> Items) : Node;
    public sealed record Array(List<Node> Items) : Node;
    public sealed record Dict(List<(Node Key, Node Value)> Entries) : Node;
    public sealed record Str(string Value) : Node;
    public sealed record Num(double Value, long Integer) : Node;
    public sealed record Bool(bool Value) : Node;
    public sealed record Variant(Node Inner) : Node;

    public static Node? Parse(string text)
    {
        int pos = 0;
        return ParseValue(text, ref pos);
    }

    public static long AsLong(Node n) => n switch { Num x => x.Integer, Variant v => AsLong(v.Inner), _ => 0 };
    public static double AsDouble(Node n) => n switch { Num x => x.Value, Variant v => AsDouble(v.Inner), _ => 0 };
    public static string AsString(Node n) => n switch { Str s => s.Value, Variant v => AsString(v.Inner), _ => "" };
    public static List<Node> AsList(Node n) => n switch { Array a => a.Items, Tuple t => t.Items, Variant v => AsList(v.Inner), _ => [] };

    public static bool DictBool(Node n, string key)
    {
        if (n is not Dict d) return false;
        foreach (var (k, v) in d.Entries)
            if (AsString(k) == key)
                return (v is Variant { Inner: Bool b } && b.Value) || (v is Bool b2 && b2.Value);
        return false;
    }

    private static void SkipSpace(string s, ref int p)
    {
        while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
    }

    private static Node? ParseValue(string s, ref int p)
    {
        SkipSpace(s, ref p);
        if (p >= s.Length) return null;

        char c = s[p];
        switch (c)
        {
            case '(':
            {
                p++;
                var items = new List<Node>();
                while (true)
                {
                    SkipSpace(s, ref p);
                    if (p >= s.Length) return null;
                    if (s[p] == ')') { p++; break; }
                    if (s[p] == ',') { p++; continue; }
                    Node? item = ParseValue(s, ref p);
                    if (item is null) return null;
                    items.Add(item);
                }
                return new Tuple(items);
            }
            case '[':
            {
                p++;
                var items = new List<Node>();
                while (true)
                {
                    SkipSpace(s, ref p);
                    if (p >= s.Length) return null;
                    if (s[p] == ']') { p++; break; }
                    if (s[p] == ',') { p++; continue; }
                    Node? item = ParseValue(s, ref p);
                    if (item is null) return null;
                    items.Add(item);
                }
                return new Array(items);
            }
            case '{':
            {
                p++;
                var entries = new List<(Node, Node)>();
                while (true)
                {
                    SkipSpace(s, ref p);
                    if (p >= s.Length) return null;
                    if (s[p] == '}') { p++; break; }
                    if (s[p] == ',') { p++; continue; }
                    Node? key = ParseValue(s, ref p);
                    SkipSpace(s, ref p);
                    if (key is null || p >= s.Length || s[p] != ':') return null;
                    p++;
                    Node? value = ParseValue(s, ref p);
                    if (value is null) return null;
                    entries.Add((key, value));
                }
                return new Dict(entries);
            }
            case '<':
            {
                p++;
                Node? inner = ParseValue(s, ref p);
                SkipSpace(s, ref p);
                if (inner is null || p >= s.Length || s[p] != '>') return null;
                p++;
                return new Variant(inner);
            }
            case '\'':
            case '"':
            {
                char quote = c;
                p++;
                var sb = new StringBuilder();
                while (p < s.Length && s[p] != quote)
                {
                    if (s[p] == '\\' && p + 1 < s.Length) p++;
                    sb.Append(s[p]);
                    p++;
                }
                if (p >= s.Length) return null;
                p++;
                return new Str(sb.ToString());
            }
            case '@':
            {
                // type annotation such as "@as" or "@a{sv}": skip the type, parse the value
                while (p < s.Length && !char.IsWhiteSpace(s[p])) p++;
                return ParseValue(s, ref p);
            }
        }

        // bare word or number
        int start = p;
        while (p < s.Length && !char.IsWhiteSpace(s[p]) && s[p] is not (',' or ')' or ']' or '}' or '>' or ':'))
            p++;
        string word = s[start..p];
        if (word.Length == 0) return null;

        if (word == "true") return new Bool(true);
        if (word == "false") return new Bool(false);

        if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            return new Num(number, (long)number);

        // type prefix ("uint32 3", "double 1.5", "byte 0x01")
        SkipSpace(s, ref p);
        Node? typed = ParseValue(s, ref p);
        return typed;
    }
}
