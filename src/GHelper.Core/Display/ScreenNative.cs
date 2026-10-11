using GHelper.Platform;

namespace GHelper.Display
{

    /// <summary>
    /// Legacy ScreenNative (DisplayConfig / EnumDisplaySettings P/Invoke) as a facade over
    /// <see cref="IDisplayService"/>. The refresh-rate cache (config "screen_max") stays here.
    /// </summary>
    internal static class ScreenNative
    {
        public const int ENUM_CURRENT_SETTINGS = -1;
        public const string DefaultDevice = @"\\.\DISPLAY1";

        private static IDisplayService Display => PlatformServices.Current.Display;

        /// <summary>
        /// Returns true if at least one active display is not the built-in internal panel.
        /// </summary>
        public static bool IsExternalDisplayConnected(bool log = false)
        {
            try
            {
                return Display.IsExternalDisplayConnected(log);
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ex.ToString());
            }

            return false;
        }

        /// <summary>
        /// Finds the platform id of the internal laptop screen (Windows: \\.\DISPLAY1, Linux: eDP-1), null when the panel is off.
        /// </summary>
        public static string? FindLaptopScreen(bool log = false)
        {
            try
            {
                return Display.FindLaptopScreen(log);
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ex.Message);
                return null;
            }
        }

        public static int GetMaxRefreshRate(string? laptopScreen)
        {
            if (laptopScreen is null) return -1;

            int frequency = Display.GetMaxRefreshRate(laptopScreen);

            if (frequency > 0) AppConfig.Set("screen_max", frequency);
            else frequency = AppConfig.Get("screen_max");

            return frequency;
        }

        public static int GetRefreshRate(string? laptopScreen)
        {
            if (laptopScreen is null) return -1;
            return Display.GetRefreshRate(laptopScreen);
        }

        public static int SetRefreshRate(string laptopScreen, int frequency = 120)
        {
            int result = Display.SetRefreshRate(laptopScreen, frequency);
            Logger.WriteLine("Screen = " + frequency + "Hz : " + result);
            return result;
        }
    }
}
