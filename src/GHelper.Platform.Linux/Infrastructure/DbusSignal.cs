using System.Text.RegularExpressions;

namespace GHelper.Platform.Linux;

/// <summary>One signal line printed by "gdbus monitor".</summary>
internal readonly record struct DbusSignal(string ObjectPath, string Interface, string Member, string Arguments)
{
    private static readonly Regex Line = new(@"^(?<path>/\S*):\s+(?<iface>[A-Za-z0-9_.]+)\.(?<member>[A-Za-z0-9_]+)\s*\((?<args>.*)\)\s*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses "/org/freedesktop/login1: org.freedesktop.login1.Manager.PrepareForSleep (true,)". Returns null for the
    /// banner lines and method calls.
    /// </summary>
    public static DbusSignal? Parse(string line)
    {
        Match m = Line.Match(line.Trim());
        if (!m.Success) return null;
        return new DbusSignal(m.Groups["path"].Value, m.Groups["iface"].Value, m.Groups["member"].Value, m.Groups["args"].Value);
    }

    /// <summary>First boolean in the argument list ("(true,)" -> true), null if none.</summary>
    public bool? FirstBool
    {
        get
        {
            Match m = Regex.Match(Arguments, @"\b(true|false)\b");
            return m.Success ? m.Groups[1].Value == "true" : null;
        }
    }

    /// <summary>Value of a boolean property in a PropertiesChanged dictionary ({'LidClosed': &lt;true&gt;}).</summary>
    public bool? PropertyBool(string name)
    {
        Match m = Regex.Match(Arguments, "'" + Regex.Escape(name) + @"':\s*<(true|false)>");
        return m.Success ? m.Groups[1].Value == "true" : null;
    }

    public string? PropertyString(string name)
    {
        Match m = Regex.Match(Arguments, "'" + Regex.Escape(name) + @"':\s*<'([^']*)'>");
        return m.Success ? m.Groups[1].Value : null;
    }
}
