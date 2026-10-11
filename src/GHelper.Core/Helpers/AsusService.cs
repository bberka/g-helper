using GHelper.Ally;
using GHelper.Platform;

namespace GHelper.Helpers
{
    /// <summary>
    /// OEM service handling. Detection / stop / start of the actual services is done by <see cref="IVendorServices"/>
    /// (Windows: Armoury Crate services, Linux: asusd/supergfxd); the bookkeeping that is platform independent
    /// (config flags, Ally controller mode, startup re-stop) stays here.
    /// </summary>
    public static class AsusService
    {
        private static IVendorServices Svc => PlatformServices.Current.Vendor;

        public static bool IsAsusOptimizationRunning() => Svc.IsAsusOptimizationRunning();

        public static bool IsArmouryRunning()
        {
            bool running = Svc.IsArmouryRunning();
            Logger.WriteLine($"Armoury running: {running}");
            return running;
        }

        public static void RunArmouryUninstaller() => Svc.RunArmouryUninstaller();

        public static bool IsOSDRunning() => Svc.IsOSDRunning();

        public static int GetRunningCount() => Svc.GetRunningCount();

        public static void StopAsusServices()
        {
            Svc.StopAsusServices();

            if (GetRunningCount() == 0) AppConfig.Set("services_disabled", 1);

            if (AppConfig.IsAlly()) AllyControl.ApplyMode((ControllerMode)AppConfig.Get("controller_mode", (int)ControllerMode.Auto), true);
        }

        public static void StartAsusServices()
        {
            AppConfig.Set("services_disabled", 0);
            Svc.StartAsusServices();
        }

        public static void StopOnStartup()
        {
            if (AppConfig.Is("services_skip")) return;
            if (!AppConfig.Is("services_disabled") || !ProcessHelper.IsUserAdministrator()) return;
            if (GetRunningCount() == 0) return;

            Logger.WriteLine("ASUS services revived, re-stopping on startup");
            Task.Run(() => StopAsusServices());
        }

    }

}
