using GHelper.Ally;
using GHelper.AnimeMatrix;
using GHelper.Battery;
using GHelper.Display;
using GHelper.Gpu;
using GHelper.Helpers;
using GHelper.Input;
using GHelper.Mode;
using GHelper.Overlay;
using GHelper.Peripherals;
using GHelper.Platform;
using GHelper.USB;
using System.Globalization;
using System.Text;

namespace GHelper
{
    /// <summary>
    /// Core half of the legacy app/Program.cs: startup sequence, auto modes, power / session / lid handling.
    /// Windows specific plumbing (SystemEvents, WM_POWERBROADCAST, tray icon, windows) is replaced by
    /// <see cref="IPowerService"/> events and <see cref="IUiHost"/> calls.
    /// </summary>
    public static partial class Program
    {
        public static ModeControl modeControl = default!;
        public static GPUModeControl gpuControl = default!;
        public static AllyControl allyControl = default!;
        public static ClamshellModeControl clamshellControl = default!;

        public static InputDispatcher? inputDispatcher;
        public static GHelper.Overlay.IHardwareOverlay? hardwareOverlay;

        /// <summary>AniMe Matrix / Slash controller (legacy settingsForm.matrixControl).</summary>
        public static AniMatrixControl? matrixControl;

        private static long lastAuto;
        private static readonly object autoLock = new();

        private static readonly System.Timers.Timer powerSettleTimer = new() { AutoReset = false };
        private static Platform.PowerSource lastLineStatus = Platform.PowerSource.Unknown;

        private static bool eventsAttached;

        /// <summary>Version of the Core assembly (all projects share <c>Directory.Build.props</c> Version).</summary>
        public static Version AppVersion => typeof(Program).Assembly.GetName().Version ?? new Version(0, 0);

        /// <summary>Legacy <c>Program.toast.RunToast(text, icon)</c>. The icon enum name is passed as icon key.</summary>
        public static void Toast(string text, ToastIcon? icon = null) => Ui.Toast(text, icon?.ToString());

        #region Startup

        /// <summary>
        /// Legacy Main() minus the UI toolkit: runs everything up to "application is running".
        /// The app head creates its windows / tray icon around this call (<see cref="Ui"/> must be set before).
        /// </summary>
        /// <returns>false when the process should exit (already running, no ACPI, one-shot "charge" action).</returns>
        public static bool Start(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Logger.WriteLine("Unhandled: " + e.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, e) => { Logger.WriteLine("Unobserved: " + e.Exception); e.SetObserved(); };

            string action = "";
            if (args.Length > 0) action = args[0];

            if (action == "charge")
            {
                Charge();
                return false;
            }

            ApplyCulture();

            Logger.WriteLine("----------------------");
            Logger.WriteLine("App launched: " + AppConfig.GetModel() + " :" + AppVersion + CultureInfo.CurrentUICulture + (ProcessHelper.IsUserAdministrator() ? "." : ""));

            modeControl = new ModeControl();
            gpuControl = new GPUModeControl();
            allyControl = new AllyControl();
            clamshellControl = new ClamshellModeControl();
            matrixControl = new AniMatrixControl();

            if (!ProcessHelper.TryAcquireSingleInstance())
            {
                Ui.Alert(Properties.Strings.AppAlreadyRunning, Properties.Strings.AppAlreadyRunningText);
                Ui.Quit();
                return false;
            }

            ProcessHelper.SetPriority();

            CleanupLegacyFiles();

            var startCount = AppConfig.Get("start_count") + 1;
            AppConfig.Set("start_count", startCount);
            Logger.WriteLine("Start Count: " + startCount);

            acpi = new AsusACPI();

            if (!acpi.IsConnected() && AppConfig.IsASUS() && !AppConfig.IsDesktop())
            {
                bool open = Ui.Confirm(Properties.Strings.StartupError, Properties.Strings.ACPIError);
                if (open) PlatformServices.Current.Process.OpenUrl("https://www.asus.com/support/FAQ/1047338/");

                Ui.Quit();
                return false;
            }

            ProcessHelper.KillSmartDisplayControl();
            AsusService.StopOnStartup();

            HardwareControl.RecreateGpuControl();

            Modes.InitFullSpeed();
            Ui.Refresh("contextmenu");

            inputDispatcher = new InputDispatcher();

            InitAura();
            Ui.Refresh("keyboard");
            Ui.Refresh("matrix");

            ScreenControl.InitScreen();

            SetAutoModes(init: true);

            AttachPlatformEvents();

            clamshellControl.RegisterDisplayEvents();
            clamshellControl.ToggleLidAction();

            Task.Run(() =>
            {
                PeripheralsProvider.DetectAllAsusMice();
                PeripheralsProvider.DetectAllAsusKeyboards();
                PeripheralsProvider.DetectAllAsusHeadsets();
            });
            PeripheralsProvider.RegisterForDeviceEvents();

            // started by hand from its own folder (or with an action): show the window
            if (action.Length > 0 || IsStartedFromInstallDirectory())
            {
                Ui.ToggleSettings(false);
            }

            switch (action)
            {
                case "cpu":
                    Startup.ReScheduleAdmin();
                    Ui.ShowWindow("fans", 0);
                    break;
                case "gpu":
                    Startup.ReScheduleAdmin();
                    Ui.ShowWindow("fans", 1);
                    break;
                case "services":
                    Ui.ShowWindow("extra", 1);
                    break;
                case "uv":
                    Startup.ReScheduleAdmin();
                    Ui.ShowWindow("fans", 2);
                    modeControl.SetRyzen();
                    break;
                case "colors":
                    Task.Run(async () =>
                    {
                        await ColorProfileHelper.InstallProfile();
                        Ui.Post(() => Ui.Refresh("screen"));
                    });
                    break;
                default:
                    Task.Run(Startup.StartupCheck);
                    break;
            }

            Task.Run(() =>
            {
                Ui.Refresh("armoury");
            });

            if (AppConfig.IsOverlay())
                hardwareOverlay?.StartOverlay();

            return true;
        }

