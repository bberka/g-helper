using System.Text.RegularExpressions;

namespace GHelper.Platform.Linux;

/// <summary>linux/input-event-codes.h values this project needs.</summary>
internal static class EvCodes
{
    public const ushort EV_SYN = 0x00;
    public const ushort EV_KEY = 0x01;
    public const ushort EV_REL = 0x02;
    public const ushort EV_MSC = 0x04;
    public const ushort MSC_SCAN = 0x04;
    public const ushort REL_WHEEL = 0x08;
    public const ushort SYN_REPORT = 0;
}

internal readonly record struct InputEvent(ushort Type, ushort Code, int Value);

internal sealed record InputDeviceInfo(string Name, int Bus, int Vendor, int Product, string Phys, string[] Handlers)
{
    /// <summary>Logical path of the evdev node ("/dev/input/event5") or null.</summary>
    public string? EventNode
    {
        get
        {
            string? handler = Handlers.FirstOrDefault(h => h.StartsWith("event", StringComparison.Ordinal));
            return handler is null ? null : "/dev/input/" + handler;
        }
    }

    public bool IsKeyboard => Handlers.Contains("kbd");
}

internal static class InputDeviceScanner
{
    /// <summary>Parses /proc/bus/input/devices.</summary>
    public static List<InputDeviceInfo> Scan(SysfsRoot root)
    {
        var result = new List<InputDeviceInfo>();
        string[]? lines = root.ReadLines("/proc/bus/input/devices");
        if (lines is null) return result;
        return Parse(lines);
    }

    public static List<InputDeviceInfo> Parse(IEnumerable<string> lines)
    {
        var result = new List<InputDeviceInfo>();
        int bus = 0, vendor = 0, product = 0;
        string name = "", phys = "";
        string[] handlers = [];
        bool open = false;

        void Flush()
        {
            if (open) result.Add(new InputDeviceInfo(name, bus, vendor, product, phys, handlers));
            open = false;
            bus = vendor = product = 0;
            name = phys = "";
            handlers = [];
        }

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0) { Flush(); continue; }
            if (line.StartsWith("I:", StringComparison.Ordinal))
            {
                Flush();
                open = true;
                bus = HexField(line, "Bus");
                vendor = HexField(line, "Vendor");
                product = HexField(line, "Product");
            }
            else if (line.StartsWith("N:", StringComparison.Ordinal))
            {
                Match m = Regex.Match(line, "Name=\"(.*)\"");
                name = m.Success ? m.Groups[1].Value : "";
            }
            else if (line.StartsWith("P:", StringComparison.Ordinal))
            {
                int idx = line.IndexOf("Phys=", StringComparison.Ordinal);
                phys = idx >= 0 ? line[(idx + 5)..].Trim() : "";
            }
            else if (line.StartsWith("H:", StringComparison.Ordinal))
            {
                int idx = line.IndexOf("Handlers=", StringComparison.Ordinal);
                handlers = idx >= 0
                    ? line[(idx + 9)..].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    : [];
            }
        }
        Flush();
        return result;
    }

    private static int HexField(string line, string field)
    {
        Match m = Regex.Match(line, field + @"=([0-9a-fA-F]+)");
        return m.Success ? Convert.ToInt32(m.Groups[1].Value, 16) : 0;
    }
}

/// <summary>One opened /dev/input/eventN node (non blocking, poll + read).</summary>
internal sealed unsafe class EvdevReader : IDisposable
{
    private static readonly int TimevalSize = IntPtr.Size * 2;
    public static readonly int EventSize = TimevalSize + 8;

    private int fd;
    private readonly byte[] buffer = new byte[EventSize * 64];

    public string Path { get; }

    private EvdevReader(int fd, string path)
    {
        this.fd = fd;
        Path = path;
    }

