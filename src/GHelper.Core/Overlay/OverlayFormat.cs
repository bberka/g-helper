using System.Globalization;
using System.Text.RegularExpressions;

namespace GHelper.Overlay
{
    /// <summary>Text model of the overlay: how sensor values and device names are shortened / formatted.</summary>
    public static class OverlayFormat
    {
        public static double ToDouble(object? v)
        {
            try { return v is null ? 0.0 : Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return 0.0; }
        }

        public static string FmtTemp(double t) =>
            t > 0 ? ((int)Math.Round(t) + "°").PadLeft(4) : " -- ";

        public static string FmtPow(double p) =>
            p > 0 ? Math.Round(p, 1).ToString("F1") + "W" : "";

        public static string FmtGpuPower(float? power) =>
            power is null ? "" : Math.Round(power.Value, 1).ToString("F1") + "W";

        public static string FormatFan(int? fan)
        {
            if (fan is null || fan < 0) return "";
            return fan.Value.ToString(CultureInfo.InvariantCulture);
        }

        public static string FormatUsage(int? usage) => usage.HasValue ? usage.Value + "%" : "";

        public static string FormatMemGb(int? usedMb) =>
            usedMb.HasValue ? (usedMb.Value / 1024.0).ToString("F1") + "GB" : "";

        public static string FormatBatteryLevel(double level) => level > 0 ? (int)Math.Round(level) + "%" : "--";

        /// <summary>"+12.3" while charging, "-8.1" while discharging, "" when idle.</summary>
        public static string FormatBatteryRate(decimal rate) =>
            rate == 0 ? "" : (rate > 0 ? "+" : "-") + Math.Abs(rate).ToString("F1");

        // "NVIDIA GeForce RTX 4070 Laptop GPU" -> "RTX 4070", "AMD Radeon RX 6850M XT" -> "RX 6850M"
        public static string ShortGpuName(string? full)
        {
            if (string.IsNullOrEmpty(full)) return "";
            foreach (string tag in new[] { "RTX", "GTX", "RX", "Arc" })
            {
                int i = full.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                if (i < 0) continue;
                string[] p = full[i..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string s = p.Length >= 2 ? p[0] + " " + p[1] : p[0];
                if (p.Length >= 3 && p[2] == "Ti") s += " Ti";
                return s;
            }
            return full;
        }

        // "...Core(TM) i9-13980HX" -> "i9-13980HX", "AMD Ryzen 9 7945HX..." -> "Ryzen 7945HX", "...Core(TM) Ultra 9 185H" -> "Ultra 185H"
        public static string ShortCpuName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            var m = Regex.Match(name, @"i[3579]-\w+");
            if (m.Success) return m.Value;

            m = Regex.Match(name, @"Ultra\s+\d+\s+(\w*\d\w*)");
            if (m.Success) return "Ultra " + m.Groups[1].Value;

            if (name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase))
            {
                m = Regex.Match(name, @"(?:[A-Z]{2,}\s+)?\d{3,}\w*");
                return m.Success ? "Ryzen " + m.Value : "Ryzen";
            }

            return name.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } t ? t[0] : "";
        }
    }
}
