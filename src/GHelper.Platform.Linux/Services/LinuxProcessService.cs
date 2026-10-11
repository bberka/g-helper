using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime;
using System.Text;

namespace GHelper.Platform.Linux;

/// <summary>
/// Single instance (exclusive lock file + unix socket for activation), pkexec elevation, xdg-open.
///
/// Elevation: the elevated child sets GHELPER_TAKEOVER=1; when it finds the lock held it asks the running user
/// instance to quit ("takeover" message) and waits for the lock instead of exiting like a plain second instance.
/// </summary>
public sealed class LinuxProcessService : IProcessService
{
    public const string TakeoverVariable = "GHELPER_TAKEOVER";

    private static readonly string[] LauncherPaths =
        ["/usr/lib/ghelper/ghelper-root-launcher", "/usr/libexec/ghelper/ghelper-root-launcher", "/opt/ghelper/ghelper-root-launcher"];

    private readonly LinuxContext ctx;
    private readonly object gate = new();
    private FileStream? lockStream;
    private Socket? listener;
    private Thread? acceptThread;
    private Action? onSecond;
    private string? socketPath;
    private long lastAdmin;

    public LinuxProcessService(LinuxContext ctx)
    {
        this.ctx = ctx;
    }

    // ------------------------------------------------------------------ single instance

    internal string LockPath(string name) => SysfsRoot.Combine(ctx.RuntimeDirectory, $"ghelper-{Sanitize(name)}.lock");
    internal string SocketPath(string name) => SysfsRoot.Combine(ctx.RuntimeDirectory, $"ghelper-{Sanitize(name)}.sock");

