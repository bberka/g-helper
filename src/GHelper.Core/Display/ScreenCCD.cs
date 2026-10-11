using GHelper.Platform;

namespace GHelper.Display
{
    /// <summary>Legacy ScreenCCD (QueryDisplayConfig advanced colour info): HDR / ACM state of the internal panel.</summary>
    public class ScreenCCD
    {

        public static bool GetHDRStatus(bool log = true) => GetHDRStatus(out _, log);

        public static bool IsHDR()
        {
            try { return GetHDRStatus(false); } catch { return false; }
        }

        public static bool GetHDRStatus(out bool acm, bool log = true)
        {
            return PlatformServices.Current.Display.GetHdrStatus(out acm, log);
        }

    }
}
