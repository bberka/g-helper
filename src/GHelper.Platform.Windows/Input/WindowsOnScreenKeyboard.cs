using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GHelper.Platform.Windows.Interop;

namespace GHelper.Platform.Windows.Input;

[ComImport]
[Guid("37c994e7-432b-4834-a2f7-dce1f13b834b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITipInvocation
{
    void Toggle(IntPtr hwnd);
}

/// <summary>Touch keyboard (TabTip) control. Lifted from legacy app/Helpers/OnScreenKeyboard.cs.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsOnScreenKeyboard
{
    private const string TabTipPath = @"C:\Program Files\Common Files\Microsoft Shared\ink\TabTip.exe";
    private const string TabTipWindowClass = "IPTIP_Main_Window";
    private const string WindowClass = "IPTip_Main_Window";
    private const string WindowParentClass1709 = "ApplicationFrameWindow";
    private const string WindowClass1709 = "Windows.UI.Core.CoreWindow";
    private const string WindowCaption1709 = "Microsoft Text Input Application";

    private const uint WS_DISABLED = 0x08000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const int GWL_STYLE = -16;
    private const int SC_CLOSE = 0xF060;

    private static void StartTabTip()
    {
        Process.Start(TabTipPath)?.Dispose();

        // wait for the window to appear (bounded, legacy looped forever)
        for (int i = 0; i < 50 && Win32.FindWindow(TabTipWindowClass, "") == IntPtr.Zero; i++)
            Thread.Sleep(100);
    }

    private static void ToggleVisibility()
    {
        var type = Type.GetTypeFromCLSID(Guid.Parse("4ce576fa-83dc-4F88-951c-9d0782b4e376"));
        if (type is null) return;

        var instance = (ITipInvocation)Activator.CreateInstance(type)!;
        try { instance.Toggle(Win32.GetDesktopWindow()); }
        finally { Marshal.ReleaseComObject(instance); }
    }

    public static void Show()
    {
        if (Win32.FindWindow(TabTipWindowClass, "") == IntPtr.Zero)
        {
            StartTabTip();
            Thread.Sleep(100);
        }

        // on some devices starting TabTip does not show the keyboard, on some it does
        if (!IsOpen()) ToggleVisibility();
    }

    public static void Hide()
    {
        if (IsOpen()) ToggleVisibility();
    }

    public static bool Close()
    {
        IntPtr handle = Win32.FindWindow(TabTipWindowClass, "");
        if (handle == IntPtr.Zero) return false;

        Win32.SendMessage(handle, Win32.WM_SYSCOMMAND, (IntPtr)SC_CLOSE, IntPtr.Zero);
        return true;
    }

    public static bool IsOpen() => GetIsOpen1709() ?? GetIsOpenLegacy();

    private static bool? GetIsOpen1709()
    {
        // a top level window means the keyboard is closed
        IntPtr wnd = Win32.FindWindowEx(IntPtr.Zero, IntPtr.Zero, WindowClass1709, WindowCaption1709);
        if (wnd != IntPtr.Zero) return false;

        IntPtr parent = IntPtr.Zero;
        while (true)
        {
            parent = Win32.FindWindowEx(IntPtr.Zero, parent, WindowParentClass1709, null);
            if (parent == IntPtr.Zero) return null; // state unknown

            // a child of an ApplicationFrameWindow means the keyboard is open
            wnd = Win32.FindWindowEx(parent, IntPtr.Zero, WindowClass1709, WindowCaption1709);
            if (wnd != IntPtr.Zero) return true;
        }
    }

    private static bool GetIsOpenLegacy()
    {
        IntPtr wnd = Win32.FindWindowEx(IntPtr.Zero, IntPtr.Zero, WindowClass, null);
        if (wnd == IntPtr.Zero) return false;

        uint style = (uint)Win32.GetWindowLong(wnd, GWL_STYLE);
        return (style & WS_VISIBLE) != 0 && (style & WS_DISABLED) == 0;
    }
}
