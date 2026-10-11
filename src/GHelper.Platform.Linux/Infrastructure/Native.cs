using System.Runtime.InteropServices;

namespace GHelper.Platform.Linux;

/// <summary>
/// Minimal libc surface (evdev read, uinput inject, euid). Every entry point is only reached on Linux and callers
/// wrap them in try/catch, so a missing libc symbol degrades the feature instead of crashing.
/// </summary>
internal static unsafe class Native
{
    public const int O_RDONLY = 0;
    public const int O_WRONLY = 1;
    public const int O_RDWR = 2;
    public const int O_NONBLOCK = 0x800;
    public const int O_CLOEXEC = 0x80000;

    public const short POLLIN = 0x0001;
    public const short POLLERR = 0x0008;
    public const short POLLHUP = 0x0010;
    public const short POLLNVAL = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }

    [DllImport("libc", SetLastError = true)]
    public static extern int open([MarshalAs(UnmanagedType.LPStr)] string path, int flags);

    [DllImport("libc", SetLastError = true)]
    public static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    public static extern nint read(int fd, byte* buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    public static extern nint write(int fd, byte* buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    public static extern int poll(PollFd* fds, nuint nfds, int timeoutMs);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    public static extern int ioctl(int fd, nuint request, nint argument);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    public static extern int ioctl(int fd, nuint request, byte* argument);

    [DllImport("libc")]
    public static extern uint geteuid();

    [DllImport("libc", SetLastError = true)]
    public static extern int chown([MarshalAs(UnmanagedType.LPStr)] string path, uint owner, uint group);

    public static bool IsLinux => OperatingSystem.IsLinux();

    public static uint SafeEuid()
    {
        try { return IsLinux ? geteuid() : uint.MaxValue; }
        catch { return uint.MaxValue; }
    }
}