    public static EvdevReader? Open(string physicalPath)
    {
        try
        {
            int fd = Native.open(physicalPath, Native.O_RDONLY | Native.O_NONBLOCK | Native.O_CLOEXEC);
            return fd < 0 ? null : new EvdevReader(fd, physicalPath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Waits up to <paramref name="timeoutMs"/>; false on timeout, throws EndOfStreamException when the device is gone.</summary>
    public bool WaitReadable(int timeoutMs)
    {
        var pfd = new Native.PollFd { fd = fd, events = Native.POLLIN };
        int rc = Native.poll(&pfd, 1, timeoutMs);
        if (rc < 0) return false;
        if (rc == 0) return false;
        if ((pfd.revents & (Native.POLLERR | Native.POLLHUP | Native.POLLNVAL)) != 0 && (pfd.revents & Native.POLLIN) == 0)
            throw new EndOfStreamException();
        return true;
    }

    /// <summary>Reads all currently queued events. Throws EndOfStreamException on EOF / ENODEV.</summary>
    public int Read(List<InputEvent> into)
    {
        int count = 0;
        fixed (byte* p = buffer)
        {
            nint n = Native.read(fd, p, (nuint)buffer.Length);
            if (n == 0) throw new EndOfStreamException();
            if (n < 0) return 0; // EAGAIN / EINTR
            for (int offset = 0; offset + EventSize <= n; offset += EventSize)
            {
                ushort type = BitConverter.ToUInt16(buffer, offset + TimevalSize);
                ushort code = BitConverter.ToUInt16(buffer, offset + TimevalSize + 2);
                int value = BitConverter.ToInt32(buffer, offset + TimevalSize + 4);
                into.Add(new InputEvent(type, code, value));
                count++;
            }
        }
        return count;
    }

    public void Dispose()
    {
        int old = Interlocked.Exchange(ref fd, -1);
        if (old >= 0)
        {
            try { Native.close(old); } catch { }
        }
    }

    /// <summary>Serialises an event the way the kernel does (tests, fake event files).</summary>
    public static byte[] Encode(ushort type, ushort code, int value)
    {
        var data = new byte[EventSize];
        BitConverter.GetBytes(type).CopyTo(data, TimevalSize);
        BitConverter.GetBytes(code).CopyTo(data, TimevalSize + 2);
        BitConverter.GetBytes(value).CopyTo(data, TimevalSize + 4);
        return data;
    }
}

/// <summary>
/// Opens every input device accepted by the filter, delivers its events on a background thread per device and
/// periodically rescans for hot-plugged devices (USB keyboards re-enumerating after suspend).
/// </summary>
internal sealed class EvdevMonitor : IDisposable
{
    private readonly SysfsRoot root;
    private readonly Func<InputDeviceInfo, bool> filter;
    private readonly Action<InputDeviceInfo, InputEvent> handler;
    private readonly int rescanMs;
    private readonly Dictionary<string, Thread> open = new();
    private readonly object gate = new();
    private readonly CancellationTokenSource cts = new();
    private Thread? supervisor;

    public EvdevMonitor(SysfsRoot root, Func<InputDeviceInfo, bool> filter, Action<InputDeviceInfo, InputEvent> handler, int rescanMs = 10000)
    {
        this.root = root;
        this.filter = filter;
        this.handler = handler;
        this.rescanMs = rescanMs;
    }

    public int OpenDeviceCount
    {
        get { lock (gate) return open.Count; }
    }

    public void Start()
    {
        if (supervisor is not null) return;
        ScanOnce();
        supervisor = new Thread(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                try { if (cts.Token.WaitHandle.WaitOne(rescanMs)) break; } catch { break; }
                ScanOnce();
            }
        })
        { IsBackground = true, Name = "ghelper-evdev-scan" };
        supervisor.Start();
    }

    private void ScanOnce()
    {
        try
        {
            foreach (InputDeviceInfo info in InputDeviceScanner.Scan(root))
            {
                string? node = info.EventNode;
                if (node is null || !filter(info)) continue;

                lock (gate)
                {
                    if (open.ContainsKey(node)) continue;
                }

                EvdevReader? reader = EvdevReader.Open(root.Map(node));
                if (reader is null) continue;

                var thread = new Thread(() => Pump(info, node, reader)) { IsBackground = true, Name = "ghelper-evdev-" + info.Name };
                lock (gate) open[node] = thread;
                thread.Start();
            }
        }
        catch (Exception ex)
        {
            LinuxLog.Write("evdev scan: " + ex.Message);
        }
    }

    private void Pump(InputDeviceInfo info, string node, EvdevReader reader)
    {
        var events = new List<InputEvent>(64);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                if (!reader.WaitReadable(500)) continue;
                events.Clear();
                reader.Read(events);
                foreach (InputEvent e in events)
                {
                    try { handler(info, e); }
                    catch (Exception ex) { LinuxLog.Write("evdev handler: " + ex.Message); }
                }
            }
        }
        catch (EndOfStreamException) { }
        catch (Exception ex)
        {
            LinuxLog.Write($"evdev {node}: {ex.Message}");
        }
        finally
        {
            reader.Dispose();
            lock (gate) open.Remove(node);
        }
    }

    public void Dispose()
    {
        cts.Cancel();
    }
}
