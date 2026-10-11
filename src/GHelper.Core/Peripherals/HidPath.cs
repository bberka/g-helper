using System.Text.RegularExpressions;
using HidSharp;

namespace GHelper.Peripherals
{
    /// <summary>USB interface / top level collection a HID node belongs to (null = unknown on this OS).</summary>
    public readonly record struct HidInterfaceInfo(int? Interface, int? Collection);

    /// <summary>
    /// Legacy peripheral code selects the right HID interface of a device with a substring of the Windows device path
    /// ("mi_01", "mi_02&amp;col03"). On Linux the path is "/dev/hidrawN", so the same tokens are resolved through the
    /// USB interface number of the node instead. Full device paths still match by plain substring.
    /// </summary>
    public static class HidPath
    {
        private static readonly Regex TokenRx = new(@"^(?:mi_(?<mi>[0-9a-f]{2}))?(?:&?col(?<col>[0-9a-f]{2}))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WinMiRx = new(@"mi_(?<mi>[0-9a-f]{2})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WinColRx = new(@"col(?<col>[0-9a-f]{2})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Resolves a device path to its interface/collection. The default handles Windows paths textually and Linux
        /// hidraw nodes through sysfs. The platform layer may replace it with something better.
        /// </summary>
        public static Func<string, HidInterfaceInfo?> InterfaceResolver { get; set; } = DefaultResolve;

        /// <summary>Root of sysfs, overridable for tests.</summary>
        public static string SysfsRoot { get; set; } = "/sys";

        public static bool Matches(HidDevice device, string token) => Matches(device.DevicePath, token);

        public static bool Matches(string devicePath, string token)
        {
            if (string.IsNullOrEmpty(token)) return true;
            // a full hidraw node: "/dev/hidraw1" must not match "/dev/hidraw13"
            if (token.StartsWith('/')) return string.Equals(devicePath, token, StringComparison.Ordinal);
            if (devicePath.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;

            var parsed = TokenRx.Match(token);
            if (!parsed.Success) return false;

            int? wantMi = parsed.Groups["mi"].Success ? Convert.ToInt32(parsed.Groups["mi"].Value, 16) : null;
            int? wantCol = parsed.Groups["col"].Success ? Convert.ToInt32(parsed.Groups["col"].Value, 16) : null;
            if (wantMi is null && wantCol is null) return false;

            HidInterfaceInfo? info;
            try { info = InterfaceResolver(devicePath); }
            catch { return false; }
            if (info is null) return false;

            if (wantMi is not null && info.Value.Interface != wantMi) return false;
            // hidraw nodes expose all top level collections of an interface at once, so a collection
            // is only compared when the OS actually reports one
            if (wantCol is not null)
            {
                if (info.Value.Collection is not null) return info.Value.Collection == wantCol;
                return wantMi is not null;
            }
            return true;
        }

        public static HidInterfaceInfo? ParseWindowsPath(string devicePath)
        {
            var mi = WinMiRx.Match(devicePath);
            var col = WinColRx.Match(devicePath);
            if (!mi.Success && !col.Success) return null;
            return new HidInterfaceInfo(
                mi.Success ? Convert.ToInt32(mi.Groups["mi"].Value, 16) : null,
                col.Success ? Convert.ToInt32(col.Groups["col"].Value, 16) : null);
        }

        private static HidInterfaceInfo? DefaultResolve(string devicePath)
        {
            var win = ParseWindowsPath(devicePath);
            if (win is not null) return win;
            return ResolveHidraw(devicePath, SysfsRoot);
        }

        /// <summary>/dev/hidrawN -> USB interface number via /sys/class/hidraw/hidrawN/device/../bInterfaceNumber.</summary>
        public static HidInterfaceInfo? ResolveHidraw(string devicePath, string sysfsRoot)
        {
            string name = Path.GetFileName(devicePath.TrimEnd('/'));
            if (!name.StartsWith("hidraw", StringComparison.Ordinal)) return null;

            string file = Path.Combine(sysfsRoot, "class", "hidraw", name, "device", "..", "bInterfaceNumber");
            try
            {
                if (!File.Exists(file)) return null;
                string text = File.ReadAllText(file).Trim();
                if (int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out int number))
                    return new HidInterfaceInfo(number, null);
            }
            catch { }
            return null;
        }
    }
}
