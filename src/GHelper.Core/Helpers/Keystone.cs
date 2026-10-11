using GHelper.Platform;

namespace GHelper.Helpers
{
    /// <summary>Windows "Proximity Connection" sound (Keystone). No-op on other platforms.</summary>
    public static class Keystone
    {
        public static bool IsEnabled()
        {
            try
            {
                return PlatformServices.Current.Vendor.IsKeystoneEnabled();
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Keystone: failed to read sound status: {ex.Message}");
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                PlatformServices.Current.Vendor.SetKeystoneEnabled(enabled);
                Logger.WriteLine($"Keystone: sound {(enabled ? "enabled" : "disabled")}");
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Keystone: failed to set sound status: {ex.Message}");
            }
        }
    }
}
