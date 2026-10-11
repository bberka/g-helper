using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using GHelper.Input;
using GHelper.Platform.Windows.Interop;

namespace GHelper.Platform.Windows.Input;

/// <summary>
/// WH_KEYBOARD_LL hook shared by every <c>ListenKeys</c> subscriber. The hook is installed on the message window thread
/// (it needs a message pump) and only forwards events: injected keystrokes are skipped, delivery to handlers happens on a
/// dedicated worker so the hook procedure returns immediately (Windows drops hooks that block).
/// </summary>
internal sealed class LowLevelKeyboardListener : IDisposable
{
    private readonly MessageWindow window;
    private readonly object sync = new();
    private readonly List<Action<Keys, bool>> handlers = new();
    private readonly BlockingCollection<(Keys key, bool down)> queue = new();
    private Win32.HookProc? hookProc;
    private IntPtr hook;
    private Thread? worker;

    public LowLevelKeyboardListener(MessageWindow window)
    {
        this.window = window;
    }

    public IDisposable Subscribe(Action<Keys, bool> handler)
    {
        lock (sync)
        {
            handlers.Add(handler);
            if (handlers.Count == 1) Install();
        }

        return new Subscription(this, handler);
    }

    private void Unsubscribe(Action<Keys, bool> handler)
    {
        lock (sync)
        {
            handlers.Remove(handler);
            if (handlers.Count == 0) Uninstall();
        }
    }

    private void Install()
    {
        if (!window.IsAlive) return;

        worker ??= new Thread(Pump) { IsBackground = true, Name = "GHelper key events" };
        if (!worker.IsAlive && worker.ThreadState == ThreadState.Unstarted) worker.Start();

        window.Invoke(() =>
        {
            if (hook != IntPtr.Zero) return;
            hookProc = HookCallback;
            hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, hookProc, Win32.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
                Logger.WriteLine("Keyboard hook failed: " + Marshal.GetLastWin32Error());
        });
    }

    private void Uninstall()
    {
        if (hook == IntPtr.Zero) return;

        window.Invoke(() =>
        {
            if (hook != IntPtr.Zero) Win32.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
            hookProc = null;
        });
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                int message = (int)wParam;
                var data = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);

                bool injected = (data.flags & Win32.LLKHF_INJECTED) != 0;
                bool down = message is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN;
                bool up = message is Win32.WM_KEYUP or Win32.WM_SYSKEYUP;

                if (!injected && (down || up))
                    queue.TryAdd((KeyMapper.FromVirtualKey(data.vkCode), down));
            }
            catch
            {
                // never let an exception escape the hook procedure
            }
        }

        return Win32.CallNextHookEx(hook, nCode, wParam, lParam);
    }

    private void Pump()
    {
        foreach (var (key, down) in queue.GetConsumingEnumerable())
        {
            Action<Keys, bool>[] snapshot;
            lock (sync) snapshot = handlers.ToArray();

            foreach (var handler in snapshot)
            {
                try { handler(key, down); }
                catch (Exception ex) { Logger.WriteLine("Key listener: " + ex.Message); }
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            handlers.Clear();
            Uninstall();
        }

        queue.CompleteAdding();
    }

    private sealed class Subscription : IDisposable
    {
        private LowLevelKeyboardListener? owner;
        private readonly Action<Keys, bool> handler;

        public Subscription(LowLevelKeyboardListener owner, Action<Keys, bool> handler)
        {
            this.owner = owner;
            this.handler = handler;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref owner, null)?.Unsubscribe(handler);
        }
    }
}
