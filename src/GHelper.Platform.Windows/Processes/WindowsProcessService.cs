using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using GHelper.Platform.Windows.Interop;

namespace GHelper.Platform.Windows.Processes;

/// <summary>
/// Windows implementation of <see cref="IProcessService"/>: single instance through a named global event (signalled by a
/// second instance), UAC elevation, restricted user launch, process helpers. Lifted from legacy ProcessHelper /
/// RestrictedProcessHelper / MemoryHelper.
/// </summary>
public sealed class WindowsProcessService : IProcessService
{
    private readonly ISystemInfo system;
    private readonly object sync = new();

    private EventWaitHandle? instanceEvent;
    private RegisteredWaitHandle? instanceWait;
    private string? instanceName;
    private Action? instanceCallback;
    private long lastAdmin;

    public WindowsProcessService(ISystemInfo system)
    {
        this.system = system;
    }

    public static string EventName(string name) => "Global\\GHelperApp-" + name;

    // --- single instance ----------------------------------------------------------------------

    public bool AcquireSingleInstance(string name, Action onSecondInstanceStarted)
    {
        if (!OperatingSystem.IsWindows()) return true;

        lock (sync)
        {
            if (instanceEvent is not null) return true;

            string eventName = EventName(name);
            EventWaitHandle? handle = null;
            bool created = false;

            try
            {
                var security = new EventWaitHandleSecurity();
                security.AddAccessRule(new EventWaitHandleAccessRule(
                    new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                    EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify,
                    AccessControlType.Allow));

                handle = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, eventName, out created, security);
            }
            catch
            {
                try { handle = EventWaitHandle.OpenExisting(eventName); }
                catch (Exception ex)
                {
                    Logger.WriteLine("Single instance: " + ex.Message);
                    return true; // can't tell: don't lock the user out
                }
            }

            if (!created)
            {
                try { handle?.Set(); }
                catch (Exception ex) { Logger.WriteLine("Signal first instance failed: " + ex.Message); }
                handle?.Dispose();
                return false;
            }

            instanceEvent = handle;
            instanceName = name;
            instanceCallback = onSecondInstanceStarted;
            instanceWait = ThreadPool.RegisterWaitForSingleObject(handle!, (_, _) =>
            {
                try { onSecondInstanceStarted(); }
                catch (Exception ex) { Logger.WriteLine("Second instance handler: " + ex.Message); }
            }, null, Timeout.Infinite, false);

            return true;
        }
    }

    public void ReleaseSingleInstance()
    {
        lock (sync)
        {
            instanceWait?.Unregister(null);
            instanceWait = null;
            instanceEvent?.Dispose();
            instanceEvent = null;
        }
    }

    // --- elevation ----------------------------------------------------------------------------

    public void RunAsAdmin(string? param = null, bool force = false)
    {
        if (!OperatingSystem.IsWindows()) return;

        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        if (Math.Abs(now - lastAdmin) < 2000) return;
        lastAdmin = now;

        if (system.IsElevated && !force) return;

        string? name = instanceName;
        Action? callback = instanceCallback;

        // the elevated copy has to be able to take over the single instance lock
        ReleaseSingleInstance();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,
                FileName = Environment.ProcessPath,
                Arguments = param ?? string.Empty,
                Verb = "runas",
            };

            Process.Start(startInfo)?.Dispose();
            Program.Ui.Quit();
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);

            // UAC cancelled: stay alive and keep the lock
            if (name is not null && callback is not null) AcquireSingleInstance(name, callback);
        }
    }

    public void RunAsRestrictedUser(string command)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            RestrictedProcess.Run(command, system.IsElevated);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Failed to run: {command} {ex.Message}");
        }
    }

    // --- processes ----------------------------------------------------------------------------

    public void KillByName(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    process.Kill();
                    Logger.WriteLine($"Stopped: {process.ProcessName}");
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"Failed to stop: {process.ProcessName} {ex.Message}");
                }
            }
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    public void SetPriority(ProcessPriorityClass priorityClass = ProcessPriorityClass.Normal)
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            p.PriorityClass = priorityClass;
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.ToString());
        }
    }

    public string RunCommand(string name, string args, string? directory = null, int timeoutMs = 0)
    {
        using var cmd = new Process();
        cmd.StartInfo.UseShellExecute = false;
        cmd.StartInfo.CreateNoWindow = true;
        cmd.StartInfo.RedirectStandardOutput = true;
        cmd.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        cmd.StartInfo.FileName = name == "powershell"
            ? Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe")
            : name;
        cmd.StartInfo.Arguments = name == "powershell" ? "-NoProfile " + args : args;
        if (directory != null) cmd.StartInfo.WorkingDirectory = directory;
        cmd.Start();

        var watch = Stopwatch.StartNew();
        string result;

        if (timeoutMs > 0)
        {
            var readTask = cmd.StandardOutput.ReadToEndAsync();
            if (!readTask.Wait(timeoutMs))
            {
                try { cmd.Kill(entireProcessTree: true); } catch { }
                watch.Stop();
                Logger.WriteLine(name + " " + args);
                Logger.WriteLine($"{watch.ElapsedMilliseconds} ms: TIMEOUT after {timeoutMs} ms");
                return string.Empty;
            }

            result = readTask.Result.Replace(Environment.NewLine, " ").Trim(' ');
        }
        else
        {
            result = cmd.StandardOutput.ReadToEnd().Replace(Environment.NewLine, " ").Trim(' ');
        }

        watch.Stop();
        Logger.WriteLine(name + " " + args);
        Logger.WriteLine(watch.ElapsedMilliseconds + " ms: " + result);
        cmd.WaitForExit();

        return result;
    }

    public void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Can't open {url}: {ex.Message}");
        }
    }

    public void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Can't open {path}: {ex.Message}");
        }
    }

    public void TrimMemory()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);

        if (!OperatingSystem.IsWindows()) return;
        if (!AppConfig.IsNotFalse("gc")) return;

        using var p = Process.GetCurrentProcess();
        Win32.SetProcessWorkingSetSize(p.Handle, -1, -1);
    }

    // --- Windows service helpers used by the vendor services ----------------------------------

    public void StopDisableService(string serviceName, string startupType = "Disabled")
    {
        try
        {
            string script = $"Get-Service -Name \"{serviceName}\" | Stop-Service -Force -PassThru | Set-Service -StartupType {startupType}";
            Logger.WriteLine(script);
            RunCommand("powershell", script);
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.ToString());
        }
    }

    public void StartEnableService(string serviceName, bool automatic = true)
    {
        try
        {
            string script = $"Set-Service -Name \"{serviceName}\" -Status running" + (automatic ? " -StartupType Automatic" : "");
            Logger.WriteLine(script);
            RunCommand("powershell", script);
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.ToString());
        }
    }
}
