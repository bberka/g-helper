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
}
