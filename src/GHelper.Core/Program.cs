using GHelper.Platform;

namespace GHelper
{
    /// <summary>
    /// UI-agnostic application state + orchestration (legacy app/Program.cs). The UI toolkit specific half
    /// (windows, tray icon, toasts) lives in GHelper.App and talks to Core through <see cref="IUiHost"/>.
    /// Kept as a static class called "Program" so ported code keeps compiling unchanged (Program.acpi, ...).
    /// </summary>
    public static partial class Program
    {
        public static AsusACPI acpi = default!;

        /// <summary>Set by the app head right after start. Core never touches UI types directly.</summary>
        public static IUiHost Ui = NullUiHost.Instance;

        public enum PowerSource { Battery, Barrel, USBC }

        public static PowerSource currentSource = PowerSource.Battery;

        public static bool usbcProfile = AppConfig.Is("usbc_profile");

        public static PowerSource ReadPowerSource()
        {
            if (PlatformServices.Current.Power.GetPowerSource() != Platform.PowerSource.AC)
                return PowerSource.Battery;

            int chargerMode = acpi?.DeviceGet(AsusACPI.ChargerMode) ?? 0;
            if (chargerMode > 0 && (chargerMode & AsusACPI.ChargerBarrel) == 0)
                return PowerSource.USBC;

            return PowerSource.Barrel;
        }

        public static int PerformanceKey() =>
            usbcProfile ? (int)ReadPowerSource() : (PlatformServices.Current.Power.GetPowerSource() == Platform.PowerSource.AC ? 1 : 0);
    }
}
