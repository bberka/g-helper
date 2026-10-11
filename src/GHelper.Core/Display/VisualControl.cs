using GHelper.Platform;
using PawnIO;

namespace GHelper.Display
{
    public enum SplendidGamut : int
    {
        VivoNative = 0,
        VivoSRGB = 1,
        VivoDCIP3 = 3,
        ViviDisplayP3 = 4,
        Native = 50,
        sRGB = 51,
        DCIP3 = 53,
        DisplayP3 = 54
    }

    public enum SplendidCommand : int
    {
        None = -1,

        VivoNormal = 1,
        VivoVivid = 2,
        VivoManual = 6,
        VivoEycare = 7,

        Init = 10,
        DimmingVivo = 9,
        DimmingVisual = 19,
        DimmingDuo = 109,
        DimmingVisualDuo = 319,

        GamutMode = 200,
        GamutModeDuo = 201,

        Default = 11,
        Racing = 21,
        Scenery = 22,
        RTS = 23,
        FPS = 24,
        Cinema = 25,
        Vivid = 13,
        Eyecare = 17,
        EReading = 212,
        EReadingDuo = 213,
        EReadingVivo = 210,
        EReadingVivoDuo = 211,
        Disabled = 18,
    }

    /// <summary>Arguments of one AsusSplendid.exe visual-mode call.</summary>
    public readonly record struct SplendidArguments(SplendidCommand Command, int Param1, int? Param2, int? Param3);

    /// <summary>
    /// ASUS Splendid / GameVisual control (colour modes, gamut, OLED dimming). Splendid is a Windows-only ASUS component:
    /// without it (<see cref="IDisplayService.SplendidDirectory"/> is null) every command is a no-op.
    /// </summary>
    public static class VisualControl
    {
        private static int _brightness = 100;
        private static bool _init = true;
        private static bool _download = true;
        private static string? _splendidPath = null;

        private static System.Timers.Timer brightnessTimer = new System.Timers.Timer(200);

        public const int DefaultColorTemp = 50;

        public static bool forceVisual = false;
        public static bool skipGamut = false;

        /// <summary>
        /// True while Program.Ui should show the "AMD OLED power optimisation blocks colour modes" notice
        /// (legacy settingsForm.VisualiseAmdOled). Refresh area "amdoled".
        /// </summary>
        public static bool AmdOledNotice { get; set; }

        static VisualControl()
        {
            brightnessTimer.Elapsed += BrightnessTimerTimer_Elapsed;
        }

        private static IDisplayService Display => PlatformServices.Current.Display;

        private static string CommonData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        public static string GetGameVisualPath()
        {
            return Path.Combine(CommonData, "ASUS", "GameVisual");
        }

        public static string GetVivobookPath()
        {
            return Path.Combine(CommonData, "ASUS", "ASUS System Control Interface", "ASUSOptimization", "Splendid");
        }

        public static SplendidGamut GetDefaultGamut()
        {
            return AppConfig.IsVivoZenPro() ? SplendidGamut.VivoNative : SplendidGamut.Native;
        }

        public static bool IsEReading()
        {
            return File.Exists(AppConfig.IsVivoZenPro() ? GetVivobookPath() : Path.Combine(GetGameVisualPath(), "Asus_Monochrome.icm"));
        }

        public static bool IsOledPowerOptimization() => AppConfig.IsOLED() && CpuInfo.IsAMD && HardwareControl.AmdApu().GetOledPowerOptimization();
        public static bool DisableOledPowerOptimization() => HardwareControl.AmdApu().DisableOledPowerOptimization();

