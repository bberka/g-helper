using Avalonia.Media;
using GHelper.Ally;
using GHelper.AnimeMatrix;
using GHelper.App.Services;
using GHelper.App.Theme;
using GHelper.App.ViewModels;
using GHelper.Battery;
using GHelper.Display;
using GHelper.Gpu;
using GHelper.Helpers;
using GHelper.Mode;
using GHelper.Peripherals;
using GHelper.Platform;
using GHelper.Properties;
using GHelper.USB;
using DrawingColor = System.Drawing.Color;

namespace GHelper.App.Backend;

// Port of the Visualise* / Init* routines of app/Settings.cs: read the Core model, push plain values into the view model.
public sealed partial class MainBackend
{
    /// <summary>
    /// One time set-up that the legacy SettingsForm constructor did: window title, check boxes, visual modes, ally
    /// section, donate badge. Core's own start-up (<c>Program.Start</c>) asks for the rest through Refresh(area).
    /// Never throws.
    /// </summary>
    public void Initialize()
    {
        Guard("title", () =>
        {
            _vm.WindowTitle = "G-Helper " + (ProcessHelper.IsUserAdministrator() ? "—" : "-") + " " + AppConfig.GetModelShort();
        });

        Guard("battery", () =>
        {
            if (AppConfig.IsChargeLimit6080()) _vm.BatterySupportedValues = new List<int> { 60, 65, 70, 75, 80, 100 };
            VisualiseBattery();
        });

        Guard("startup", () =>
        {
            // schtasks / systemd query: do not block the UI thread
            Parallel(() =>
            {
                bool scheduled = Startup.IsScheduled();
                OnUi(() => _vm.VisualiseRunOnStartup(scheduled));
            });
        });

        Guard("tray", () =>
        {
            _vm.BwTrayIcon = AppConfig.IsBWIcon();
            _vm.OverlayGameOnly = AppConfig.IsOverlayGameOnly();
            VisualiseOverlay();
            RefreshContextMenu();
            _vm.VisualiseFnLock(AppConfig.Is("fn_lock"));
        });

        Guard("visual", InitVisual);
        Guard("ally", () => { if (AppConfig.IsAlly()) RefreshAlly(); });
        Guard("update", () =>
        {
            _update = new AutoUpdate.AutoUpdateControl();
            RefreshVersion();
        });
        Guard("donate", InitDonate);
        Guard("sensor timer", InitSensorTimer);
        Guard("energy saver", RefreshEnergySaver);
        Guard("keyboard", RefreshKeyboard);
    }

    private static void Guard(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { Logger.WriteLine($"Main window init ({what}): {ex.Message}"); }
    }

    // ============================== Refresh(area) =============================================================

