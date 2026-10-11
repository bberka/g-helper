using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.TaskScheduler;

namespace GHelper.Platform.Windows.Startup;

/// <summary>
/// Run-on-login through the Task Scheduler (logon + console connect triggers, elevated when the app is) and the
/// boot time "charge" task running as SYSTEM. Lifted from legacy app/Helpers/Startup.cs.
/// </summary>
public sealed class WindowsStartupService : IStartupService
{
    private const string TaskName = "GHelper";
    private const string ChargeTaskName = TaskName + "Charge";

    private readonly ISystemInfo system;
    private readonly Func<IProcessService> process;

    public WindowsStartupService(ISystemInfo system, Func<IProcessService> process)
    {
        this.system = system;
        this.process = process;
    }

    public bool IsSupported => OperatingSystem.IsWindows();

    private static string ExecutablePath => (Environment.ProcessPath ?? string.Empty).Trim();

    [SupportedOSPlatform("windows")]
    private static string UserSid => WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;

    [SupportedOSPlatform("windows")]
    private static string UserTaskName => TaskName + "_" + UserSid;

    [SupportedOSPlatform("windows")]
    private static Microsoft.Win32.TaskScheduler.Task? GetUserTask(TaskService taskService)
    {
        try
        {
            var task = taskService.GetTask(UserTaskName);
            if (task != null) return task;

            var legacy = taskService.GetTask(TaskName);
            string owner = legacy?.Definition.Principal.UserId ?? string.Empty;
            if (owner == UserSid ||
                string.Equals(owner.Split('\\').Last(), Environment.UserName, StringComparison.OrdinalIgnoreCase))
                return legacy;
        }
        catch (Exception e)
        {
            Logger.WriteLine("Can't read startup task: " + e.Message);
        }

        return null;
    }

