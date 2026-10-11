using System.Text.RegularExpressions;

namespace GHelper.Input
{
    /// <summary>Pure parsing helpers of the input dispatcher (custom key combos, modifier config).</summary>
    public static class KeyCommand
    {
        /// <summary>
        /// "0x5B 0x50" / "5B 50" -> virtual key codes. Anything that is not a space separated list of 1-2 digit hex
        /// values yields an empty array (the command is then treated as a program to launch).
        /// </summary>
        public static int[] ParseHexValues(string input)
        {
            string pattern = @"\b(0x[0-9A-Fa-f]{1,2}|[0-9A-Fa-f]{1,2})\b";

            if (!Regex.IsMatch(input, $"^{pattern}(\\s+{pattern})*$")) return new int[0];

            MatchCollection matches = Regex.Matches(input, pattern);

            int[] hexValues = new int[matches.Count];

            for (int i = 0; i < matches.Count; i++)
            {
                string hexValueStr = matches[i].Value;
                int hexValue = int.Parse(hexValueStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? hexValueStr.Substring(2)
                    : hexValueStr, System.Globalization.NumberStyles.HexNumber);

                hexValues[i] = hexValue;
            }

            return hexValues;
        }

        /// <summary>"shift-control-alt-win" (config modifier_keybind*) to flags, <paramref name="defaultModifiers"/> when empty.</summary>
        public static ModifierKeys ParseModifiers(string? configValue, ModifierKeys defaultModifiers)
        {
            if (string.IsNullOrWhiteSpace(configValue))
                return defaultModifiers;

            ModifierKeys modifiers = ModifierKeys.None;
            HashSet<string> keys = new HashSet<string>(configValue.Split('-'), StringComparer.OrdinalIgnoreCase);

            if (keys.Contains("win")) modifiers |= ModifierKeys.Win;
            if (keys.Contains("shift")) modifiers |= ModifierKeys.Shift;
            if (keys.Contains("control")) modifiers |= ModifierKeys.Control;
            if (keys.Contains("alt")) modifiers |= ModifierKeys.Alt;

            return modifiers;
        }
    }

    /// <summary>Decoding of the ASUS HID input report (0x5A) the keyboard listener reads.</summary>
    public static class AsusKeyReport
    {
        /// <summary>
        /// Byte 1 is the ASUS hotkey code. 0 (key released) only matters on the Ally, 236 is a heartbeat the firmware
        /// sends all the time.
        /// </summary>
        public static bool TryGetKey(byte[] data, bool ally, out int key)
        {
            key = 0;
            if (data.Length <= 1 || data[0] != GHelper.USB.AsusHid.INPUT_ID) return false;

            if (data[1] > 0 && data[1] != 236)
            {
                key = data[1];
                return true;
            }

            return ally && data[1] == 0;
        }
    }
}
