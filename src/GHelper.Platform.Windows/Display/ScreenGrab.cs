using System.Drawing;
using System.Runtime.InteropServices;
using GHelper.AnimeMatrix;

namespace GHelper.Platform.Windows.Display;

/// <summary>
/// GDI desktop grab for the Aura "ambient" mode (legacy Aura.AmbientData.CamptureScreen / IsMoveSize): StretchBlt of the
/// desktop DC with STRETCH_DELETESCANS into a small memory bitmap, read back as BGRA.
/// </summary>
internal static class ScreenGrab
{
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int STRETCH_DELETESCANS = 3;
    private const int SRCCOPY = 0x00CC0020;
    private const int DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint idThread, int[] gui);

    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, int rop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[]? bits, ref BITMAPINFO info, uint usage);

    /// <summary>Legacy Screen.GetBounds(Point.Empty): the primary screen at the origin.</summary>
    public static Rectangle GetPrimaryScreenBounds()
    {
        if (!OperatingSystem.IsWindows()) return Rectangle.Empty;
        return new Rectangle(0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
    }

    /// <summary>True while a window of the foreground thread is being dragged / resized (GUI_INMOVESIZE).</summary>
    public static bool IsWindowMoveSize()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            int[] gui = new int[18];
            gui[0] = 72; // cbSize of GUITHREADINFO
            return GetGUIThreadInfo(0, gui) && (gui[1] & 0x2) != 0;
        }
        catch
        {
            return false;
        }
    }

    public static PixelBuffer? Capture(Rectangle area, int outWidth, int outHeight)
    {
        if (!OperatingSystem.IsWindows() || outWidth <= 0 || outHeight <= 0 || area.Width <= 0 || area.Height <= 0) return null;

        IntPtr desktop = GetDesktopWindow();
        IntPtr hdc = GetWindowDC(desktop);
        if (hdc == IntPtr.Zero) return null;

        IntPtr hdcMem = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;

        try
        {
            hdcMem = CreateCompatibleDC(hdc);
            hBitmap = CreateCompatibleBitmap(hdc, outWidth, outHeight);
            if (hdcMem == IntPtr.Zero || hBitmap == IntPtr.Zero) return null;

            IntPtr old = SelectObject(hdcMem, hBitmap);
            SetStretchBltMode(hdcMem, STRETCH_DELETESCANS);
            bool blt = StretchBlt(hdcMem, 0, 0, outWidth, outHeight, hdc, area.X, area.Y, area.Width, area.Height, SRCCOPY);
            SelectObject(hdcMem, old);
            if (!blt) return null;

            var info = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = outWidth,
                    biHeight = -outHeight, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                },
            };

            var bgra = new byte[outWidth * outHeight * 4];
            if (GetDIBits(hdcMem, hBitmap, 0, (uint)outHeight, bgra, ref info, DIB_RGB_COLORS) == 0) return null;

            for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255; // GDI leaves alpha at 0
            return PixelBuffer.FromBgra(outWidth, outHeight, bgra);
        }
        finally
        {
            if (hdcMem != IntPtr.Zero) DeleteDC(hdcMem);
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            ReleaseDC(desktop, hdc);
        }
    }
}