        private static bool IsStartedFromInstallDirectory()
        {
            try
            {
                string cwd = Environment.CurrentDirectory.TrimEnd('\\', '/');
                string app = AppContext.BaseDirectory.TrimEnd('\\', '/');
                return string.Equals(cwd, app, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyCulture()
        {
            string? language = AppConfig.GetString("language");
            try
            {
                if (language != null && language.Length > 0)
                    Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                else
                {
                    var culture = CultureInfo.CurrentUICulture;
                    if (culture.ToString() == "kr") culture = CultureInfo.GetCultureInfo("ko");
                    Thread.CurrentThread.CurrentUICulture = culture;
                }
            }
            catch
            {
                Logger.WriteLine("Unknown Language: " + language);
            }
        }

        // Core half of legacy settingsForm.InitAura(): the combo / buttons are rebuilt by the UI on Refresh("keyboard")
        private static void InitAura()
        {
            if (!Aura.IsBacklightDetected)
                Aura.Init();

            Aura.Mode = (AuraMode)AppConfig.Get("aura_mode");
            Aura.Speed = (AuraSpeed)AppConfig.Get("aura_speed");
            Aura.SetColor(AppConfig.Get("aura_color"));
            Aura.SetColor2(AppConfig.Get("aura_color2"));
        }

        /// <summary>One shot startup action of the scheduled task: apply battery limit + keyboard backlight and exit (legacy "charge" argument).</summary>
        public static void Charge()
        {
            if (AppConfig.IsZ13())
            {
                AsusHid.Write([
                    Encoding.ASCII.GetBytes("]ASUS Tech.Inc."),
                    [AsusHid.AURA_ID, 0xC0, 0x03, 0x01]
                ], "Init");
            }

            try
            {
                int limit = AppConfig.Get("charge_limit");
                acpi = new AsusACPI();
                if (limit > 0 && limit < 100)
                {
                    Logger.WriteLine($"------- Startup Battery Limit {limit} -------");
                    if (acpi.IsConnected()) acpi.DeviceSet(AsusACPI.BatteryLimit, limit, "Limit");
                    else AsusACPI.DeviceSetWmi(AsusACPI.BatteryLimit, limit);
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Startup Battery Limit Error: " + ex.Message);
            }

            try
            {
                InputDispatcher.StartupBacklight();
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Startup Backlight: {ex.Message}");
            }

            Ui.Quit();
        }

        static void CleanupLegacyFiles()
        {
            string appDir = AppContext.BaseDirectory;
            string[] legacyFiles = ["WinRing0x64.sys", "WinRing0x64.dll"];

            foreach (string fileName in legacyFiles)
            {
                string filePath = Path.Combine(appDir, fileName);
                if (File.Exists(filePath))
                {
                    try
                    {
                        File.Delete(filePath);
                        Logger.WriteLine($"Deleted legacy file: {fileName}");
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine($"Failed to delete legacy file {fileName}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>Legacy Application.ApplicationExit handler: detach events and device notifications.</summary>
        public static void OnExit()
        {
            try { PeripheralsProvider.UnregisterForDeviceEvents(); } catch (Exception ex) { Logger.WriteLine("Exit: " + ex.Message); }
            clamshellControl?.UnregisterDisplayEvents();
            DetachPlatformEvents();
            powerSettleTimer.Stop();
        }

        #endregion

        #region Auto modes

        public static bool SetAutoModes(bool powerChanged = false, bool init = false, bool wakeup = false)
        {
            int skipDelay = wakeup ? 10000 : 3000;

            if (init) gpuControl?.CaptureNvBootState();

            lock (autoLock)
            {
                if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastAuto) < skipDelay) return false;
                lastAuto = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }

            currentSource = ReadPowerSource();
            Logger.WriteLine("AutoSetting for " + PlatformServices.Current.Power.GetPowerSource());

            BatteryControl.AutoBattery(init);
            if (init) InputDispatcher.InitScreenpad();
            DynamicLightingHelper.Init();
            ScreenControl.InitOptimalBrightness();

            inputDispatcher?.Init();
            //HardwareControl.ReadSensors(true);

            modeControl?.AutoPerformance(powerChanged);

            if (powerChanged) matrixControl?.SetMatrix(true);
            else matrixControl?.SetDevice(true);
            InputDispatcher.InitStatusLed();
            if (init) NumberPad.Init();
            XGM.Init();

            if (AppConfig.IsAlly())
            {
                allyControl?.Init();
            }
            else
            {
                InputDispatcher.AutoKeyboard();
            }

            bool switched = gpuControl?.AutoGPUMode(delay: 1000) ?? false;
            if (!switched)
            {
                gpuControl?.InitGPUMode();
                if (init) gpuControl?.CheckStandardHalfState();
                ScreenControl.AutoScreen();
            }

            ScreenControl.InitMiniled();
            VisualControl.InitBrightness();

            return true;
        }

        #endregion

        #region Power source

        public static void SchedulePowerCheck()
        {
            if (AppConfig.Is("disable_power_event")) return;
            powerSettleTimer.Interval = Math.Max(AppConfig.Get("charger_delay"), 2000);
            powerSettleTimer.Stop();
            powerSettleTimer.Start();
        }

        /// <summary>Evaluates the power source once the charger / battery state stopped flapping (timer callback).</summary>
        public static void OnPowerSettled()
        {
            PowerSource source = ReadPowerSource();
            if (source == currentSource) return;

            Logger.WriteLine($"Power source: {currentSource} -> {source}");
            currentSource = source;
            SetAutoModes(powerChanged: true);
        }

        private static void OnPowerSettled(object? sender, System.Timers.ElapsedEventArgs e) => OnPowerSettled();

        public static void OnChargerEvent() => SchedulePowerCheck();

        #endregion

        #region Platform events

        private static readonly Action<Platform.PowerSource> powerSourceHandler = OnPowerSourceChanged;
        private static readonly Action suspendingHandler = OnSuspending;
        private static readonly Action resumedHandler = OnResumed;
        private static readonly Action<bool> lidHandler = OnLidChanged;
        private static readonly Action<bool> displayHandler = OnDisplayStateChanged;
        private static readonly Action<bool> sessionLockHandler = OnSessionLockChanged;
        private static readonly Action<bool> batterySaverHandler = OnBatterySaverChanged;
        private static readonly Action userPreferencesHandler = OnUserPreferencesChanged;
        private static readonly Action sessionEndingHandler = OnSessionEnding;
        private static ISessionEndingEvents? sessionEndingEvents;

        /// <summary>Subscribe to <see cref="IPowerService"/> events (idempotent). Called by <see cref="Start"/>.</summary>
        public static void AttachPlatformEvents()
        {
            if (eventsAttached) return;
            eventsAttached = true;

            var power = PlatformServices.Current.Power;

            lastLineStatus = power.GetPowerSource();
            powerSettleTimer.Elapsed -= OnPowerSettled;
            powerSettleTimer.Elapsed += OnPowerSettled;

            power.PowerSourceChanged += powerSourceHandler;
            power.Suspending += suspendingHandler;
            power.Resumed += resumedHandler;
            power.LidChanged += lidHandler;
            power.DisplayStateChanged += displayHandler;
            power.SessionLockChanged += sessionLockHandler;
            power.BatterySaverChanged += batterySaverHandler;
            power.UserPreferencesChanged += userPreferencesHandler;

            sessionEndingEvents = PlatformCapabilities.GetOptional<ISessionEndingEvents>();
            if (sessionEndingEvents is not null) sessionEndingEvents.SessionEnding += sessionEndingHandler;
        }

        public static void DetachPlatformEvents()
        {
            if (!eventsAttached) return;
            eventsAttached = false;

            var power = PlatformServices.Current.Power;

            power.PowerSourceChanged -= powerSourceHandler;
            power.Suspending -= suspendingHandler;
            power.Resumed -= resumedHandler;
            power.LidChanged -= lidHandler;
            power.DisplayStateChanged -= displayHandler;
            power.SessionLockChanged -= sessionLockHandler;
            power.BatterySaverChanged -= batterySaverHandler;
            power.UserPreferencesChanged -= userPreferencesHandler;
            powerSettleTimer.Elapsed -= OnPowerSettled;

            if (sessionEndingEvents is not null) sessionEndingEvents.SessionEnding -= sessionEndingHandler;
            sessionEndingEvents = null;
        }

        /// <summary>AC / battery switch (legacy SystemEvents.PowerModeChanged StatusChange).</summary>
        public static void OnPowerSourceChanged(Platform.PowerSource status)
        {
            if (status != lastLineStatus)
            {
                lastLineStatus = status;
                Logger.WriteLine($"Power Mode StatusChange: {status}");
            }

            SchedulePowerCheck();
        }

        /// <summary>System goes to sleep (legacy PBT_APMSUSPEND + PowerModes.Suspend).</summary>
        public static void OnSuspending()
        {
            Logger.WriteLine("System Suspend");
            GPUModeControl.suspended = true;
            modeControl?.SleepReset();

            gpuControl?.StandardModeFix();
            modeControl?.ShutdownReset();
            InputDispatcher.ShutdownStatusLed();
            XGM.NotifyShutdown();
        }

        /// <summary>System woke up (legacy PBT_APMRESUMEAUTOMATIC + PowerModes.Resume).</summary>
        public static void OnResumed()
        {
            Logger.WriteLine("System Resume");
            GPUModeControl.suspended = false;
            BatteryControl.AutoBattery();

            Platform.PowerSource status = PlatformServices.Current.Power.GetPowerSource();
            if (status != lastLineStatus)
            {
                lastLineStatus = status;
                Logger.WriteLine($"Power Mode Resume: {status}");
            }

            SchedulePowerCheck();
        }

        /// <param name="closed">true = lid closed.</param>
        public static void OnLidChanged(bool closed)
        {
            if (closed)
            {
                Logger.WriteLine("Lid Closed");
                BatteryControl.AutoBattery();
                InputDispatcher.lidClose = AniMatrixControl.lidClose = true;
                Aura.ApplyBrightness(0, "Lid");
                matrixControl?.SetLidMode();
            }
            else
            {
                Logger.WriteLine("Lid Open");
                InputDispatcher.InitFNLock();
                InputDispatcher.lidClose = AniMatrixControl.lidClose = false;
                Aura.ApplyBrightness(InputDispatcher.GetBacklight(), "Lid");
                matrixControl?.SetLidMode();
            }
        }

        /// <param name="on">true = monitor on.</param>
        public static void OnDisplayStateChanged(bool on)
        {
            if (!on)
            {
                Logger.WriteLine("Monitor Power Off");
                Aura.SleepBrightness();
                XGM.NotifyShutdown();
                hardwareOverlay?.SuspendForDisplayOff();
            }
            else
            {
                Logger.WriteLine("Monitor Power On");
                GPUModeControl.suspended = false;
                if (!SetAutoModes(wakeup: true)) BatteryControl.AutoBattery();
                hardwareOverlay?.ResumeForDisplayOn();
            }
        }

        /// <param name="locked">true = session locked. false = logon / unlock / console connect.</param>
        public static void OnSessionLockChanged(bool locked)
        {
            if (locked)
            {
                Logger.WriteLine("Session:Lock");
                Aura.sessionLock = true;
                return;
            }

            Logger.WriteLine("Session:Unlock");
            ProcessHelper.KillSmartDisplayControl();
            bool wasLocked = Aura.sessionLock;
            Aura.sessionLock = false;
            if (!AppConfig.Is("skip_aura")) Aura.ApplyAura();
            Task.Delay(2000).ContinueWith(_ =>
            {
                ScreenControl.AutoScreen();
                if (!wasLocked) return;
                if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastAuto) < 10000) return;
                modeControl?.AutoCPUTemp();
            });
        }

        public static void OnBatterySaverChanged(bool active)
        {
            Logger.WriteLine("Battery Saver: " + (active ? 1 : 0));
            Ui.Refresh("energysaver");
        }

        /// <summary>Theme / display preference change: the UI re-reads the OS theme (ISystemInfo.IsDarkTheme).</summary>
        public static void OnUserPreferencesChanged()
        {
            Ui.Refresh("theme");
        }

        /// <summary>User logs off / machine shuts down (legacy SystemEvents.SessionEnding).</summary>
        public static void OnSessionEnding()
        {
            gpuControl?.StandardModeFix();
            modeControl?.ShutdownReset();
            BatteryControl.AutoBattery();
            InputDispatcher.ShutdownStatusLed();
            XGM.NotifyShutdown();
        }

        #endregion
    }
}