        public static Dictionary<SplendidGamut, string> GetGamutModes()
        {

            bool isVivo = AppConfig.IsVivoZenPro();

            Dictionary<SplendidGamut, string> _modes = new Dictionary<SplendidGamut, string>();

            string iccPath = isVivo ? GetVivobookPath() : GetGameVisualPath();

            if (!Directory.Exists(iccPath))
            {
                Logger.WriteLine(iccPath + " doesn't exist");
                return _modes;
            }

            try
            {
                DirectoryInfo d = new DirectoryInfo(iccPath);
                FileInfo[] icms = d.GetFiles("*.icm");
                if (icms.Length == 0) return _modes;

                _modes.Add(isVivo ? SplendidGamut.VivoNative : SplendidGamut.Native, "Gamut: Native");
                foreach (FileInfo icm in icms)
                {
                    if (icm.Name.Contains("sRGB"))
                    {
                        try
                        {
                            _modes.Add(isVivo ? SplendidGamut.VivoSRGB : SplendidGamut.sRGB, "Gamut: sRGB");
                            Logger.WriteLine(icm.FullName + " sRGB");
                        }
                        catch
                        {
                        }
                    }

                    if (icm.Name.Contains("DCIP3"))
                    {
                        try
                        {
                            _modes.Add(isVivo ? SplendidGamut.VivoDCIP3 : SplendidGamut.DCIP3, "Gamut: DCIP3");
                            Logger.WriteLine(icm.FullName + " DCIP3");
                        }
                        catch
                        {
                        }
                    }

                    if (icm.Name.Contains("DisplayP3"))
                    {
                        try
                        {
                            _modes.Add(isVivo ? SplendidGamut.ViviDisplayP3 : SplendidGamut.DisplayP3, "Gamut: DisplayP3");
                            Logger.WriteLine(icm.FullName + " DisplayP3");
                        }
                        catch
                        {
                        }
                    }
                }
                return _modes;
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ex.ToString());
                return _modes;
            }

        }

        public static SplendidCommand GetDefaultVisualMode()
        {
            return AppConfig.IsVivoZenPro() ? SplendidCommand.VivoNormal : SplendidCommand.Default;
        }

        public static Dictionary<SplendidCommand, string> GetVisualModes()
        {

            if (AppConfig.IsVivoZenPro())
            {
                return new Dictionary<SplendidCommand, string>
                {
                    { SplendidCommand.VivoNormal, "Default" },
                    { SplendidCommand.VivoVivid, "Vivid" },
                    { SplendidCommand.VivoManual, "Manual" },
                    { SplendidCommand.VivoEycare, "Eyecare" },
                    { SplendidCommand.EReading, "E-Reading"},
                };
            }

            return new Dictionary<SplendidCommand, string>
            {
                { SplendidCommand.Default, "Default"},
                { SplendidCommand.Racing, "Racing"},
                { SplendidCommand.Scenery, "Scenery"},
                { SplendidCommand.RTS, "RTS/RPG"},
                { SplendidCommand.FPS, "FPS"},
                { SplendidCommand.Cinema, "Cinema"},
                { SplendidCommand.Vivid, "Vivid" },
                { SplendidCommand.Eyecare, "Eyecare"},
                { SplendidCommand.EReading, "E-Reading"},
                { SplendidCommand.Disabled, "Disabled"}
            };
        }

        public static Dictionary<int, string> GetTemperatures()
        {
            return new Dictionary<int, string>
            {
                { 0, "Warmest"},
                { 15, "Warmer"},
                { 30, "Warm"},
                { 50, "Neutral"},
                { 70, "Cold"},
                { 85, "Colder"},
                { 100, "Coldest"},
            };
        }

        public static Dictionary<int, string> GetEyeCares()
        {
            return new Dictionary<int, string>
            {
                { 0, "0"},
                { 1, "1"},
                { 2, "2"},
                { 3, "3"},
                { 4, "4"},
            };
        }

        /// <summary>GameVisual "ActiveGVStatus" (Windows registry value, always enabled elsewhere).</summary>
        public static bool IsEnabled()
        {
            return Display.GetGameVisualStatus() > 0;
        }

        public static void SetRegStatus(int status = 1)
        {
            Display.SetGameVisualStatus(status);
        }

        public static void InitGamut()
        {
            int gamut = AppConfig.Get("gamut");

            if (gamut < 0) return;
            if ((SplendidGamut)gamut == SplendidGamut.Native || (SplendidGamut)gamut == SplendidGamut.VivoNative) return;

            SetGamut(gamut);
        }

        public static void SetGamut(int mode = -1)
        {
            if (skipGamut) return;
            if (mode < 0) mode = (int)GetDefaultGamut();

            AppConfig.Set("gamut", mode);

            var result = RunSplendid(SplendidCommand.GamutMode, 0, mode);
            if (result == 0) return;
            if (result == -1)
            {
                Logger.WriteLine("Gamut setting refused, reverting.");
                RunSplendid(SplendidCommand.GamutMode, 0, (int)GetDefaultGamut());
                if (PlatformServices.Current.System.IsElevated && _download)
                {
                    _download = false;
                    _ = ColorProfileHelper.InstallProfile();
                }
            }
            if (result == 1 && _init)
            {
                _init = false;
                RunSplendid(SplendidCommand.Init);
                RunSplendid(SplendidCommand.GamutMode, 0, mode);
            }
        }

