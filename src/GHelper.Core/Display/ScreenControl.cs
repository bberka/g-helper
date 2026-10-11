using GHelper.Gpu;
using GHelper.Platform;

namespace GHelper.Display
{
    /// <summary>
    /// What legacy <c>settingsForm.VisualiseScreen(...)</c> received. <see cref="ScreenControl.State"/> holds the latest
    /// snapshot, the UI reads it when <c>Program.Ui.Refresh("screen")</c> fires.
    /// </summary>
    public sealed record ScreenState(
        bool ScreenEnabled,
        bool ScreenAuto,
        int Frequency,
        int MaxFrequency,
        int Overdrive,
        bool OverdriveSetting,
        int Miniled1,
        int Miniled2,
        bool Hdr,
        bool Acm,
        int Fhd,
        int HdrControl);

    public static class ScreenControl
    {

        public const int MAX_REFRESH = 1000;
        public static int MIN_RATE = AppConfig.Get("min_rate", 60);
        public static int MAX_RATE = AppConfig.Get("max_rate");

        /// <summary>Latest result of <see cref="InitScreen"/>, null before the first call.</summary>
        public static ScreenState? State { get; private set; }

        private static bool IsOnAc() => PlatformServices.Current.Power.GetPowerSource() == PowerSource.AC;

        public static int GetMaxRate(string? laptopScreen)
        {
            if (MAX_RATE > 0) return MAX_RATE;
            else return ScreenNative.GetMaxRefreshRate(laptopScreen);
        }

        public static void AutoScreen(bool force = false)
        {
            if (force || AppConfig.Is("screen_auto"))
            {
                if (IsOnAc())
                    SetScreen(MAX_REFRESH, 1);
                else
                    SetScreen(MIN_RATE, 0);
            }
            else
            {
                SetScreen(overdrive: AppConfig.Get("overdrive"));
            }
        }

        public static void SetAutoRefresh(int auto)
        {
            AppConfig.Set("screen_auto", auto);
            if (auto == 0) SetAsusRefreshFlag(0);
        }

        public static void SetAsusRefreshFlag(int value)
        {
            if (!PlatformServices.Current.System.IsElevated) return;
            try
            {
                PlatformServices.Current.Display.SetAsusRefreshFlag(value);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Failed to set RefreshFlag: {ex.Message}");
            }
        }

        public static void ToggleScreenRate()
        {
            var laptopScreen = ScreenNative.FindLaptopScreen(true);
            var refreshRate = ScreenNative.GetRefreshRate(laptopScreen);
            if (refreshRate < 0) return;

            ScreenNative.SetRefreshRate(laptopScreen!, refreshRate > MIN_RATE ? MIN_RATE : GetMaxRate(laptopScreen));
            InitScreen();
        }


        public static void SetScreen(int frequency = -1, int overdrive = -1, int miniled = -1)
        {
            var laptopScreen = ScreenNative.FindLaptopScreen(true);
            var refreshRate = ScreenNative.GetRefreshRate(laptopScreen);

            if (refreshRate < 0) return;

            if (frequency >= MAX_REFRESH)
            {
                frequency = GetMaxRate(laptopScreen);
            }

            if (frequency > 0 && frequency != refreshRate)
            {
                ScreenNative.SetRefreshRate(laptopScreen!, frequency);
            }

            if (Program.acpi.IsOverdriveSupported() && overdrive >= 0)
            {
                if (AppConfig.IsNoOverdrive()) overdrive = 0;
                if (overdrive != Program.acpi.DeviceGet(AsusACPI.ScreenOverdrive))
                {
                    Program.acpi.DeviceSet(AsusACPI.ScreenOverdrive, overdrive, "ScreenOverdrive");
                }
            }

            SetMiniled(miniled);

            InitScreen();
        }

        public static void SetMiniled(int miniled = -1)
        {
            if (miniled >= 0)
            {
                if (Program.acpi.IsSupported(AsusACPI.ScreenMiniled1))
                    Program.acpi.DeviceSet(AsusACPI.ScreenMiniled1, miniled, "Miniled1");
                else
                {
                    Program.acpi.DeviceSet(AsusACPI.ScreenMiniled2, miniled, "Miniled2");
                    Thread.Sleep(100);
                }
            }
        }

        public static void InitMiniled()
        {
            if (AppConfig.IsForceMiniled())
            {
                if (ScreenCCD.IsHDR()) SetHDRControl(AppConfig.Get("hdr_control"));
                else SetMiniled(AppConfig.Get("miniled"));
            }

            InitELMB();
        }

        public static void InitELMB()
        {
            if (!AppConfig.IsELMB()) return;

            int elmb = AppConfig.Get("elmb");
            if (elmb >= 0 && !ScreenCCD.IsHDR()) ScreenELMB.Set(elmb);
        }

        public static void InitOptimalBrightness()
        {
            int optimalBrightness = AppConfig.Get("optimal_brightness");
            if (optimalBrightness >= 0) SetOptimalBrightness(optimalBrightness);
        }

