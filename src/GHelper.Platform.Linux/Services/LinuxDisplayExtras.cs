using System.Runtime.InteropServices;
using GHelper.USB;

namespace GHelper.Platform.Linux;

/// <summary>
/// The optional display capabilities of the Linux platform:
/// <list type="bullet">
/// <item><see cref="IDisplaySettingsEvents"/>: monitor hot plug, polled from the DRM connector state while somebody listens;</item>
/// <item><see cref="IScreenCapture"/>: X11 root window grab for the Aura "ambient" mode (not available on Wayland).</item>
/// </list>
/// Not provided (Windows-only ASUS software, see tasks/seams.md): ElmbAccess, SplendidDirectory, GameVisual, RefreshFlag.
/// </summary>
public sealed partial class LinuxDisplayService : IDisplaySettingsEvents, IScreenCapture, IDisposable
{
    private readonly object displayEventGate = new();
    private Action? displaySettingsChanged;
    private Timer? displayTimer;
    private string? displaySignature;

    /// <summary>Connector poll period; tests lower it or call <see cref="PollDisplays"/> directly.</summary>
    internal int DisplayPollMs { get; set; } = 2000;

    public event Action? DisplaySettingsChanged
    {
        add
        {
            lock (displayEventGate)
            {
                displaySettingsChanged += value;
                if (displayTimer is null && DisplayPollMs > 0)
                {
                    displaySignature = Signature();
                    displayTimer = new Timer(_ => PollDisplays(), null, DisplayPollMs, DisplayPollMs);
                }
            }
        }
        remove
        {
            lock (displayEventGate)
            {
                displaySettingsChanged -= value;
                if (displaySettingsChanged is null)
                {
                    displayTimer?.Dispose();
                    displayTimer = null;
                }
            }
        }
    }

    private string Signature() =>
        string.Join(';', EnumerateConnectors().Select(c =>
            $"{c.Name}={(c.Connected ? 1 : 0)}:{root.ReadText(c.SysfsDir + "/enabled")}:{root.ReadLines(c.SysfsDir + "/modes")?.FirstOrDefault()}"));

    /// <summary>One poll step: raises <see cref="DisplaySettingsChanged"/> when a connector appeared, went away or changed mode.</summary>
    internal void PollDisplays()
    {
        try
        {
            string signature = Signature();
            Action? handler;
            lock (displayEventGate)
            {
                if (signature == displaySignature) return;
                displaySignature = signature;
                handler = displaySettingsChanged;
            }
            handler?.Invoke();
        }
        catch (Exception ex)
        {
            LinuxLog.Write("display poll: " + ex.Message);
        }
    }

    public void Dispose()
    {
        lock (displayEventGate)
        {
            displayTimer?.Dispose();
            displayTimer = null;
            grab?.Dispose();
            grab = null;
            grabFailed = true;
        }
    }

    // ------------------------------------------------------------------ IScreenCapture (X11)

    private X11ScreenGrab? grab;
    private bool grabFailed;

    private X11ScreenGrab? Grab()
    {
        lock (displayEventGate)
        {
            if (grab is not null || grabFailed) return grab;
            // a Wayland compositor only exposes the (empty) XWayland root window: refuse instead of feeding black frames
            if (!ctx.IsX11Session || ctx.IsWaylandSession || !root.IsRealRoot)
            {
                grabFailed = true;
                return null;
            }
            grab = X11ScreenGrab.TryOpen();
            grabFailed = grab is null;
            return grab;
        }
    }

    bool IScreenCapture.IsSupported => Grab() is not null;

    System.Drawing.Rectangle IScreenCapture.GetPrimaryScreenBounds() => Grab()?.Bounds ?? System.Drawing.Rectangle.Empty;

    GHelper.AnimeMatrix.PixelBuffer? IScreenCapture.Capture(System.Drawing.Rectangle area, int outWidth, int outHeight)
        => Grab()?.Capture(area, outWidth, outHeight);

    // no cheap way to know that a window is being dragged on X11 / Wayland: capture anyway
    bool IScreenCapture.IsWindowMoveSize() => false;
}