        /// <summary>
        /// Maps a UI colour mode + white balance to the AsusSplendid.exe call (legacy switch inside SetVisual).
        /// The command can change: E-Reading on a Vivobook Duo uses the Vivo variant.
        /// </summary>
        public static SplendidArguments BuildVisualArguments(SplendidCommand mode, int whiteBalance, bool isDuo, bool isVivoZenPro)
        {
            int param1 = 0;
            int? param2 = null;
            int? param3 = null;

            switch (mode)
            {
                case SplendidCommand.Disabled:
                    param1 = 2;
                    break;
                case SplendidCommand.Eyecare:
                    param2 = 4;
                    break;
                case SplendidCommand.VivoNormal:
                case SplendidCommand.VivoVivid:
                    param2 = null;
                    break;
                case SplendidCommand.VivoEycare:
                    param2 = Math.Abs(whiteBalance - 50) * 3 / 50 + (whiteBalance < 50 ? 1 : 0);
                    break;
                case SplendidCommand.EReading:
                    if (isDuo && isVivoZenPro) mode = SplendidCommand.EReadingVivo;
                    param2 = 2;            // Contrast
                    param3 = whiteBalance; // Color Temp
                    break;
                default:
                    param2 = whiteBalance;
                    break;
            }

            return new SplendidArguments(mode, param1, param2, param3);
        }

        public static void SetVisual(SplendidCommand mode = SplendidCommand.Default, int whiteBalance = DefaultColorTemp, bool init = false)
        {
            if (mode == SplendidCommand.None) return;
            if ((mode == SplendidCommand.Default || mode == SplendidCommand.VivoNormal) && whiteBalance == DefaultColorTemp && init) return; // Skip default setting on init
            if (mode == SplendidCommand.Disabled && !CpuInfo.IsAMD && init) return; // Skip disabled setting for Intel devices

            AppConfig.Set("visual", (int)mode);
            AppConfig.Set("color_temp", whiteBalance);

            Task.Run(() =>
            {
                if (!forceVisual && (ScreenCCD.GetHDRStatus(out bool acm, true) || acm)) return;
                if (!forceVisual && ScreenNative.GetRefreshRate(ScreenNative.FindLaptopScreen(true)) < 0) return;

                if (!init && mode == SplendidCommand.EReading && !PlatformServices.Current.System.IsElevated && !IsEReading()) PlatformServices.Current.Process.RunAsAdmin();

                var args = BuildVisualArguments(mode, whiteBalance, AppConfig.IsDUO(), AppConfig.IsVivoZenPro());
                SplendidCommand command = args.Command;

                int result = RunSplendid(command, args.Param1, args.Param2, args.Param3);
                if (result == 0) return;
                if (result == -1)
                {
                    Logger.WriteLine("Visual mode setting refused, reverting.");
                    RunSplendid(SplendidCommand.Default, 0, DefaultColorTemp);
                    if (PlatformServices.Current.System.IsElevated && _download)
                    {
                        _download = false;
                        _ = ColorProfileHelper.InstallProfile();
                    }
                }
                if (result == 1 && _init)
                {
                    _init = false;
                    RunSplendid(SplendidCommand.Init);
                    RunSplendid(command, 0, args.Param2, args.Param3);
                }
            });
        }

        private static string? GetSplendidPath()
        {
            if (_splendidPath == null)
            {
                try
                {
                    _splendidPath = Display.SplendidDirectory;
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(ex.Message);
                }
            }

            return _splendidPath;
        }

        /// <summary>The second-screen (Duo) twin of a Splendid command, None when there is none / not a Duo.</summary>
        public static SplendidCommand GetDuoCommand(SplendidCommand command, bool isDuo)
        {
            if (!isDuo) return SplendidCommand.None;

            switch (command)
            {
                case SplendidCommand.VivoNormal:
                case SplendidCommand.VivoVivid:
                case SplendidCommand.VivoManual:
                case SplendidCommand.VivoEycare:
                    return command + 100;
                case SplendidCommand.DimmingVivo:
                    return SplendidCommand.DimmingDuo;
                case SplendidCommand.DimmingVisual:
                    return SplendidCommand.DimmingVisualDuo;
                case SplendidCommand.GamutMode:
                    return SplendidCommand.GamutModeDuo;
                case SplendidCommand.EReadingVivo:
                    return SplendidCommand.EReadingVivoDuo;
                case SplendidCommand.EReading:
                    return SplendidCommand.EReadingDuo;
                case SplendidCommand.Default:
                case SplendidCommand.Vivid:
                case SplendidCommand.Eyecare:
                case SplendidCommand.Disabled:
                case SplendidCommand.Racing:
                case SplendidCommand.Scenery:
                case SplendidCommand.RTS:
                case SplendidCommand.FPS:
                case SplendidCommand.Cinema:
                    return command + 300;
                default:
                    return SplendidCommand.None;
            }
        }

