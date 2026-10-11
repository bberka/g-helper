using GHelper.Platform;
using System.Drawing;

namespace GHelper.Helpers
{
    /// <summary>Windows 11 Dynamic Lighting. Registry access lives in the platform (IVendorServices / IDynamicLightingEffects).</summary>
    public static class DynamicLightingHelper
    {
        public enum DynamicLightingEffect
        {
            Solid = 0,
            Breathing = 1,
            Rainbow = 2,
            Wave = 4,
            Wheel = 5,
            Gradient = 6
        }

        private static IVendorServices Svc => PlatformServices.Current.Vendor;

        public static bool IsEnabled()
        {
            if (!Svc.IsDynamicLightingSupported) return false;
            if (ProcessHelper.IsRunningAsSystem()) return false;
            return Svc.IsDynamicLightingEnabled();
        }

        public static void SetRegStatus(int status = 1)
        {
            try
            {
                Svc.SetDynamicLighting(status > 0);
                Logger.WriteLine($"Dynamic lighting: {status}");
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Error setting value for dynamic lighting: {ex.Message}");
            }
        }

        public static void SetEffect(
            DynamicLightingEffect effect,
            Color? color = null,
            Color? color2 = null,
            int? brightness = null,
            int? speed = null)
        {
            try
            {
                var effects = PlatformCapabilities.GetOptional<IDynamicLightingEffects>();
                if (effects is null) return;

                effects.SetEffect((int)effect, color, color2, brightness, speed);
                Logger.WriteLine($"Dynamic lighting effect set: {effect}");
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Dynamic lighting effect error: {ex}");
            }
        }

        public static void Init()
        {
            if (!AppConfig.IsDynamicLightingInit()) return;
            if (!Svc.IsDynamicLightingSupported) return;
            if (IsEnabled()) return;

            SetRegStatus(1);
            Thread.Sleep(1000);
            SetRegStatus(0);
        }

        public static void OpenSettings()
        {
            Svc.OpenDynamicLightingSettings();
        }


    }
}
