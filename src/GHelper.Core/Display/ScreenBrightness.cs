using GHelper.Platform;

namespace GHelper.Display
{
    /// <summary>Panel backlight (legacy: WMI WmiMonitorBrightness) through <see cref="IDisplayService"/>.</summary>
    public static class ScreenBrightness
    {
        public static int Get()
        {
            return Math.Max(0, PlatformServices.Current.Display.GetBrightness());
        }

        public static void Set(int brightness)
        {
            PlatformServices.Current.Display.SetBrightness(brightness);
        }

        public static int Adjust(int delta)
        {
            int brightness = Get();
            brightness = Math.Min(100, Math.Max(0, brightness + delta));
            Set(brightness);
            return brightness;
        }

    }
}
