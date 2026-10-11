using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace GHelper.App.Views.Overlay;

/// <summary>
/// The small amount of native window plumbing the overlay needs and Avalonia does not expose: mouse click-through,
/// "never take focus", staying above topmost game windows, and a global cursor / modifier probe for the drag keys.
/// Windows: user32. X11 (also XWayland): XShape input region + WM input hint + XQueryPointer.
/// Pure Wayland has no client side API for any of this; there the window is a normal borderless topmost window
/// (best effort). Every call is a no-op when the platform handle is missing, never throws.
/// </summary>
internal static class NativeOverlay
{
    // ---- shared ---------------------------------------------------------------------------------------

    private static (nint handle, string? kind) Handle(Window window)
    {
        try
        {
            var h = window.TryGetPlatformHandle();
            return h is null ? (0, null) : (h.Handle, h.HandleDescriptor);
        }
        catch
        {
            return (0, null);
        }
    }

    private static bool IsX11(string? kind) => kind == "XID";

    /// <summary>True when <see cref="SetClickThrough"/> can really make mouse events pass through on this window.</summary>
    public static bool CanClickThrough(Window window)
    {
        var (h, kind) = Handle(window);
        return h != 0 && ((OperatingSystem.IsWindows() && kind == "HWND") || (OperatingSystem.IsLinux() && IsX11(kind)));
    }

    /// <summary>Style the freshly shown window: no taskbar / alt-tab entry, never activated, click-through when asked.</summary>
    public static void Prepare(Window window, bool clickThrough)
    {
        var (h, kind) = Handle(window);
        if (h == 0) return;

        try
        {
            if (OperatingSystem.IsWindows() && kind == "HWND") Win32.Prepare(h, clickThrough);
            else if (OperatingSystem.IsLinux() && IsX11(kind)) X11.Prepare(h, clickThrough);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            AppLog.Write("Overlay native styles unavailable: " + ex.Message);
        }
    }

    public static void SetClickThrough(Window window, bool on)
    {
        var (h, kind) = Handle(window);
        if (h == 0) return;

        try
        {
            if (OperatingSystem.IsWindows() && kind == "HWND") Win32.SetClickThrough(h, on);
            else if (OperatingSystem.IsLinux() && IsX11(kind)) X11.SetClickThrough(h, on);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            AppLog.Write("Overlay click-through unavailable: " + ex.Message);
        }
    }

    /// <summary>Re-assert the topmost position when another topmost window (a fullscreen game) got above us. Windows only.</summary>
    public static void KeepOnTop(Window window)
    {
        var (h, kind) = Handle(window);
        if (h == 0 || !OperatingSystem.IsWindows() || kind != "HWND") return;
        try { Win32.KeepOnTop(h); } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>
    /// Global cursor position (device pixels) and whether Ctrl+Shift+Alt are all down, independent of which window has
    /// the focus. False when the platform cannot tell (then only the hardware drag key / button works).
    /// </summary>
    public static bool TryProbe(Window window, out PixelPoint cursor, out bool dragKeys)
    {
        cursor = default;
        dragKeys = false;
        var (h, kind) = Handle(window);

        try
        {
            if (OperatingSystem.IsWindows() && kind == "HWND") return Win32.Probe(out cursor, out dragKeys);
            if (OperatingSystem.IsLinux() && IsX11(kind)) return X11.Probe(out cursor, out dragKeys);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        return false;
    }

    // ---- Windows --------------------------------------------------------------------------------------

    private static class Win32
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
        private const uint GW_HWNDPREV = 3;
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
        private static readonly nint HWND_TOPMOST = -1;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern nint GetWindow(nint hWnd, uint uCmd);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);

        public static void Prepare(nint hwnd, bool clickThrough)
        {
            int style = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, style);
            SetClickThrough(hwnd, clickThrough);
        }

