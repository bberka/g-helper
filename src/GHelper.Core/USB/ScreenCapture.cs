using GHelper.AnimeMatrix;
using GHelper.Platform;
using System.Drawing;

namespace GHelper.USB
{
    /// <summary>
    /// Optional platform capability behind the Aura "Ambient" mode: a downscaled copy of the primary screen.
    /// Windows: GDI StretchBlt of the desktop DC (legacy AmbientData.CamptureScreen) + GetGUIThreadInfo for the move/size check.
    /// Linux: best effort (portal / X11), unsupported on pure Wayland without a portal.
    /// Implement it on the platform's <see cref="IDisplayService"/> object (or register it through
    /// <see cref="ScreenCapture.Override"/>); when absent the Ambient mode is simply a no-op.
    /// </summary>
    public interface IScreenCapture
    {
        bool IsSupported { get; }

        /// <summary>Bounds of the primary screen in pixels (legacy Screen.GetBounds(Point.Empty)).</summary>
        Rectangle GetPrimaryScreenBounds();

        /// <summary>Capture <paramref name="area"/> of the screen scaled to outWidth x outHeight (nearest / delete-scans is fine).</summary>
        PixelBuffer? Capture(Rectangle area, int outWidth, int outHeight);

        /// <summary>True while the user drags or resizes a window (capturing then is pointless and slow).</summary>
        bool IsWindowMoveSize();
    }

    public static class ScreenCapture
    {
        public static IScreenCapture? Override { get; set; }

        public static IScreenCapture? Current
        {
            get
            {
                if (Override is not null) return Override;
                if (!PlatformServices.IsRegistered) return null;
                return PlatformServices.Current.Display as IScreenCapture;
            }
        }
    }
}