        /// <summary>OLED dimming level handed to Splendid for a 0..100 brightness slider value.</summary>
        public static int GetDimmingLevel(int brightness) => (int)(40 + brightness * 0.6);

        private static int RunSplendid(SplendidCommand command, int? param1 = null, int? param2 = null, int? param3 = null)
        {
            if (IsOledPowerOptimization())
            {
                Logger.WriteLine("Skipping command due to AMD OLED Power Optimization flag");
                AmdOledNotice = true;
                Program.Ui.Refresh("amdoled");
                return 0;
            }

            string? splendidPath = GetSplendidPath();
            if (string.IsNullOrEmpty(splendidPath)) return 0;

            string splendidExe = Path.Combine(splendidPath, "AsusSplendid.exe");
            bool isVivo = AppConfig.IsVivoZenPro();
            bool isSplenddid = File.Exists(splendidExe);

            if (ScreenNative.FindLaptopScreen() == null && ScreenNative.IsExternalDisplayConnected())
            {
                Logger.WriteLine("Skipping Splendid: internal display is off with external display connected");
                return 0;
            }

            if (isSplenddid)
            {
                var process = PlatformServices.Current.Process;

                var duoCommand = GetDuoCommand(command, AppConfig.IsDUO());
                if (duoCommand != SplendidCommand.None) process.RunCommand(splendidExe, (int)duoCommand + " " + param1 + " " + param2 + " " + param3, splendidPath);

                var result = process.RunCommand(splendidExe, (int)command + " " + param1 + " " + param2 + " " + param3, splendidPath);
                if (result.Contains("file not exist") || (result.Length == 0 && !isVivo)) return 1;
                if (result.Contains("return code: -1")) return -1;
                if (result.Contains("Visual is disabled"))
                {
                    SetRegStatus(1);
                    return 1;
                }
            }

            return 0;
        }

        private static void BrightnessTimerTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            brightnessTimer.Stop();

            var dimmingCommand = AppConfig.IsVivoZenPro() ? SplendidCommand.DimmingVivo : SplendidCommand.DimmingVisual;
            var dimmingLevel = GetDimmingLevel(_brightness);

            if (RunSplendid(dimmingCommand, 0, dimmingLevel) == 0) return;

            if (_init)
            {
                _init = false;
                RunSplendid(SplendidCommand.Init);
                RunSplendid(SplendidCommand.Init, 4);
                if (RunSplendid(dimmingCommand, 0, dimmingLevel) == 0) return;
            }

        }

        public static void InitBrightness()
        {
            if (!AppConfig.IsOLED()) return;
            if (!AppConfig.SaveDimming()) return;

            int brightness = GetBrightness();
            if (brightness >= 0) SetBrightness(brightness);
        }

        private static bool IsOnBattery()
        {
            return AppConfig.SaveDimming() && PlatformServices.Current.Power.GetPowerSource() != PowerSource.AC;
        }

        public static int GetBrightness()
        {
            return AppConfig.Get(IsOnBattery() ? "brightness_battery" : "brightness", 100);
        }

        public static int SetBrightness(int brightness = -1, int delta = 0)
        {
            if (!AppConfig.IsOLED()) return -1;
            if (brightness < 0) brightness = GetBrightness();

            _brightness = Math.Max(0, Math.Min(100, brightness + delta));
            AppConfig.Set(IsOnBattery() ? "brightness_battery" : "brightness", _brightness);

            brightnessTimer.Stop();
            brightnessTimer.Start();

            Program.Ui.Refresh("brightness");

            return _brightness;
        }

        public static void ResetGamut()
        {
            int defaultGamut = (int)GetDefaultGamut();

            if (AppConfig.Get("gamut") != defaultGamut)
            {
                skipGamut = true;
                AppConfig.Set("gamut", defaultGamut);
                Program.Ui.Refresh("gamut");
                skipGamut = false;
            }
        }

    }
}
