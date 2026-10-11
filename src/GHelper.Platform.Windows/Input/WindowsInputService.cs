using System.Runtime.InteropServices;
using GHelper.Input;
using GHelper.Platform.Windows.Interop;
using GHelper.Platform.Windows.Vendor;
using Microsoft.Win32;

namespace GHelper.Platform.Windows.Input;

/// <summary>
/// Windows implementation of <see cref="IInputService"/>: key injection via keybd_event/mouse_event, global hotkeys via
/// RegisterHotKey on a hidden message-only window, passive key observation via a low level keyboard hook and the touch
/// keyboard. Also implements <see cref="IInputExtras"/> (idle time, modifier state, screen off, touchpad / slate mode
/// registry values, ASUS software folder) and <see cref="INumberPadDriver"/> (\\.\AsusTPPC IOCTL).
/// Lifted from legacy KeyboardHook / OnScreenKeyboard / NativeMethods / InputDispatcher / NumberPad.
/// </summary>
public sealed class WindowsInputService : IInputService, IInputExtras, INumberPadDriver
{
    private readonly object sync = new();
    private MessageWindow? window;
    private LowLevelKeyboardListener? listener;
    private readonly Dictionary<int, Action> hotkeys = new();
    private int nextHotkeyId;
    private bool disposed;

    // --- injection ----------------------------------------------------------------------------

