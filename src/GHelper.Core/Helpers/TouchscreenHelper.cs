using GHelper.Platform;

public static class TouchscreenHelper
{

    /// <returns>true = enabled, false = disabled, null = no touchscreen / unsupported.</returns>
    public static bool? GetStatus()
    {
        try
        {
            return PlatformServices.Current.Display.GetTouchscreenStatus();
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Can't get touchscreen status: {ex.Message}");
            return null;
        }
    }

    public static void ToggleTouchscreen(bool status)
    {
        try
        {
            PlatformServices.Current.Display.SetTouchscreenStatus(status);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Can't toggle touchscreen: {ex.Message}");
        }

    }
}
