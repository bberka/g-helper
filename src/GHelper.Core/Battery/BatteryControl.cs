using GHelper.Helpers;
using GHelper.Platform;

namespace GHelper.Battery
{
    public static class BatteryControl
    {

        static bool _chargeFull = AppConfig.Is("charge_full");
        public static bool chargeFull
        {
            get
            {
                return _chargeFull;
            }
            set
            {
                AppConfig.Set("charge_full", value ? 1 : 0);
                _chargeFull = value;
            }
        }

        public static void ToggleBatteryLimitFull()
        {
            if (chargeFull) SetBatteryChargeLimit();
            else SetBatteryLimitFull();
        }

        public static void SetBatteryLimitFull()
        {
            chargeFull = true;
            Program.acpi.DeviceSet(AsusACPI.BatteryLimit, 100, "BatteryLimit");
            Program.Ui.Refresh("battery");
        }

        public static void UnSetBatteryLimitFull()
        {
            chargeFull = false;
            Logger.WriteLine("Battery fully charged");
            Program.Ui.Refresh("battery");
        }

        public static void AutoBattery(bool init = false)
        {
            if (chargeFull && !init) SetBatteryLimitFull();
            else SetBatteryChargeLimit();
        }

        /// <summary>Mirrors the limit into ASUS System Control Interface (Windows, needs elevation). No-op elsewhere.</summary>
        public static void SetAsusChargeLimit(int value)
        {
            if (!ProcessHelper.IsUserAdministrator()) return;

            try
            {
                PlatformCapabilities.GetOptional<IAsusChargeLimitSync>()?.SetChargingRate(value);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Failed to set ChargingRate: {ex.Message}");
            }
        }

        public static void SetBatteryChargeLimit(int setLimit = -1)
        {
            int limit = setLimit;
            if (limit < 0) limit = AppConfig.Get("charge_limit");
            if (limit < 40 || limit > 100) return;

            if (AppConfig.IsChargeLimit6080())
            {
                if (limit > 85) limit = 100;
                else if (limit >= 80) limit = 80;
                else if (limit < 60) limit = 60;
            }

            if (setLimit > 0) SetAsusChargeLimit(limit);

            Program.acpi.DeviceSet(AsusACPI.BatteryLimit, limit, "BatteryLimit");

            AppConfig.Set("charge_limit", limit);
            chargeFull = false;

            Program.Ui.Refresh("battery");
        }

        /// <summary>Windows powercfg battery report (opens battery-report.html). Other platforms: not available.</summary>
        public static void BatteryReport()
        {
            if (PlatformServices.Current.System.Kind != PlatformKind.Windows)
            {
                Logger.WriteLine("Battery report is only available on Windows");
                return;
            }

            var reportDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            Task.Run(() =>
            {
                try
                {
                    PlatformServices.Current.Process.RunCommand("powershell", "powercfg /batteryreport; explorer battery-report.html", reportDir, 60000);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(ex.Message);
                }
            });
        }

    }
}
