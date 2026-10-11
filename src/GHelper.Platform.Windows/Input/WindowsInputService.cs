using System.Runtime.Versioning;
using GHelper.Input;
using GHelper.Platform.Windows.Interop;

namespace GHelper.Platform.Windows.Input;

/// <summary>
/// Windows implementation of <see cref="IInputService"/>: key injection via keybd_event/mouse_event, global hotkeys via
/// RegisterHotKey on a hidden message-only window, passive key observation via a low level keyboard hook and the touch
/// keyboard. Lifted from legacy KeyboardHook / OnScreenKeyboard / NativeMethods.
/// </summary>
public sealed class WindowsInputService : IInputService
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

    public bool IsKeyDown(Keys key)
    {
        if (!OperatingSystem.IsWindows()) return false;
        return (Win32.GetAsyncKeyState(KeyMapper.ToVirtualKey(key)) & 0x8000) != 0;
    }

    public TimeSpan GetIdleTime()
    {
        if (!OperatingSystem.IsWindows()) return TimeSpan.Zero;

        var info = new Win32.LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.LASTINPUTINFO>() };
        if (!Win32.GetLastInputInfo(ref info)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds((uint)Environment.TickCount - info.dwTime);
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
