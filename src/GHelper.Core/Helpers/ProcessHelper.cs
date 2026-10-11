using GHelper.Platform;
using System.Diagnostics;

namespace GHelper.Helpers
{
    /// <summary>
    /// Legacy ProcessHelper as a thin facade over <see cref="IProcessService"/> + <see cref="ISystemInfo"/>.
    /// Windows-only service control (StopDisableService/StartEnableService) lives in the Windows IVendorServices implementation.
    /// </summary>
    public static class ProcessHelper
    {
        public const string SingleInstanceName = "GHelperApp";

        private static IProcessService Svc => PlatformServices.Current.Process;
        private static ISystemInfo Sys => PlatformServices.Current.System;

        public static bool IsRunningAsSystem() => Sys.IsRunningAsSystem;

        public static bool IsUserAdministrator() => Sys.IsElevated;

        /// <summary>
        /// Legacy behaviour: a newly started G-Helper replaces the running one. The platform lock is released by the
        /// first instance when it is signalled, the new instance keeps retrying for a few seconds.
        /// Returns false when the old instance could not be replaced (app should exit).
        /// </summary>
        public static bool TryAcquireSingleInstance(int timeoutMs = 5000)
        {
            Action onSecondInstance = () =>
            {
                Logger.WriteLine("Another instance started, exiting");
                try { Svc.ReleaseSingleInstance(); } catch { }
                Program.Ui.Quit();
            };

            var watch = Stopwatch.StartNew();
            do
            {
                try
                {
                    if (Svc.AcquireSingleInstance(SingleInstanceName, onSecondInstance)) return true;
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Single instance check failed: " + ex.Message);
                    return true;
                }
                Thread.Sleep(250);
            } while (watch.ElapsedMilliseconds < timeoutMs);

            return false;
        }

        public static void CheckAlreadyRunning()
        {
            if (TryAcquireSingleInstance()) return;

            Program.Ui.Alert(Properties.Strings.AppAlreadyRunning, Properties.Strings.AppAlreadyRunningText);
            Program.Ui.Quit();
        }

        public static void RunAsAdmin(string? param = null, bool force = false) => Svc.RunAsAdmin(param, force);

        public static void KillByName(string name) => Svc.KillByName(name);

        public static void KillSmartDisplayControl() => PlatformServices.Current.Vendor.KillSmartDisplayControl();

        public static void KillByProcess(Process process)
        {
            string name;
            try { name = process.ProcessName; } catch { name = "?"; }

            try
            {
                process.Kill();
                Logger.WriteLine($"Stopped: {name}");
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Failed to stop: {name} {ex.Message}");
            }
        }

        public static string RunCMD(string name, string args, string? directory = null, int timeoutMs = 0)
            => Svc.RunCommand(name, args, directory, timeoutMs);

        public static void SetPriority(ProcessPriorityClass priorityClass = ProcessPriorityClass.Normal)
            => Svc.SetPriority(priorityClass);
    }
}