    public void KeyPress(Keys key)
    {
        if (!OperatingSystem.IsWindows()) return;

        switch (key)
        {
            case Keys.LButton:
                Win32.mouse_event(Win32.MOUSEEVENTF_LEFTDOWN | Win32.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                return;
            case Keys.RButton:
                Win32.mouse_event(Win32.MOUSEEVENTF_RIGHTDOWN | Win32.MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                return;
            case Keys.MButton:
                Win32.mouse_event(Win32.MOUSEEVENTF_MIDDLEDOWN | Win32.MOUSEEVENTF_MIDDLEUP, 0, 0, 0, UIntPtr.Zero);
                return;
        }

        Press(key);
        Thread.Sleep(1);
        Release(key);
    }

    public void KeyKeyPress(Keys key, Keys key2)
    {
        if (!OperatingSystem.IsWindows()) return;

        Press(key);
        Press(key2);

        Thread.Sleep(1);

        Release(key2);
        Release(key);
    }

    public void KeyKeyKeyPress(Keys key, Keys key2, Keys key3, int sleep = 1, int interSleep = 0)
    {
        if (!OperatingSystem.IsWindows()) return;

        Press(key);
        Thread.Sleep(interSleep);
        Press(key2);
        Thread.Sleep(interSleep);
        Press(key3);

        Thread.Sleep(sleep);

        Release(key3);
        Release(key2);
        Release(key);
    }

    public void KeyKeyKeyKeyPress(Keys key, Keys key2, Keys key3, Keys key4, int sleep = 1)
    {
        if (!OperatingSystem.IsWindows()) return;

        Press(key);
        Press(key2);
        Press(key3);
        Press(key4);

        Thread.Sleep(sleep);

        Release(key4);
        Release(key3);
        Release(key2);
        Release(key);
    }

    private static void Press(Keys key) =>
        Win32.keybd_event(KeyMapper.ToVirtualKey(key), 0, Win32.KEYEVENTF_EXTENDEDKEY, IntPtr.Zero);

    private static void Release(Keys key) =>
        Win32.keybd_event(KeyMapper.ToVirtualKey(key), 0, Win32.KEYEVENTF_EXTENDEDKEY | Win32.KEYEVENTF_KEYUP, IntPtr.Zero);

    public void MouseWheel(int delta)
    {
        if (!OperatingSystem.IsWindows()) return;
        Win32.mouse_event(Win32.MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta), UIntPtr.Zero);
    }

    // --- IInputExtras -------------------------------------------------------------------------

    private static bool IsKeyDown(Keys key) =>
        (Win32.GetAsyncKeyState(KeyMapper.ToVirtualKey(key)) & 0x8000) != 0;

    /// <summary>Legacy Control.ModifierKeys: Keys.Shift / Keys.Control / Keys.Alt flags of the keys physically held down.</summary>
    public Keys GetModifierKeys()
    {
        if (!OperatingSystem.IsWindows()) return Keys.None;

        Keys modifiers = Keys.None;
        if (IsKeyDown(Keys.ShiftKey)) modifiers |= Keys.Shift;
        if (IsKeyDown(Keys.ControlKey)) modifiers |= Keys.Control;
        if (IsKeyDown(Keys.Menu)) modifiers |= Keys.Alt;
        return modifiers;
    }

    public TimeSpan GetIdleTime()
    {
        if (!OperatingSystem.IsWindows()) return TimeSpan.Zero;

        var info = new Win32.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<Win32.LASTINPUTINFO>() };
        if (!Win32.GetLastInputInfo(ref info)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds((uint)Environment.TickCount - info.dwTime);
    }

    /// <summary>Legacy NativeMethods.TurnOffScreen: SC_MONITORPOWER has to be sent to a window of this process.</summary>
    public void TurnOffScreen()
    {
        if (!OperatingSystem.IsWindows()) return;

        IntPtr target = GetWindow()?.Handle ?? IntPtr.Zero;
        if (target == IntPtr.Zero) target = Win32.GetDesktopWindow();

        Win32.SendMessage(target, Win32.WM_SYSCOMMAND, (IntPtr)Win32.SC_MONITORPOWER, (IntPtr)Win32.MONITOR_OFF);
        int error = Marshal.GetLastWin32Error();
        if (error != 0) Logger.WriteLine($"Turn off screen: error code {error}");
    }

    public bool? GetTouchpadEnabled()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status", false);
            string? value = key?.GetValue("Enabled")?.ToString();
            return value is null ? null : value == "1";
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Touchpad status: " + ex.Message);
            return null;
        }
    }

    public void SetSlateMode(int status)
    {
        if (!OperatingSystem.IsWindows()) return;

        // Core logs success / failure around this call
        Registry.SetValue(@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control\PriorityControl", "ConvertibleSlateMode", status, RegistryValueKind.DWord);
    }

    public string? GetAsusSoftwarePath() => AsusDriverPath.Find();

    // --- INumberPadDriver ---------------------------------------------------------------------

    private const string NumberPadDevice = @"\\.\AsusTPPC";

    /// <summary>Legacy NumberPad.Ioctl: the 0x800 byte request is built by Core, the reply is written back into the buffer.</summary>
    public bool NumberPadIoctl(uint code, byte[] buffer)
    {
        if (!OperatingSystem.IsWindows()) return false;

        IntPtr device = Win32.CreateFile(NumberPadDevice, Win32.GENERIC_READ | Win32.GENERIC_WRITE, 3, IntPtr.Zero,
            Win32.OPEN_EXISTING, 0, IntPtr.Zero);
        if (device == IntPtr.Zero || device == Win32.INVALID_HANDLE_VALUE) return false;

        try
        {
            uint returned = 0;
            return Win32.DeviceIoControl(device, code, buffer, (uint)buffer.Length, buffer, (uint)buffer.Length, ref returned, IntPtr.Zero);
        }
        finally
        {
            Win32.CloseHandle(device);
        }
    }

    // --- hotkeys / key tap --------------------------------------------------------------------

    private MessageWindow? GetWindow()
    {
        if (!OperatingSystem.IsWindows() || disposed) return null;

        lock (sync)
        {
            if (window is null)
            {
                window = new MessageWindow();
                window.MessageReceived += OnMessage;
            }

            return window.IsAlive ? window : null;
        }
    }

    public IDisposable RegisterHotkey(HotkeyModifiers modifiers, Keys key, Action handler)
    {
        var win = GetWindow();
        if (win is null) return NullDisposable.Instance;

        int id = Interlocked.Increment(ref nextHotkeyId);

        bool ok = win.Invoke(() => Win32.RegisterHotKey(win.Handle, id, KeyMapper.ToWin32Modifiers(modifiers), KeyMapper.ToVirtualKey(key)));
        if (!ok)
        {
            Logger.WriteLine("Couldn't register " + key);
            return NullDisposable.Instance;
        }

        lock (sync) hotkeys[id] = handler;
        return new HotkeyRegistration(this, id);
    }

    private void Unregister(int id)
    {
        lock (sync) hotkeys.Remove(id);

        var win = window;
        if (win is null || !win.IsAlive) return;

        try { win.Invoke(() => Win32.UnregisterHotKey(win.Handle, id)); }
        catch { }
    }

    private void OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != Win32.WM_HOTKEY) return;

        Action? handler;
        lock (sync) hotkeys.TryGetValue((int)wParam, out handler);
        if (handler is null) return;

        // never run user code on the message pump
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { handler(); }
            catch (Exception ex) { Logger.WriteLine("Hotkey handler: " + ex.Message); }
        });
    }

    public IDisposable ListenKeys(Action<Keys, bool> handler)
    {
        var win = GetWindow();
        if (win is null) return NullDisposable.Instance;

        lock (sync) listener ??= new LowLevelKeyboardListener(win);
        return listener.Subscribe(handler);
    }

    // --- on screen keyboard -------------------------------------------------------------------

    public bool OnScreenKeyboardSupported => OperatingSystem.IsWindows();

    public void ToggleOnScreenKeyboard()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            if (WindowsOnScreenKeyboard.IsOpen()) WindowsOnScreenKeyboard.Hide();
            else WindowsOnScreenKeyboard.Show();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("On screen keyboard: " + ex.Message);
        }
    }

    public bool IsOnScreenKeyboardOpen()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try { return WindowsOnScreenKeyboard.IsOpen(); }
        catch { return false; }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
        }

        listener?.Dispose();

        var win = window;
        if (win is not null)
        {
            int[] ids;
            lock (sync) ids = hotkeys.Keys.ToArray();
            foreach (int id in ids)
            {
                try { win.Invoke(() => Win32.UnregisterHotKey(win.Handle, id)); } catch { }
            }

            win.Dispose();
        }
    }

    private sealed class HotkeyRegistration : IDisposable
    {
        private WindowsInputService? owner;
        private readonly int id;

        public HotkeyRegistration(WindowsInputService owner, int id)
        {
            this.owner = owner;
            this.id = id;
        }

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Unregister(id);
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}
