namespace GHelper.App;

/// <summary>Legacy first-argument actions of app/Program.cs.</summary>
public enum LaunchAction
{
    None,
    /// <summary>Apply the saved battery charge limit + keyboard backlight and exit without any UI (scheduled at logon).</summary>
    Charge,
    /// <summary>Open the fan window on the CPU tab (after elevating).</summary>
    Cpu,
    /// <summary>Open the fan window on the GPU tab (after elevating).</summary>
    Gpu,
    /// <summary>Open the Extra window and its ASUS services section.</summary>
    Services,
    /// <summary>Open the fan window on the undervolting tab (after elevating).</summary>
    Uv,
    /// <summary>Download the colour profiles.</summary>
    Colors,
}

/// <summary>
/// Parsed command line. Legacy accepted a single positional action (<c>GHelper.exe cpu</c>); the parser keeps that and
/// additionally understands <c>debug</c> / <c>--debug</c>, <c>--version</c> and <c>--show</c> anywhere.
/// </summary>
public sealed class LaunchOptions
{
    public static LaunchOptions Current { get; set; } = new();

    public LaunchAction Action { get; init; } = LaunchAction.None;

    /// <summary>Verbose diagnostics requested (legacy "debug").</summary>
    public bool Debug { get; init; }

    /// <summary>Print the version and exit (exit code 0, no window).</summary>
    public bool ShowVersion { get; init; }

    /// <summary>Show the settings window right after start, even when launched from autostart. Useful on desktops without a tray.</summary>
    public bool ShowWindow { get; init; }

    /// <summary>Unrecognised arguments, kept for diagnostics.</summary>
    public IReadOnlyList<string> Unknown { get; init; } = Array.Empty<string>();

    /// <summary>Legacy: any action argument makes the settings window appear on start.</summary>
    public bool OpensWindow => ShowWindow || (Action != LaunchAction.None && Action != LaunchAction.Charge);

    /// <summary>"charge" runs without creating any window.</summary>
    public bool IsHeadless => Action == LaunchAction.Charge;

    public static LaunchOptions Parse(IEnumerable<string> args)
    {
        var action = LaunchAction.None;
        bool debug = false, version = false, show = false;
        var unknown = new List<string>();

        foreach (var raw in args)
        {
            string a = raw.Trim().TrimStart('-', '/').ToLowerInvariant();
            switch (a)
            {
                case "charge": SetAction(LaunchAction.Charge); break;
                case "cpu": SetAction(LaunchAction.Cpu); break;
                case "gpu": SetAction(LaunchAction.Gpu); break;
                case "services": SetAction(LaunchAction.Services); break;
                case "uv": SetAction(LaunchAction.Uv); break;
                case "colors": SetAction(LaunchAction.Colors); break;
                case "debug": debug = true; break;
                case "version" or "v": version = true; break;
                case "show": show = true; break;
                case "": break;
                default: unknown.Add(raw); break;
            }
        }

        // the first action wins like the legacy args[0]
        void SetAction(LaunchAction candidate)
        {
            if (action == LaunchAction.None) action = candidate;
        }

        return new LaunchOptions { Action = action, Debug = debug, ShowVersion = version, ShowWindow = show, Unknown = unknown };
    }
}
