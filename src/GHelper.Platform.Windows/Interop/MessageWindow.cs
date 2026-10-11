using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace GHelper.Platform.Windows.Interop;

/// <summary>
/// Hidden message-only window running its own message pump on a dedicated thread. Replaces the WinForms NativeWindow /
/// Settings form WndProc the legacy app used for WM_HOTKEY and WM_POWERBROADCAST. Work that has to happen on the
/// window thread (RegisterHotKey, SetWindowsHookEx) is marshalled with <see cref="Invoke"/>.
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private const uint WM_INVOKE = Win32.WM_APP + 1;

    private readonly Win32.WndProc wndProc;   // keeps the delegate alive for the native window class
    private readonly string className = "GHelperMessageWindow_" + Guid.NewGuid().ToString("N");
    private readonly ConcurrentQueue<Action> queue = new();
    private readonly ManualResetEventSlim ready = new(false);
    private readonly Thread thread;
    private IntPtr handle;
    private int threadId;
    private bool disposed;

    /// <summary>Raised on the window thread for every message (msg, wParam, lParam). Keep handlers short.</summary>
    public event Action<uint, IntPtr, IntPtr>? MessageReceived;

    public MessageWindow()
    {
        wndProc = WindowProc;
        thread = new Thread(Run) { IsBackground = true, Name = "GHelper message window" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(5000);
    }

    public IntPtr Handle => handle;

    public bool IsAlive => handle != IntPtr.Zero && !disposed;

    private void Run()
    {
        threadId = Environment.CurrentManagedThreadId;
        IntPtr instance = Win32.GetModuleHandle(null);
        try
        {
            var wc = new Win32.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
                hInstance = instance,
                lpszClassName = className,
            };

            if (Win32.RegisterClassEx(ref wc) == 0)
            {
                Logger.WriteLine("MessageWindow: RegisterClassEx failed " + Marshal.GetLastWin32Error());
                return;
            }

            handle = Win32.CreateWindowEx(0, className, "GHelper", 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, IntPtr.Zero, instance, IntPtr.Zero);
            if (handle == IntPtr.Zero)
            {
                Logger.WriteLine("MessageWindow: CreateWindowEx failed " + Marshal.GetLastWin32Error());
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine("MessageWindow: " + ex.Message);
            return;
        }
        finally
        {
            ready.Set();
        }

        while (Win32.GetMessage(out Win32.MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }

        handle = IntPtr.Zero;
        try { Win32.UnregisterClass(className, instance); } catch { }
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == WM_INVOKE)
            {
                while (queue.TryDequeue(out var action))
                {
                    try { action(); }
                    catch (Exception ex) { Logger.WriteLine("MessageWindow invoke: " + ex.Message); }
                }
                return IntPtr.Zero;
            }

            if (msg == Win32.WM_DESTROY)
            {
                Win32.PostQuitMessage(0);
                return IntPtr.Zero;
            }

            MessageReceived?.Invoke(msg, wParam, lParam);

            // PBT_* notifications expect TRUE
            if (msg == Win32.WM_POWERBROADCAST) return (IntPtr)1;
        }
        catch (Exception ex)
        {
            Logger.WriteLine("MessageWindow: " + ex.Message);
        }

        return Win32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private bool OnWindowThread => Environment.CurrentManagedThreadId == threadId;

    /// <summary>Run on the window thread and wait. Falls back to the caller thread when the window could not be created.</summary>
    public void Invoke(Action action, int timeoutMs = 5000)
    {
        Invoke<object?>(() => { action(); return null; }, timeoutMs);
    }

    public T? Invoke<T>(Func<T> func, int timeoutMs = 5000)
    {
        if (!IsAlive || OnWindowThread) return func();

        T? result = default;
        Exception? error = null;
        using var done = new ManualResetEventSlim(false);

        queue.Enqueue(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        });

        if (!Win32.PostMessage(handle, WM_INVOKE, IntPtr.Zero, IntPtr.Zero)) return func();
        if (!done.Wait(timeoutMs)) throw new TimeoutException("Message window did not respond");
        if (error is not null) throw error;
        return result;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        IntPtr h = handle;
        if (h != IntPtr.Zero)
        {
            Win32.PostMessage(h, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            // WM_CLOSE -> DefWindowProc -> DestroyWindow -> WM_DESTROY -> PostQuitMessage
            thread.Join(2000);
        }
    }
}