        public static void SetClickThrough(nint hwnd, bool on)
        {
            int style = GetWindowLong(hwnd, GWL_EXSTYLE);
            // a layered + transparent window is skipped by the hit test, the pointer goes to whatever is below
            style = on ? style | WS_EX_TRANSPARENT | WS_EX_LAYERED : style & ~WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, style);
        }

        public static void KeepOnTop(nint hwnd)
        {
            if (GetWindow(hwnd, GW_HWNDPREV) != 0)
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public static bool Probe(out PixelPoint cursor, out bool dragKeys)
        {
            bool ok = GetCursorPos(out var p);
            cursor = new PixelPoint(p.X, p.Y);
            dragKeys = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
                && (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0
                && (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            return ok;
        }
    }

    // ---- X11 ------------------------------------------------------------------------------------------

    private static class X11
    {
        private const int ShapeInput = 2;
        private const int ShapeSet = 0;
        private const int Unsorted = 0;
        private const int InputHint = 1;
        private const uint ShiftMask = 1, ControlMask = 4, Mod1Mask = 8;

        [StructLayout(LayoutKind.Sequential)]
        private struct XWMHints
        {
            public nint flags;
            public int input;
            public int initial_state;
            public nuint icon_pixmap;
            public nuint icon_window;
            public int icon_x, icon_y;
            public nuint icon_mask;
            public nuint window_group;
        }

        [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(nint name);
        [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
        [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
        [DllImport("libX11.so.6")] private static extern nuint XDefaultRootWindow(nint display);
        [DllImport("libX11.so.6")] private static extern int XSetWMHints(nint display, nuint window, ref XWMHints hints);
        [DllImport("libX11.so.6")]
        private static extern bool XQueryPointer(nint display, nuint window, out nuint root, out nuint child,
            out int rootX, out int rootY, out int winX, out int winY, out uint mask);

        [DllImport("libXext.so.6")]
        private static extern void XShapeCombineRectangles(nint display, nuint dest, int destKind, int xOff, int yOff,
            nint rectangles, int count, int op, int ordering);

        [DllImport("libXext.so.6")]
        private static extern void XShapeCombineMask(nint display, nuint dest, int destKind, int xOff, int yOff, nuint mask, int op);

        public static void Prepare(nint xid, bool clickThrough)
        {
            nint dpy = XOpenDisplay(0);
            if (dpy == 0) return;
            try
            {
                // WM input hint false: the window manager never gives this window the keyboard focus
                var hints = new XWMHints { flags = InputHint, input = 0 };
                XSetWMHints(dpy, (nuint)xid, ref hints);
                ApplyInputRegion(dpy, xid, clickThrough);
                XFlush(dpy);
            }
            finally
            {
                XCloseDisplay(dpy);
            }
        }

        public static void SetClickThrough(nint xid, bool on)
        {
            nint dpy = XOpenDisplay(0);
            if (dpy == 0) return;
            try
            {
                ApplyInputRegion(dpy, xid, on);
                XFlush(dpy);
            }
            finally
            {
                XCloseDisplay(dpy);
            }
        }

        private static void ApplyInputRegion(nint dpy, nint xid, bool clickThrough)
        {
            if (clickThrough)
                XShapeCombineRectangles(dpy, (nuint)xid, ShapeInput, 0, 0, 0, 0, ShapeSet, Unsorted); // empty input region
            else
                XShapeCombineMask(dpy, (nuint)xid, ShapeInput, 0, 0, 0, ShapeSet); // None: back to the full window
        }

        public static bool Probe(out PixelPoint cursor, out bool dragKeys)
        {
            cursor = default;
            dragKeys = false;

            nint dpy = XOpenDisplay(0);
            if (dpy == 0) return false;
            try
            {
                bool ok = XQueryPointer(dpy, XDefaultRootWindow(dpy), out _, out _, out int rx, out int ry, out _, out _, out uint mask);
                cursor = new PixelPoint(rx, ry);
                const uint all = ShiftMask | ControlMask | Mod1Mask;
                dragKeys = (mask & all) == all;
                return ok;
            }
            finally
            {
                XCloseDisplay(dpy);
            }
        }
    }
}
