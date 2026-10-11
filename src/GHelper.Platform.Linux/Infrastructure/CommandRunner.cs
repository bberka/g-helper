using System.Diagnostics;
using System.Text;

namespace GHelper.Platform.Linux;

public readonly record struct CommandResult(bool Started, int ExitCode, string StdOut, string StdErr)
{
    public bool Success => Started && ExitCode == 0;
    public static readonly CommandResult NotStarted = new(false, -1, "", "");
}

/// <summary>
/// Seam for everything that shells out (powerprofilesctl, xrandr, pactl, systemctl, ...). Tests substitute a fake.
/// Implementations never throw: a missing binary yields <see cref="CommandResult.NotStarted"/>.
/// </summary>
public interface ICommandRunner
{
    CommandResult Run(string fileName, IReadOnlyList<string> args, int timeoutMs = 5000, string? stdin = null,
        IReadOnlyDictionary<string, string>? environment = null);

    bool CommandExists(string fileName);

    /// <summary>Long running process, stdout delivered line by line. Dispose to kill. Null if it can not be started.</summary>
    IDisposable? StartLineStream(string fileName, IReadOnlyList<string> args, Action<string> onLine);

    /// <summary>Long running process, stdout delivered as raw chunks (parec). Dispose to kill. Null if it can not be started.</summary>
    IDisposable? StartBinaryStream(string fileName, IReadOnlyList<string> args, Action<byte[], int> onData);

    /// <summary>Fire and forget (GUI helpers, pkexec relaunch). Returns the process id or -1.</summary>
    int StartDetached(string fileName, IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? environment = null);
}

public sealed class ProcessCommandRunner : ICommandRunner
{
    private static readonly string[] SearchDirs =
        ["/usr/local/sbin", "/usr/local/bin", "/usr/sbin", "/usr/bin", "/sbin", "/bin"];

    public CommandResult Run(string fileName, IReadOnlyList<string> args, int timeoutMs = 5000, string? stdin = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        try
        {
            using var process = new Process { StartInfo = CreateStartInfo(fileName, args, redirectInput: stdin is not null, environment) };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (stdout) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };

            if (!process.Start()) return CommandResult.NotStarted;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (stdin is not null)
            {
                process.StandardInput.Write(stdin);
                process.StandardInput.Close();
            }

            if (timeoutMs > 0)
            {
                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(true); } catch { }
                    return new CommandResult(true, -1, Snapshot(stdout), Snapshot(stderr));
                }
            }
            process.WaitForExit();

            return new CommandResult(true, process.ExitCode, Snapshot(stdout), Snapshot(stderr));
        }
        catch (Exception ex)
        {
            LinuxLog.Write($"run {fileName}: {ex.Message}");
            return CommandResult.NotStarted;
        }
    }

    public bool CommandExists(string fileName)
    {
        if (fileName.Contains('/')) return File.Exists(fileName);

        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Concat(SearchDirs);

        foreach (string dir in dirs)
        {
            try
            {
                if (File.Exists(Path.Combine(dir, fileName))) return true;
            }
            catch { }
        }
        return false;
    }

    public IDisposable? StartLineStream(string fileName, IReadOnlyList<string> args, Action<string> onLine)
    {
        try
        {
            var process = new Process { StartInfo = CreateStartInfo(fileName, args, false, null), EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                try { onLine(e.Data); } catch (Exception ex) { LinuxLog.Write($"{fileName} handler: {ex.Message}"); }
            };
            if (!process.Start()) { process.Dispose(); return null; }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return new ProcessHandle(process);
        }
        catch (Exception ex)
        {
            LinuxLog.Write($"stream {fileName}: {ex.Message}");
            return null;
        }
    }

    public IDisposable? StartBinaryStream(string fileName, IReadOnlyList<string> args, Action<byte[], int> onData)
    {
        try
        {
            var process = new Process { StartInfo = CreateStartInfo(fileName, args, false, null) };
            if (!process.Start()) { process.Dispose(); return null; }
            process.BeginErrorReadLine();

            var handle = new ProcessHandle(process);
            var thread = new Thread(() =>
            {
                var buffer = new byte[16384];
                try
                {
                    var stream = process.StandardOutput.BaseStream;
                    int read;
                    while (!handle.IsDisposed && (read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        onData(buffer, read);
                }
                catch { }
            })
            { IsBackground = true, Name = "ghelper-" + fileName };
            thread.Start();
            return handle;
        }
        catch (Exception ex)
        {
            LinuxLog.Write($"stream {fileName}: {ex.Message}");
            return null;
        }
    }

    public int StartDetached(string fileName, IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? environment = null)
    {
        try
        {
            var info = CreateStartInfo(fileName, args, false, environment);
            info.RedirectStandardOutput = false;
            info.RedirectStandardError = false;
            using var process = Process.Start(info);
            return process?.Id ?? -1;
        }
        catch (Exception ex)
        {
            LinuxLog.Write($"start {fileName}: {ex.Message}");
            return -1;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, IReadOnlyList<string> args, bool redirectInput,
        IReadOnlyDictionary<string, string>? environment)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        // stable, parseable output for the tools we scrape
        info.Environment["LC_ALL"] = "C";
        info.Environment["LANG"] = "C";
        if (environment is not null)
            foreach (var pair in environment) info.Environment[pair.Key] = pair.Value;
        return info;
    }

    private static string Snapshot(StringBuilder builder)
    {
        lock (builder) return builder.ToString();
    }

    private sealed class ProcessHandle(Process process) : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            try { if (!process.HasExited) process.Kill(true); } catch { }
            try { process.Dispose(); } catch { }
        }
    }
}