    public void RefreshArea(string area, MainViewModel viewModel)
    {
        try
        {
            switch (area.ToLowerInvariant())
            {
                case "mode": VisualiseMode(); break;
                case "modelabel": _vm.SetModeLabel(ModeControl.ModeLabel); break;
                case "contextmenu": RefreshContextMenu(); break;
                case "gpu": RefreshGpu(); break;
                case "xgm": RefreshXgm(-1); break;
                case "screen": RefreshScreen(); break;
                case "brightness": _vm.VisualiseBrightness(VisualControl.GetBrightness()); break;
                case "gamut": _vm.SelectGamut(AppConfig.Get("gamut", (int)VisualControl.GetDefaultGamut())); break;
                case "amdoled": _vm.AmdOled.IsVisible = true; break;
                case "armoury": RefreshArmoury(); break;
                case "battery": VisualiseBattery(); break;
                case "keyboard":
                case "keyboardlabel":
                    RefreshKeyboard();
                    break;
                case "fnlock": _vm.VisualiseFnLock(AppConfig.Is("fn_lock")); break;
                case "matrix": RefreshMatrix(); break;
                case "ally": RefreshAlly(); break;
                case "autotdp": _vm.VisualiseAutoTdp(AllyControl.AutoTDP); break;
                case "fpslimit": _vm.VisualiseFpsLimit(AllyControl.FpsLimit); break;
                case "controller": _vm.VisualiseController(ControllerText(AllyControl.Mode)); break;
                case "backlight": _vm.VisualiseBacklight(AllyControl.Backlight); break;
                case "peripherals": RefreshPeripherals(); break;
                case "version": RefreshVersion(); break;
                case "energysaver": RefreshEnergySaver(); break;
                case "overlay": VisualiseOverlay(); break;
                case "theme": RefreshTheme(); break;
                case "sensors": RefreshSensors(true); break;
                case "visual": InitVisual(); break;

                // icon: the tray follows the view model. fans / fansresult / fansaxis: the side windows listen to
                // AvaloniaUiHost.AreaRefreshed themselves.
                case "icon":
                case "fans":
                case "fansresult":
                case "fansaxis":
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Refresh({area}): {ex.Message}");
        }
    }

    // ============================== Mode / tray ===============================================================

    private void VisualiseMode()
    {
        int mode = Modes.GetCurrent();
        _vm.VisualiseMode(mode, Modes.GetBase(mode));
    }

    /// <summary>Port of the data part of SetContextMenu: entries of the performance section + toggles.</summary>
    private void RefreshContextMenu()
    {
        _vm.TrayModes = Modes.GetDictonary().Select(m => new ChoiceItem(m.Key, m.Value)).ToList();
        VisualiseMode();
        _vm.BwTrayIcon = AppConfig.IsBWIcon();
        _vm.OverlayGameOnly = AppConfig.IsOverlayGameOnly();
        VisualiseOverlay();
    }

    private void VisualiseOverlay()
    {
        _vm.VisualiseOverlay(AppConfig.IsOverlay());
        _vm.OverlayGameOnly = AppConfig.IsOverlayGameOnly();
    }

    private void RefreshTheme()
    {
        UiSettings.LoadFromConfig();
        AppTheme.Apply(UiSettings.UiMode, UiSettings.Theme);
        _vm.VisualiseFnLock(AppConfig.Is("fn_lock"));
        VisualiseBattery();
    }

    // ============================== GPU =======================================================================

    private void RefreshGpu()
    {
        if (GPUModeControl.ModesHidden) _vm.HideGpuModes(GPUModeControl.GpuPanelVisible);
        else _vm.VisualiseGpuButtons(GPUModeControl.EcoSupported, GPUModeControl.MuxSupported);

        if (GPUModeControl.IsLocked)
        {
            _vm.LockGpuModes(GPUModeControl.LockText);
            return;
        }

        int mode = GPUModeControl.RequestedVisualMode;
        if (mode == -1) mode = AppConfig.Get("gpu_mode");

        bool ally = AppConfig.IsAlly();
        _vm.VisualiseGpuMode(mode, AppConfig.Is("gpu_auto"), AppConfig.IsAlwaysUltimate(), GPUModeControl.gpuError, ally);

        RefreshXgm(ally ? -1 : mode);
    }

    private void RefreshXgm(int gpuMode)
    {
        Parallel(() =>
        {
            bool connected = false;
            int activated = -1;
            try
            {
                connected = Program.acpi?.IsXGConnected() ?? false;
                if (connected) activated = Program.acpi!.DeviceGet(AsusACPI.GPUXG);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("XGM state: " + ex.Message);
            }

            bool amdIgpu = AppConfig.IsAMDiGPU();
            OnUi(() => _vm.VisualiseXgm(connected, activated, gpuMode, amdIgpu));
        });
    }

    private void ToggleOptimizedGpu()
    {
        AppConfig.Set("gpu_auto", AppConfig.Get("gpu_auto") == 1 ? 0 : 1);
        Program.Ui.Refresh("gpu");
        Program.gpuControl.AutoGPUMode(true);
    }

    private void GpuErrorClick()
    {
        if (GPUModeControl.gpuError is null) return;

        GPUModeControl.CheckGpuError();
        if (PlatformServices.Current.Kind == PlatformKind.Windows)
        {
            try { PlatformServices.Current.Process.OpenUrl("devmgmt.msc"); }
            catch (Exception ex) { Logger.WriteLine("Device manager: " + ex.Message); }
        }
    }

    // ============================== Screen / visual ===========================================================

    private void RefreshScreen()
    {
        _shell.SetMinRate(ScreenControl.MIN_RATE);

        var s = ScreenControl.State;
        if (s is null) return;

        _vm.VisualiseScreen(s.ScreenEnabled, s.ScreenAuto, s.Frequency, s.MaxFrequency, s.Overdrive, s.OverdriveSetting,
            s.Miniled1, s.Miniled2, s.Hdr, s.Acm, s.Fhd, s.HdrControl, ScreenControl.MIN_RATE, AppConfig.NoGpu());

        // colour profiles were installed meanwhile (legacy: InitVisual again after the download)
        if (_vm.InstallColors.IsVisible && VisualControl.GetGamutModes().Count > 0) InitVisual();
    }

    /// <summary>Port of SettingsForm.InitVisual.</summary>
    public void InitVisual()
    {
        if (AppConfig.Is("hide_visual")) return;

        if (AppConfig.IsOLED())
        {
            _vm.GammaSectionVisible = true;
            _vm.GammaSliderVisible = true;
            _vm.GammaTitle = Strings.FlickerFreeDimming + " / " + Strings.VisualMode;
            _vm.VisualiseBrightness(VisualControl.GetBrightness());
        }
        else
        {
            _vm.GammaTitle = Strings.VisualMode;
        }

        var gamuts = VisualControl.GetGamutModes();

        if (gamuts.Count == 0)
        {
            // colour profiles can be downloaded?
            if (ColorProfileHelper.ProfileExists())
            {
                _vm.InstallColors.IsVisible = true;
                _vm.GammaSectionVisible = true;
                _vm.VisualTableVisible = true;
            }
            return;
        }

        _vm.InstallColors.IsVisible = false;
        _vm.GammaSectionVisible = true;
        _vm.VisualTableVisible = true;

        var visualValue = (SplendidCommand)AppConfig.Get("visual", (int)VisualControl.GetDefaultVisualMode());
        int colorTempValue = AppConfig.Get("color_temp", VisualControl.DefaultColorTemp);

        _vm.SetVisualModes(VisualControl.GetVisualModes().Select(m => new ChoiceItem((int)m.Key, m.Value)), (int)visualValue);
        _vm.SetColorTemps(VisualControl.GetTemperatures().Select(m => new ChoiceItem(m.Key, m.Value)), colorTempValue);

        // apply the stored visual mode once (legacy: SetVisual(.., init: true) from the form constructor)
        Parallel(() =>
        {
            try { VisualControl.SetVisual(visualValue, colorTempValue, true); }
            catch (Exception ex) { Logger.WriteLine("Visual init: " + ex.Message); }
        });

        _vm.VisualModeVisible = true;
        _vm.ColorTempVisible = true;
        VisualiseDisabled();

        if (gamuts.Count <= 1) return;

        _vm.SetGamuts(gamuts.Select(m => new ChoiceItem((int)m.Key, m.Value)),
            AppConfig.Get("gamut", (int)VisualControl.GetDefaultGamut()));
        _vm.GamutVisible = true;
    }

    private void VisualiseDisabled() =>
        _vm.VisualCombosEnabled = (SplendidCommand)AppConfig.Get("visual") != SplendidCommand.Disabled;

    private void InstallColorProfiles()
    {
        Parallel(async () =>
        {
            try
            {
                await ColorProfileHelper.InstallProfile();
                OnUi(InitVisual);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Colour profiles: " + ex.Message);
            }
        });
    }

    // ============================== Battery ===================================================================

    private void VisualiseBattery()
    {
        int limit = AppConfig.Get("charge_limit");
        if (limit < 40 || limit > 100) limit = 100;
        _vm.VisualiseBattery(limit, BatteryControl.chargeFull);
    }

    // ============================== Matrix ====================================================================

    private void RefreshMatrix()
    {
        var matrix = AniMatrixControl.Instance;

        IEnumerable<ChoiceItem>? slashModes = null;
        if (matrix.IsSlash)
            slashModes = SlashDevice.Modes.Select((m, i) => new ChoiceItem(i, m.Value)).ToList();

        _vm.InitMatrix(matrix.IsValid, matrix.IsSlash, slashModes,
            Math.Max(0, AppConfig.Get("matrix_brightness", 0)), Math.Max(0, AppConfig.Get("matrix_running", 0)));
    }

    private void SetMatrixRunning(int mode)
    {
        var matrix = AniMatrixControl.Instance;

        Run("Matrix running", () =>
        {
            AppConfig.Set("matrix_running", mode);
            matrix.SetDevice();
        });

        // legacy: picking "text" opens the matrix window so the text can be typed
        if (!matrix.IsSlash && mode == (int)MatrixMode.Text && !_shell.IsSideWindowOpen("matrix"))
            Program.Ui.ShowWindow("matrix", 0);
    }

    // ============================== Ally ======================================================================

    private void RefreshAlly()
    {
        if (!AppConfig.IsAlly()) return;

        _vm.VisualiseAlly(AppConfig.IsOverlay());
        _vm.VisualiseController(ControllerText(AllyControl.Mode));
        _vm.VisualiseBacklight(AllyControl.Backlight);
        _vm.VisualiseFpsLimit(AllyControl.FpsLimit);
        _vm.VisualiseAutoTdp(AllyControl.AutoTDP);
    }

    internal static string ControllerText(ControllerMode mode) => mode switch
    {
        ControllerMode.Gamepad => "Gamepad",
        ControllerMode.Mouse => "Mouse",
        ControllerMode.Skip => "Skip",
        _ => "Auto",
    };

    // ============================== Footer ====================================================================

    private void RefreshVersion()
    {
        if (_update is null) return;
        _vm.SetVersionLabel(_update.VersionLabel, _update.VersionLabelHighlight);
    }

    private void RefreshEnergySaver()
    {
        bool active = false;
        try { active = PlatformServices.Current.Power.GetBatterySaverStatus(); }
        catch (Exception ex) { Logger.WriteLine("Battery saver: " + ex.Message); }
        _vm.EnergySaver.IsVisible = active;
    }

    private void RefreshArmoury()
    {
        Parallel(() =>
        {
            bool running = false;
            try { running = AsusService.IsArmouryRunning(); }
            catch (Exception ex) { Logger.WriteLine("Armoury check: " + ex.Message); }
            OnUi(() => _vm.Armoury.IsVisible = running);
        });
    }

    private void ArmouryClick()
    {
        if (!Program.Ui.Confirm("Armoury Crate", "Armoury Crate is active, download official uninstaller app?")) return;
        Run("Armoury uninstaller", AsusService.RunArmouryUninstaller);
    }

    private AutoUpdate.AutoUpdateControl? _update;

    // ============================== Colour helpers ============================================================

    internal static Color ToAvalonia(DrawingColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);
}