    private static string Sanitize(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name) sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        return sb.Length == 0 ? "app" : sb.ToString();
    }

    private FileStream? TryLock(string physical)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
            // FileShare.None is an advisory flock(LOCK_EX | LOCK_NB) on Unix
            return new FileStream(physical, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public bool AcquireSingleInstance(string name, Action onSecondInstanceStarted)
    {
        string physicalLock = ctx.Root.Map(LockPath(name));
        string physicalSocket = ctx.Root.Map(SocketPath(name));
        bool takeover = ctx.Env(TakeoverVariable) == "1";

        FileStream? stream = null;
        try
        {
            stream = TryLock(physicalLock);
        }
        catch (UnauthorizedAccessException)
        {
            // lock file created by a root instance: if it answers we are the second instance, otherwise it is stale
            if (Signal(physicalSocket, takeover ? "takeover" : "activate"))
                return WaitForLock(physicalLock, takeover, onSecondInstanceStarted);
            try { File.Delete(physicalLock); stream = TryLock(physicalLock); } catch { }
        }

        if (stream is null)
        {
            Signal(physicalSocket, takeover ? "takeover" : "activate");
            return WaitForLock(physicalLock, takeover, onSecondInstanceStarted);
        }

        Adopt(stream, physicalSocket, onSecondInstanceStarted);
        return true;
    }

    private bool WaitForLock(string physicalLock, bool takeover, Action activated)
    {
        if (!takeover) return false;

        // the user instance releases the lock while it shuts down
        for (int i = 0; i < 50; i++)
        {
            Thread.Sleep(100);
            FileStream? stream = null;
            try { stream = TryLock(physicalLock); } catch { }
            if (stream is not null)
            {
                string socket = Path.ChangeExtension(physicalLock, ".sock");
                Adopt(stream, socket, activated);
                return true;
            }
        }
        return false;
    }

    private void Adopt(FileStream stream, string physicalSocket, Action activated)
    {
        lock (gate)
        {
            lockStream = stream;
            onSecond = activated;
            socketPath = physicalSocket;
            try
            {
                stream.SetLength(0);
                byte[] pid = Encoding.ASCII.GetBytes(Environment.ProcessId.ToString());
                stream.Write(pid, 0, pid.Length);
                stream.Flush();
            }
            catch { }
        }
        StartListener(physicalSocket);
    }

    private void StartListener(string physicalSocket)
    {
        try
        {
            if (File.Exists(physicalSocket)) File.Delete(physicalSocket);
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(physicalSocket));
            socket.Listen(4);
            listener = socket;

            acceptThread = new Thread(() => AcceptLoop(socket)) { IsBackground = true, Name = "ghelper-single-instance" };
            acceptThread.Start();
        }
        catch (Exception ex)
        {
            LinuxLog.Write("single instance socket: " + ex.Message);
        }
    }

    private void AcceptLoop(Socket socket)
    {
        while (true)
        {
            try
            {
                using Socket client = socket.Accept();
                client.ReceiveTimeout = 2000;
                var buffer = new byte[64];
                int read = client.Receive(buffer);
                string message = Encoding.ASCII.GetString(buffer, 0, read).Trim();

                if (message == "takeover")
                {
                    client.Send("ok\n"u8.ToArray());
                    ReleaseSingleInstance();
                    try { Program.Ui.Quit(); } catch { }
                    return;
                }

                Action? callback;
                lock (gate) callback = onSecond;
                callback?.Invoke();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (listener is null) return;
            }
            catch (Exception ex)
            {
                LinuxLog.Write("single instance: " + ex.Message);
            }
        }
    }

    private static bool Signal(string physicalSocket, string message)
    {
        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
            {
                SendTimeout = 1000,
                ReceiveTimeout = 1000,
            };
            client.Connect(new UnixDomainSocketEndPoint(physicalSocket));
            client.Send(Encoding.ASCII.GetBytes(message + "\n"));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void ReleaseSingleInstance()
    {
        lock (gate)
        {
            try { listener?.Dispose(); } catch { }
            listener = null;

            if (socketPath is not null)
            {
                try { File.Delete(socketPath); } catch { }
            }

            try { lockStream?.Dispose(); } catch { }
            lockStream = null;
        }
    }

    // ------------------------------------------------------------------ elevation

    public void RunAsAdmin(string? param = null, bool force = false)
    {
        long now = Environment.TickCount64;
        if (Math.Abs(now - lastAdmin) < 2000) return;
        lastAdmin = now;

        if (ctx.IsRoot) return; // already elevated, nothing to do (force cannot elevate further)

        if (!ctx.Runner.CommandExists("pkexec"))
        {
            LinuxLog.Write("pkexec not found: can't elevate");
            return;
        }

        var args = new List<string>();
        string? launcher = LauncherPaths.FirstOrDefault(p => ctx.Root.FileExists(p));
        if (launcher is not null) args.Add(launcher);
        else args.Add("env");

        foreach (var pair in ctx.SessionEnvironment())
            args.Add($"{pair.Key}={pair.Value}");
        args.Add($"{TakeoverVariable}=1");

        if (launcher is not null) args.Add("--");

        args.AddRange(ctx.LaunchCommand);
        if (!string.IsNullOrWhiteSpace(param))
            args.AddRange(param.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        int pid = ctx.Runner.StartDetached("pkexec", args);
        LinuxLog.Write(pid > 0 ? "Elevation requested through pkexec" : "pkexec failed to start");
    }

    public void RunAsRestrictedUser(string command)
    {
        LaunchAsUser("sh", ["-c", command]);
    }

    /// <summary>Start a process in the interactive user's session even when this process is root.</summary>
    internal void LaunchAsUser(string file, IReadOnlyList<string> args)
    {
        if (ctx.IsRoot && ctx.InvokingUid is uint uid && ctx.LookupUser(uid) is { } user && ctx.Runner.CommandExists("runuser"))
        {
            var all = new List<string> { "-u", user.Name, "--", file };
            all.AddRange(args);
            ctx.Runner.StartDetached("runuser", all, ctx.SessionEnvironment());
            return;
        }

        ctx.Runner.StartDetached(file, args);
    }

    // ------------------------------------------------------------------ processes

    public void KillByName(string name)
    {
        Process[] processes = Process.GetProcessesByName(name);
        try
        {
            foreach (Process process in processes)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    process.Kill();
                    LinuxLog.Write("Stopped: " + process.ProcessName);
                }
                catch (Exception ex)
                {
                    LinuxLog.Write($"Can't stop {name}: {ex.Message}");
                }
            }
        }
        finally
        {
            foreach (Process p in processes) p.Dispose();
        }
    }

    public void SetPriority(ProcessPriorityClass priorityClass = ProcessPriorityClass.Normal)
    {
        try
        {
            using Process current = Process.GetCurrentProcess();
            current.PriorityClass = priorityClass;
        }
        catch (Exception ex)
        {
            LinuxLog.Write("Can't set priority: " + ex.Message);
        }
    }

    public string RunCommand(string name, string args, string? directory = null, int timeoutMs = 0)
    {
        try
        {
            var info = new ProcessStartInfo(name, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            if (!string.IsNullOrEmpty(directory)) info.WorkingDirectory = directory;

            using var process = Process.Start(info);
            if (process is null) return "";

            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();

            if (timeoutMs > 0)
            {
                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(true); } catch { }
                    return "";
                }
            }
            else
            {
                process.WaitForExit();
            }

            string output = stdout.GetAwaiter().GetResult();
            string error = stderr.GetAwaiter().GetResult();
            return string.IsNullOrEmpty(output) ? error : output;
        }
        catch (Exception ex)
        {
            LinuxLog.Write($"{name} {args}: {ex.Message}");
            return "";
        }
    }

    public void OpenUrl(string url) => Open(url);

    public void OpenFolder(string path) => Open(path);

    private void Open(string target)
    {
        foreach (string opener in new[] { "xdg-open", "gio", "sensible-browser", "x-www-browser" })
        {
            if (!ctx.Runner.CommandExists(opener)) continue;
            LaunchAsUser(opener, opener == "gio" ? ["open", target] : [target]);
            return;
        }
        LinuxLog.Write("No xdg-open available to open " + target);
    }

    public void TrimMemory()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            Native.malloc_trim(0);
        }
        catch
        {
            // musl has no malloc_trim; the GC pass above is enough
        }
    }
}
