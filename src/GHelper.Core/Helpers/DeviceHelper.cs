using GHelper.Platform;

namespace GHelper.Helpers
{
    public static class DeviceHelper
    {
        /// <summary>Human readable GPU device problem (Windows CM_Get_DevNode_Status), null if healthy / unknown.</summary>
        public static string? GetGpuError()
        {
            try
            {
                return PlatformServices.Current.Vendor.GetGpuError();
            }
            catch (Exception ex)
            {
                Logger.WriteLine("GPU error check failed: " + ex.Message);
                return null;
            }
        }
    }
}
