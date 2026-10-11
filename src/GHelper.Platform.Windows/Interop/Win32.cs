using System.Runtime.InteropServices;

namespace GHelper.Platform.Windows.Interop;

/// <summary>Small shared Win32 surface (kernel32 / user32). Larger, self contained groups keep their own declarations.</summary>
internal static class Win32
{
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 1;
    public const uint FILE_SHARE_WRITE = 2;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    // window messages
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_POWERBROADCAST = 0x0218;
    public const uint WM_APP = 0x8000;

    public const int PBT_APMSUSPEND = 0x0004;
    public const int PBT_APMRESUMESUSPEND = 0x0007;
    public const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    public const int PBT_POWERSETTINGCHANGE = 0x8013;

    public const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0;

    public static readonly IntPtr HWND_MESSAGE = new(-3);

    // keyboard / mouse injection
    public const uint KEYEVENTF_EXTENDEDKEY = 1;
    public const uint KEYEVENTF_KEYUP = 2;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x02;
    public const uint MOUSEEVENTF_LEFTUP = 0x04;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x08;
    public const uint MOUSEEVENTF_RIGHTUP = 0x10;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x20;
    public const uint MOUSEEVENTF_MIDDLEUP = 0x40;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;

    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;
    public const uint LLKHF_INJECTED = 0x10;

    // power
    public const int SC_MONITORPOWER = 0xF170;
    public const int MONITOR_OFF = 2;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    // --- kernel32 -----------------------------------------------------------------------------

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, byte[]? lpInBuffer, uint nInBufferSize,
        byte[]? lpOutBuffer, uint nOutBufferSize, ref uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, int dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetEvent(IntPtr hEvent);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetProcessWorkingSetSize(IntPtr hProcess, nint dwMin, nint dwMax);

    [DllImport("kernel32.dll")]
    public static extern bool GetPhysicallyInstalledSystemMemory(out long totalKb);

    // --- user32 -------------------------------------------------------------------------------

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, [In] ref Guid powerSettingGuid, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr RegisterSuspendResumeNotification(IntPtr hRecipient, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterSuspendResumeNotification(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool LockWorkStation();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    // --- powrprof -----------------------------------------------------------------------------

    [DllImport("powrprof.dll")]
    public static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}