    public bool IsScheduled()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var taskService = new TaskService();
            return GetUserTask(taskService) != null;
        }
        catch (Exception e)
        {
            Logger.WriteLine("Can't check startup task status: " + e.Message);
            return false;
        }
    }

    public void ReScheduleAdmin()
    {
        if (!OperatingSystem.IsWindows()) return;

        if (system.IsElevated && IsScheduled())
        {
            UnSchedule();
            Schedule();
        }
    }

    public void StartupCheck()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            CheckTask();
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Can't check startup task: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private void CheckTask()
    {
        using var taskService = new TaskService();
        var task = GetUserTask(taskService);
        if (task == null) return;

        string exePath = ExecutablePath;

        try
        {
            string action = task.Definition.Actions.FirstOrDefault()!.ToString()!.Trim();
            bool needsReschedule = false;

            if (!exePath.Equals(action, StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(action))
                {
                    Logger.WriteLine("Startup file doesn't exist: " + action);
                    needsReschedule = true;
                }
                else
                {
                    try
                    {
                        var currentVer = Assembly.GetEntryAssembly()?.GetName().Version;
                        var fileVersion = FileVersionInfo.GetVersionInfo(action).FileVersion;
                        if (currentVer is not null && TryParseFileVersion(fileVersion, out var scheduledVer) && currentVer > scheduledVer)
                        {
                            Logger.WriteLine($"Startup file is older {scheduledVer}, current is {currentVer}");
                            needsReschedule = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine("Can't compare assembly versions: " + ex.Message);
                    }
                }
            }

            if (needsReschedule)
            {
                if (task.Definition.Principal.RunLevel == TaskRunLevel.Highest && !system.IsElevated)
                {
                    process().RunAsAdmin();
                    return;
                }

                Logger.WriteLine("Rescheduling to: " + exePath);
                UnSchedule();
                Schedule();
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Can't check startup task: {ex.Message}");
        }

        if (taskService.RootFolder.AllTasks.FirstOrDefault(t => t.Name == ChargeTaskName) == null) ScheduleCharge();
    }

    /// <summary>"1.2.3.4" / "0.286" style FileVersion strings, missing parts are 0.</summary>
    public static bool TryParseFileVersion(string? fileVersion, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(fileVersion)) return false;

        string[] parts = fileVersion.Split('.');
        int[] numbers = new int[4];
        for (int i = 0; i < 4 && i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out numbers[i])) return false;
        }

        version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    public void UnscheduleCharge()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            using var taskService = new TaskService();
            taskService.RootFolder.DeleteTask(ChargeTaskName);
        }
        catch (Exception e)
        {
            Logger.WriteLine("Can't remove charge limit task: " + e.Message);
        }
    }

    public void ScheduleCharge()
    {
        if (!OperatingSystem.IsWindows()) return;

        string exePath = ExecutablePath;
        if (string.IsNullOrEmpty(exePath)) return;

        try
        {
            CreateChargeTask(exePath);
        }
        catch (Exception e)
        {
            Logger.WriteLine("Can't create a charge limit task: " + e.Message);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void CreateChargeTask(string exePath)
    {
        using TaskDefinition td = TaskService.Instance.NewTask();
        td.RegistrationInfo.Description = "G-Helper Charge Limit";
        td.Triggers.Add(new BootTrigger());
        td.Triggers.Add(new EventTrigger
        {
            Subscription = "<QueryList><Query Id='0' Path='System'><Select Path='System'>*[System[Provider[@Name='Microsoft-Windows-Kernel-Boot'] and EventID=27]]</Select></Query></QueryList>",
        });
        td.Actions.Add(exePath, "charge");

        td.Principal.UserId = "SYSTEM";
        td.Principal.LogonType = TaskLogonType.ServiceAccount;
        td.Principal.RunLevel = TaskRunLevel.Highest;

        td.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
        td.Settings.StopIfGoingOnBatteries = false;
        td.Settings.DisallowStartIfOnBatteries = false;
        td.Settings.ExecutionTimeLimit = TimeSpan.FromSeconds(30);

        TaskService.Instance.RootFolder.RegisterTaskDefinition(ChargeTaskName, td);
        Logger.WriteLine("Charge limit task scheduled: " + exePath);
    }

    public void Schedule()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            CreateStartupTask(ExecutablePath);
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Can't create startup task: " + ex.Message);
            if (system.IsElevated)
                Program.Ui.Alert("Scheduler Error", "Can't create a start up task. Try running Task Scheduler by hand and manually deleting GHelper task if it exists there.");
            else
                process().RunAsAdmin();
        }

        ScheduleCharge();
    }

    [SupportedOSPlatform("windows")]
    private void CreateStartupTask(string exePath)
    {
        string userName = WindowsIdentity.GetCurrent().Name;

        using TaskDefinition td = TaskService.Instance.NewTask();
        td.RegistrationInfo.Description = "G-Helper Auto Start";
        td.Triggers.Add(new LogonTrigger { UserId = userName, Delay = TimeSpan.FromSeconds(1) });
        // ConsoleConnect = fast user switch back; no SessionUnlock, it fires on every unlock
        td.Triggers.Add(new SessionStateChangeTrigger { StateChange = TaskSessionStateChangeType.ConsoleConnect, UserId = userName, Delay = TimeSpan.FromSeconds(1) });
        td.Actions.Add(exePath);

        td.Principal.LogonType = TaskLogonType.InteractiveToken;
        if (system.IsElevated) td.Principal.RunLevel = TaskRunLevel.Highest;

        td.Settings.StopIfGoingOnBatteries = false;
        td.Settings.DisallowStartIfOnBatteries = false;
        td.Settings.ExecutionTimeLimit = TimeSpan.Zero;

        TaskService.Instance.RootFolder.RegisterTaskDefinition(UserTaskName, td);
        Logger.WriteLine("Startup task scheduled: " + exePath);
    }

    public void UnSchedule()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            using (var taskService = new TaskService())
            {
                taskService.RootFolder.DeleteTask(UserTaskName, false);
                if (GetUserTask(taskService) != null) taskService.RootFolder.DeleteTask(TaskName);
            }
        }
        catch (Exception)
        {
            if (system.IsElevated)
                Program.Ui.Alert("Scheduler Error", "Can't remove task. Try running Task Scheduler by hand and manually deleting GHelper task if it exists there.");
            else
                process().RunAsAdmin();
        }

        UnscheduleCharge();
    }
}
