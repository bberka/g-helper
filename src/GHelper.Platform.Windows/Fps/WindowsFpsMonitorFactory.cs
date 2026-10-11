using GHelper.Platform.Windows.Interop;

namespace GHelper.Platform.Windows.Fps;

/// <summary>
/// ETW based FPS monitor factory plus the foreground window information the overlay pins the counter to (legacy
/// HardwareOverlay: GetForegroundWindow + GetWindowThreadProcessId, polled once per second; <see cref="ForegroundChanged"/>
/// adds an event so game-only visibility reacts immediately).
/// The native structures use 64 bit layouts, so 32 bit processes are unsupported.
/// </summary>
public sealed class WindowsFpsMonitorFactory : IFpsMonitorFactory, IDisposable
{
    private readonly object sync = new();
    private readonly Win32.WinEventProc callback;   // keeps the delegate alive for the native hook
    private MessageWindow? window;
    private IntPtr hook;
    private Action? foregroundChanged;

    public WindowsFpsMonitorFactory()
    {
        callback = OnWinEvent;
    }

    public bool IsSupported => OperatingSystem.IsWindows() && Environment.Is64BitProcess;

    public IFpsMonitor? Create()
    {
        if (!IsSupported) return null;
        return new EtwFpsMonitor();
    }

    public int GetForegroundProcessId()
    {
        if (!OperatingSystem.IsWindows()) return 0;

        try
        {
            Win32.GetWindowThreadProcessId(Win32.GetForegroundWindow(), out uint pid);
            return (int)pid;
        }
        catch
        {
            return 0;
        }
    }

    public event Action? ForegroundChanged
    {
        add
        {
            lock (sync)
            {
                foregroundChanged += value;
                EnsureHook();
            }
        }
        remove
        {
            lock (sync)
            {
                foregroundChanged -= value;
                if (foregroundChanged is null) ReleaseHook();
            }
        }
    }

    // WINEVENT_OUTOFCONTEXT needs a message loop on the thread that installs the hook: use a private message window
    private void EnsureHook()
    {
        if (!OperatingSystem.IsWindows() || hook != IntPtr.Zero) return;

        try
        {
            window ??= new MessageWindow();
            if (!window.IsAlive) return;

            hook = window.Invoke(() => Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, callback, 0, 0, Win32.WINEVENT_OUTOFCONTEXT));
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Foreground hook: " + ex.Message);
        }
    }

    private void ReleaseHook()
    {
        if (hook == IntPtr.Zero) return;

        IntPtr handle = hook;
        hook = IntPtr.Zero;

        try { window?.Invoke(() => Win32.UnhookWinEvent(handle)); }
        catch { }
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try { foregroundChanged?.Invoke(); }
        catch (Exception ex) { Logger.WriteLine("Foreground handler: " + ex.Message); }
    }

    public void Dispose()
    {
        lock (sync)
        {
            ReleaseHook();
            window?.Dispose();
            window = null;
        }
    }
}
