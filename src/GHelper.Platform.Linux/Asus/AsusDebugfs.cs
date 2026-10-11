using System.Globalization;
using System.Text.RegularExpressions;

namespace GHelper.Platform.Linux;

/// <summary>
/// Raw DSTS / DEVS access through the asus-wmi debugfs directory (/sys/kernel/debug/asus-nb-wmi):
/// write the device id to dev_id (and the argument to ctrl_param), then read dsts / devs, which prints
/// "DSTS(0x120075) = 0x10001" / "DEVS(0x120075, 0x1) = 0x1". Needs root and a kernel with debugfs.
/// This is the equivalent of the Windows ATKACPI IOCTL for ids without a dedicated sysfs attribute. The files are
/// global state of the driver, so every transaction is serialised.
/// </summary>
public sealed partial class AsusDebugfs
{
    private readonly SysfsRoot root;
    private readonly string dir;
    private readonly object gate = new();

    public AsusDebugfs(SysfsRoot root, string dir)
    {
        this.root = root;
        this.dir = dir;
    }

    /// <summary>Raw DSTS result (presence bit 0x10000 included) or null on failure / unsupported id.</summary>
    public uint? Dsts(uint deviceId)
    {
        lock (gate)
        {
            if (!root.TryWrite(dir + "/dev_id", "0x" + deviceId.ToString("x")))
                return null;

            string? text = root.ReadText(dir + "/dsts");
            return ParseResult(text);
        }
    }

    /// <summary>Raw DEVS result (1 = success on most ids) or null on failure.</summary>
    public uint? Devs(uint deviceId, uint parameter)
    {
        lock (gate)
        {
            if (!root.TryWrite(dir + "/dev_id", "0x" + deviceId.ToString("x"))) return null;
            if (!root.TryWrite(dir + "/ctrl_param", "0x" + parameter.ToString("x"))) return null;

            string? text = root.ReadText(dir + "/devs");
            return ParseResult(text);
        }
    }

    [GeneratedRegex(@"=\s*(0x[0-9a-fA-F]+|[0-9]+)\s*$")]
    private static partial Regex ResultRegex();

    internal static uint? ParseResult(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string last = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1];
        Match m = ResultRegex().Match(last.Trim());
        if (!m.Success) return null;
        string v = m.Groups[1].Value;
        try
        {
            return v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? uint.Parse(v[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : uint.Parse(v, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }
}