        public static void SetOptimalBrightness(int status)
        {
            AppConfig.Set("optimal_brightness", status);
            if (status == 2) status = PlatformServices.Current.Power.GetPowerSource() == PowerSource.Battery ? 1 : 0;
            Program.acpi.DeviceSet(AsusACPI.ScreenOptimalBrightness, status, "Optimal Brightness");
        }

        public static int GetOptimalBrightness()
        {
            return Program.acpi.DeviceGet(AsusACPI.ScreenOptimalBrightness);
        }

        public static void ToogleFHD()
        {
            int fhd = Program.acpi.DeviceGet(AsusACPI.ScreenFHD);
            Logger.WriteLine($"FHD Toggle: {fhd}");

            if (Program.Ui.Confirm(Properties.Strings.AlertUltimateTitle, "Changing display mode requires reboot"))
            {
                Program.acpi.DeviceSet(AsusACPI.ScreenFHD, (fhd == 1) ? 0 : 1, "FHD");
                SystemReboot.Reboot();
            }
        }

        public static void SetHDRControl(int status = -1)
        {
            if (status >= 0)
            {
                AppConfig.Set("hdr_control", status);
                Program.acpi.DeviceSet(AsusACPI.ScreenHDRControl, status, "HDR Control");
            }
        }

        public static void ToogleHDRControl()
        {
            int hdrControl = Program.acpi.DeviceGet(AsusACPI.ScreenHDRControl);
            Logger.WriteLine($"HDR Control Toggle: {hdrControl}");
            SetHDRControl((hdrControl == 1) ? 1 : 0);
            Thread.Sleep(200);
            InitScreen();
        }

        public static string ToogleMiniled()
        {
            int miniled1 = Program.acpi.DeviceGet(AsusACPI.ScreenMiniled1);
            int miniled2 = Program.acpi.DeviceGet(AsusACPI.ScreenMiniled2);

            Logger.WriteLine($"MiniledToggle: {miniled1} {miniled2}");

            int miniled;
            string name;

            if (miniled1 >= 0)
            {
                switch (miniled1)
                {
                    case 1:
                        miniled = 0;
                        name = Properties.Strings.OneZone;
                        break;
                    default:
                        miniled = 1;
                        name = Properties.Strings.Multizone;
                        break;
                }
            }
            else
            {
                switch (miniled2)
                {
                    case 1:
                        miniled = 2;
                        name = Properties.Strings.OneZone;
                        break;
                    case 2:
                        miniled = 0;
                        name = Properties.Strings.Multizone;
                        break;
                    default:
                        miniled = 1;
                        name = Properties.Strings.MultizoneStrong;
                        break;
                }
            }

            AppConfig.Set("miniled", miniled);
            SetScreen(miniled: miniled);

            return name;
        }

        public static void InitScreen()
        {
            var laptopScreen = ScreenNative.FindLaptopScreen();
            int frequency = ScreenNative.GetRefreshRate(laptopScreen);
            int maxFrequency = GetMaxRate(laptopScreen);

            if (maxFrequency > 0) AppConfig.Set("max_frequency", maxFrequency);
            else maxFrequency = AppConfig.Get("max_frequency");

            bool screenAuto = AppConfig.Is("screen_auto");
            bool overdriveSetting = Program.acpi.IsOverdriveSupported() && !AppConfig.IsNoOverdrive();

            int overdrive = overdriveSetting ? Program.acpi.DeviceGet(AsusACPI.ScreenOverdrive) : 0;

            int miniled1 = Program.acpi.DeviceGet(AsusACPI.ScreenMiniled1);
            int miniled2 = Program.acpi.DeviceGet(AsusACPI.ScreenMiniled2);

            int miniled = (miniled1 >= 0) ? miniled1 : miniled2;
            bool hdr = false;
            bool acm = false;

            if (miniled >= 0)
            {
                Logger.WriteLine($"Miniled: {miniled1} {miniled2}");
                AppConfig.Set("miniled", miniled);
            }

            try
            {
                hdr = ScreenCCD.GetHDRStatus(out acm);
            } catch (Exception ex)
            {
                Logger.WriteLine(ex.Message);
            }

            bool screenEnabled = (frequency >= 0);

            int fhd = -1;
            if (AppConfig.IsDUO())
            {
                fhd = Program.acpi.DeviceGet(AsusACPI.ScreenFHD);
            }

            int hdrControl = Program.acpi.DeviceGet(AsusACPI.ScreenHDRControl);
            if (hdrControl >= 0) Logger.WriteLine($"HDR Control Status: {hdrControl}");

            AppConfig.Set("frequency", frequency);
            AppConfig.Set("overdrive", overdrive);

            State = new ScreenState(
                ScreenEnabled: screenEnabled,
                ScreenAuto: screenAuto,
                Frequency: frequency,
                MaxFrequency: maxFrequency,
                Overdrive: overdrive,
                OverdriveSetting: overdriveSetting,
                Miniled1: miniled1,
                Miniled2: miniled2,
                Hdr: hdr,
                Acm: acm,
                Fhd: fhd,
                HdrControl: hdrControl);

            Program.Ui.Refresh("screen");
        }
    }
}
