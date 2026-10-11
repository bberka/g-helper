using GHelper.Platform;

namespace GHelper.Mode
{
    /// <summary>
    /// Legacy static PowerNative (PowrProf P/Invoke) as a facade over <see cref="IPowerService"/>.
    /// The Windows implementation holds the original overlay / plan GUID handling (including the "scheme" / "scheme_usbc" config lookup).
    /// </summary>
    public static class PowerNative
    {
        private const string POWER_SILENT = "961cc777-2547-4f9d-8174-7d86181b8a7a";
        private const string POWER_BALANCED = "00000000-0000-0000-0000-000000000000";
        private const string POWER_TURBO = "ded574b5-45a0-4f42-8737-46345c09c238";

        private const string PLAN_HIGH_PERFORMANCE = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        private static readonly Dictionary<string, string> windowsPowerModes = new()
        {
            { POWER_SILENT, "Best Power Efficiency" },
            { POWER_BALANCED, "Balanced" },
            { POWER_TURBO, "Best Performance" },
            { PLAN_HIGH_PERFORMANCE, "High Performance Plan"},
        };

        private static IPowerService Svc => PlatformServices.Current.Power;

        /// <summary>Selectable OS power modes (id -> display name). Platform specific, Windows overlay GUIDs by default.</summary>
        public static Dictionary<string, string> powerModes
        {
            get
            {
                var modes = PlatformCapabilities.GetOptional<IPowerModeCatalog>()?.PowerModes;
                return modes is null || modes.Count == 0
                    ? new Dictionary<string, string>(windowsPowerModes)
                    : modes.ToDictionary(p => p.Key, p => p.Value);
            }
        }

        public static int GetCPUBoost() => Svc.GetCPUBoost();

        public static void SetCPUBoost(int boost = 0) => Svc.SetCPUBoost(boost);

        public static string GetPowerMode() => Svc.GetPowerMode();

        public static void SetPowerMode(string scheme) => Svc.SetPowerMode(scheme);

        public static void SetPowerPlan(string scheme) => Svc.SetPowerPlan(scheme);

        public static string GetDefaultPowerMode(int mode) => Svc.GetDefaultPowerMode(mode);

        public static void SetPowerMode(int mode) => Svc.SetPowerMode(mode);

        public static int GetASPM() => Svc.GetASPM();

        public static void SetASPM(int status = 0) => Svc.SetASPM(status);

        public static void SetBalancedASPM(int status = 0) => Svc.SetBalancedASPM(status);

        public static void SetConnectivityInStandby(int ac = 0, int dc = 0) => Svc.SetConnectivityInStandby(ac, dc);

        public static int GetLidAction(bool ac) => Svc.GetLidAction(ac);

        public static void SetLidAction(int action, bool acOnly = false) => Svc.SetLidAction(action, acOnly);

        public static int GetHibernateAfter() => Svc.GetHibernateAfter();

        public static void SetHibernateAfter(int minutes) => Svc.SetHibernateAfter(minutes);

        public static bool GetBatterySaverStatus() => Svc.GetBatterySaverStatus();
    }
}
