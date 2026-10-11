using System.Drawing;

namespace GHelper.Overlay
{
    /// <summary>Overlay presets. Click on the overlay cycles Light, Default, Full, Complete, Light.</summary>
    public enum OverlayMode { Default = 0, Light = 1, Full = 2, Complete = 3 }

    /// <summary>
    /// Persisted overlay options (config keys overlay_*), the static half of the legacy HardwareOverlay class that
    /// the OverlayConfig window also used.
    /// </summary>
    public static class OverlaySettings
    {
        public const int MinScalePercent = 35;
        public const int MaxScalePercent = 300;
        public const int ScaleStepPercent = 10;

        public const int DefaultAlpha = 128;

        public static readonly Color DefaultGpuColor = Color.FromArgb(255, 0, 255, 80);
        public static readonly Color DefaultCpuColor = Color.FromArgb(255, 60, 220, 255);

        public static Color CpuColor => ParseColor("overlay_color_cpu", DefaultCpuColor);
        public static Color GpuColor => ParseColor("overlay_color_gpu", DefaultGpuColor);

        public static string ModeKey(string key, int mode) => key + "_" + mode;

        public static OverlayMode GetMode() => (OverlayMode)Math.Clamp(AppConfig.Get("overlay_mode", 0), 0, 3);

        public static int GetScalePercent() => Math.Clamp(AppConfig.Get("overlay_scale_percent", 100), MinScalePercent, MaxScalePercent);

        public static int GetBackgroundAlpha() => Math.Clamp(AppConfig.Get("overlay_alpha", DefaultAlpha), 0, 255);

        /// <summary>Default visibility of a block in a preset, before user overrides.</summary>
        public static bool BlockDefault(string key, OverlayMode mode) => key switch
        {
            "overlay_show_fans" or "overlay_show_chart" => mode != OverlayMode.Light,
            "overlay_show_usage" => mode == OverlayMode.Full || mode == OverlayMode.Complete,
            "overlay_show_ram" => mode == OverlayMode.Complete,
            "overlay_names" => false,
            _ => true,
        };

        public static bool BlockShown(string key, int mode)
        {
            bool def = BlockDefault(key, (OverlayMode)mode);
            return AppConfig.Get(ModeKey(key, mode), def ? 1 : 0) != 0;
        }

        // -1 = only on battery, 0 = off, 1 = always
        public static int BatteryState(int mode) =>
            AppConfig.Get(ModeKey("overlay_show_battery", mode), (OverlayMode)mode == OverlayMode.Light ? 0 : -1);

        /// <summary>
        /// "#RRGGBB", "RRGGBB", "#RGB", "#AARRGGBB" or a colour name; <paramref name="fallback"/> when it can't be parsed.
        /// </summary>
        public static Color ParseColor(string? hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;

            string text = hex.Trim();
            string digits = text.StartsWith('#') ? text[1..] : text;

            if (digits.All(Uri.IsHexDigit))
            {
                try
                {
                    switch (digits.Length)
                    {
                        case 3:
                            return Color.FromArgb(255,
                                Convert.ToInt32(new string(digits[0], 2), 16),
                                Convert.ToInt32(new string(digits[1], 2), 16),
                                Convert.ToInt32(new string(digits[2], 2), 16));
                        case 6:
                            return Color.FromArgb(255,
                                Convert.ToInt32(digits[..2], 16),
                                Convert.ToInt32(digits[2..4], 16),
                                Convert.ToInt32(digits[4..6], 16));
                        case 8:
                            return Color.FromArgb(
                                Convert.ToInt32(digits[..2], 16),
                                Convert.ToInt32(digits[2..4], 16),
                                Convert.ToInt32(digits[4..6], 16),
                                Convert.ToInt32(digits[6..8], 16));
                    }
                }
                catch { }
                return fallback;
            }

            Color named = Color.FromName(text);
            return named.IsKnownColor ? named : fallback;
        }

        public static Color ParseColorKey(string key, Color fallback) => ParseColor(AppConfig.GetString(key), fallback);

        /// <summary>Chart area fill: base colour at one third brightness, alpha 128.</summary>
        public static Color ChartFill(Color color) => Color.FromArgb(128, color.R / 3, color.G / 3, color.B / 3);
    }
}