/// <summary>libX11 root window grab, downscaled with nearest neighbour (the legacy StretchBlt DELETESCANS).</summary>
internal sealed unsafe class X11ScreenGrab : IDisposable
{
    private const int ZPixmap = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct XImage
    {
        public int width, height, xoffset, format;
        public nint data;
        public int byte_order, bitmap_unit, bitmap_bit_order, bitmap_pad, depth, bytes_per_line, bits_per_pixel;
        public nuint red_mask, green_mask, blue_mask;
    }

    [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(nint name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
    [DllImport("libX11.so.6")] private static extern int XDefaultScreen(nint display);
    [DllImport("libX11.so.6")] private static extern nuint XRootWindow(nint display, int screen);
    [DllImport("libX11.so.6")] private static extern int XDisplayWidth(nint display, int screen);
    [DllImport("libX11.so.6")] private static extern int XDisplayHeight(nint display, int screen);
    [DllImport("libX11.so.6")] private static extern nint XGetImage(nint display, nuint drawable, int x, int y, uint width, uint height, nuint planeMask, int format);
    [DllImport("libX11.so.6")] private static extern int XFree(nint data);

    private nint display;
    private readonly nuint root;
    private readonly int screen;
    private readonly object gate = new();

    private X11ScreenGrab(nint display)
    {
        this.display = display;
        screen = XDefaultScreen(display);
        root = XRootWindow(display, screen);
        Bounds = new System.Drawing.Rectangle(0, 0, XDisplayWidth(display, screen), XDisplayHeight(display, screen));
    }

    public System.Drawing.Rectangle Bounds { get; }

    public static X11ScreenGrab? TryOpen()
    {
        try
        {
            nint display = XOpenDisplay(0);
            return display == 0 ? null : new X11ScreenGrab(display);
        }
        catch (Exception ex)
        {
            LinuxLog.Write("X11 screen capture unavailable: " + ex.Message);
            return null;
        }
    }

    public GHelper.AnimeMatrix.PixelBuffer? Capture(System.Drawing.Rectangle area, int outWidth, int outHeight)
    {
        area.Intersect(Bounds);
        if (area.Width <= 0 || area.Height <= 0 || outWidth <= 0 || outHeight <= 0) return null;

        lock (gate)
        {
            if (display == 0) return null;

            nint handle = XGetImage(display, root, area.X, area.Y, (uint)area.Width, (uint)area.Height, nuint.MaxValue, ZPixmap);
            if (handle == 0) return null;

            try
            {
                XImage image = *(XImage*)handle;
                if (image.bits_per_pixel != 32 || image.data == 0) return null;

                var source = new ReadOnlySpan<byte>((void*)image.data, image.bytes_per_line * image.height);
                return Downsample(source, image.bytes_per_line, image.width, image.height, outWidth, outHeight,
                    redFirst: image.red_mask == 0xFF);
            }
            finally
            {
                // XDestroyImage is a macro for exactly this: free the pixel data, then the XImage itself
                XImage image = *(XImage*)handle;
                if (image.data != 0) XFree(image.data);
                XFree(handle);
            }
        }
    }

    /// <summary>Nearest neighbour sample of a 32 bit ZPixmap (B G R X, or R G B X when <paramref name="redFirst"/>).</summary>
    internal static GHelper.AnimeMatrix.PixelBuffer Downsample(ReadOnlySpan<byte> source, int stride, int width, int height,
        int outWidth, int outHeight, bool redFirst = false)
    {
        var result = new GHelper.AnimeMatrix.PixelBuffer(outWidth, outHeight);
        for (int y = 0; y < outHeight; y++)
        {
            int sy = (int)((long)y * height / outHeight);
            for (int x = 0; x < outWidth; x++)
            {
                int sx = (int)((long)x * width / outWidth);
                int s = sy * stride + sx * 4;
                int d = (y * outWidth + x) * 4;
                byte first = source[s], second = source[s + 1], third = source[s + 2];
                result.Data[d] = redFirst ? first : third;
                result.Data[d + 1] = second;
                result.Data[d + 2] = redFirst ? third : first;
                result.Data[d + 3] = 255;
            }
        }
        return result;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (display == 0) return;
            try { XCloseDisplay(display); } catch { }
            display = 0;
        }
    }
}
