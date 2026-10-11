using GHelper.AnimeMatrix;
using GHelper.App.ViewModels;
using GHelper.Battery;
using GHelper.Display;
using GHelper.Fan;
using GHelper.Helpers;
using GHelper.Input;
using GHelper.Platform;
using GHelper.USB;

namespace GHelper.App.Backend;

/// <summary>
/// The "logic owner" half of the main window: port of the behaviour of the legacy <c>SettingsForm</c> (app/Settings.cs)
/// on top of GHelper.Core. Button / slider handlers live here, the Visualise* routines (model -> view model) in
/// <c>MainBackend.Refresh.cs</c>, the sensor timer in <c>MainBackend.Sensors.cs</c>.
/// <para>
/// Threading: <see cref="Execute"/> / <see cref="SetValue"/> / <see cref="RefreshArea"/> are called on the UI thread.
/// Handlers that write hardware run one after another on a background queue (<see cref="Background"/>), as the legacy
/// handlers blocked the UI thread. Results reach the view model through <c>Program.Ui.Post</c>.
/// </para>
/// </summary>
public sealed partial class MainBackend : IMainBackend, IUiActionHandler, IDisposable
{
    /// <summary>Debounce of the battery charge limit slider (legacy batteryTimer, 200 ms after the last change).</summary>
    public const int BatteryDebounceMs = 200;

    private readonly MainViewModel _vm;
    private readonly IMainShell _shell;

    private readonly object _queueLock = new();
    private Task _queue = Task.CompletedTask;
    private System.Threading.Timer? _batteryDebounce;
    private bool _disposed;

    public MainBackend(MainViewModel viewModel, IMainShell? shell = null)
    {
        _vm = viewModel;
        _shell = shell ?? NullMainShell.Instance;
        Background = EnqueueBackground;
        Parallel = action => Task.Run(action);
    }

    /// <summary>
    /// Runs hardware handlers off the UI thread, strictly one after another. Tests replace it with
    /// <c>action => action()</c>.
    /// </summary>
    public Action<Action> Background { get; set; }

    /// <summary>Independent background work (sensor reads, battery lookups). Tests replace it with an inline call.</summary>
    public Action<Action> Parallel { get; set; }

    /// <summary>Make both schedulers synchronous (tests).</summary>
    public void RunInline()
    {
        Background = action => action();
        Parallel = action => action();
    }

    public MainViewModel ViewModel => _vm;

    private static void OnUi(Action action) => Program.Ui.Post(action);

    private void EnqueueBackground(Action action)
    {
        lock (_queueLock)
        {
            _queue = _queue.ContinueWith(_ => action(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    /// <summary>Run a handler on <see cref="Background"/>; exceptions are logged, never thrown into the UI.</summary>
    private void Run(string name, Action work)
    {
        Background(() =>
        {
            try { work(); }
            catch (Exception ex) { Logger.WriteLine($"{name}: {ex.Message}"); }
        });
    }

    // ============================== Execute ===================================================================

    public void Execute(MainAction action)
    {
        try
        {
            ExecuteCore(action);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"UI action {action}: {ex.Message}");
        }
    }

    private void ExecuteCore(MainAction action)
    {
        switch (action)
        {
            // ---- performance ----
            case MainAction.PerformanceSilent:
                Run("Silent", () => Program.modeControl.SetPerformanceMode(AsusACPI.PerformanceSilent));
                break;
            case MainAction.PerformanceBalanced:
                Run("Balanced", () => Program.modeControl.SetPerformanceMode(AsusACPI.PerformanceBalanced));
                break;
            case MainAction.PerformanceTurbo:
                Run("Turbo", () => Program.modeControl.SetPerformanceMode(AsusACPI.PerformanceTurbo));
                break;
            case MainAction.FansAndPower:
                ToggleSideWindow("fans", 0);
                break;
            case MainAction.ToggleFanUnits:
                FanSensorControl.fanRpm = !FanSensorControl.fanRpm;
                RefreshSensors(true);
                break;

            // ---- GPU ----
            case MainAction.GpuEco:
                Run("GPU Eco", () => Program.gpuControl.SetGPUMode(AsusACPI.GPUModeEco));
                break;
            case MainAction.GpuStandard:
                Run("GPU Standard", () => Program.gpuControl.SetGPUMode(AsusACPI.GPUModeStandard));
                break;
            case MainAction.GpuUltimate:
                Run("GPU Ultimate", () => Program.gpuControl.SetGPUMode(AsusACPI.GPUModeUltimate));
                break;
            case MainAction.GpuOptimized:
                Run("GPU Optimized", ToggleOptimizedGpu);
                break;
            case MainAction.GpuStopApps:
                Run("GPU stop apps", () => Program.gpuControl.KillGPUApps());
                break;
            case MainAction.GpuXgm:
                Run("XGM", () => Program.gpuControl.ToggleXGM());
                break;
            case MainAction.GpuErrorClick:
                GpuErrorClick();
                break;

            // ---- screen ----
            case MainAction.ScreenAuto:
                Run("Screen auto", () =>
                {
                    ScreenControl.SetAutoRefresh(1);
                    ScreenControl.AutoScreen();
                });
                break;
            case MainAction.Screen60Hz:
                Run("Screen min rate", () =>
                {
                    ScreenControl.SetAutoRefresh(0);
                    ScreenControl.SetScreen(ScreenControl.MIN_RATE, 0);
                });
                break;
            case MainAction.Screen120Hz:
                Run("Screen max rate", () =>
                {
                    ScreenControl.SetAutoRefresh(0);
                    ScreenControl.SetScreen(ScreenControl.MAX_REFRESH, 1);
                });
                break;
            case MainAction.ScreenMiniLed:
                Run("Miniled", () => ScreenControl.ToogleMiniled());
                break;
            case MainAction.ScreenFhd:
                Run("FHD", ScreenControl.ToogleFHD);
                break;
            case MainAction.ScreenHdrControl:
                Run("HDR control", ScreenControl.ToogleHDRControl);
                break;
            case MainAction.InstallColorProfiles:
                InstallColorProfiles();
                break;
            case MainAction.DismissVisualHint:
                // legacy LabelVisual_Click: hide the hint and apply the visual modes anyway
                VisualControl.forceVisual = true;
                _vm.VisualHintVisible = false;
                break;

            // ---- matrix ----
            case MainAction.MatrixOpen:
                ToggleSideWindow(AniMatrixControl.Instance.IsSlash ? "slash" : "matrix", 0);
                break;

            // ---- keyboard ----
            case MainAction.KeyboardColor:
                PickColor("aura_color", Aura.Color1, allowRandom: true);
                break;
            case MainAction.KeyboardColor2:
                PickColor("aura_color2", Aura.Color2, allowRandom: false);
                break;
            case MainAction.RearColor:
                PickColor("rear_color", Aura.RearColor, allowRandom: false);
                break;
            case MainAction.KeyboardExtra:
                ToggleSideWindow("extra", 0);
                break;
            case MainAction.FnLock:
                Run("Fn-Lock", InputDispatcher.ToggleFnLock);
                break;
            case MainAction.Overlay:
            case MainAction.OverlayAlly:
                ToggleSideWindow("overlay", 0);
                break;
            case MainAction.OverlayToggleOnly:
                ToggleOverlay();
                break;
            case MainAction.BacklightHintClick:
                if (AppConfig.IsDynamicLighting() && DynamicLightingHelper.IsEnabled())
                    Run("Dynamic Lighting", DynamicLightingHelper.OpenSettings);
                break;

            // ---- ROG Ally ----
            case MainAction.AllyControllerMode:
                Run("Controller mode", Program.allyControl.ToggleMode);
                break;
            case MainAction.AllyBacklight:
                Run("Ally backlight", Program.allyControl.ToggleBacklight);
                break;
            case MainAction.AllyController:
                ToggleSideWindow("handheld", 0);
                break;
            case MainAction.AutoTdp:
                Run("AutoTDP", Program.allyControl.ToggleAutoTDP);
                break;
            case MainAction.FpsLimit:
                Run("FPS limit", () => Program.allyControl.ToggleFPSLimit());
                break;

            // ---- battery ----
            case MainAction.BatteryFull:
                Run("Battery full", BatteryControl.ToggleBatteryLimitFull);
                break;
            case MainAction.BatteryStatusClick:
                HardwareControl.chargeWatt = !HardwareControl.chargeWatt;
                RefreshSensors(true);
                break;
            case MainAction.BatteryReport:
            case MainAction.ChargeLabelClick:
                Run("Battery report", BatteryControl.BatteryReport);
                break;
            case MainAction.ChargeHoverEnter:
                SetBatteryHover(true);
                break;
            case MainAction.ChargeHoverLeave:
                SetBatteryHover(false);
                break;
            case MainAction.BatteryFullHoverEnter:
                SetBatteryFullHover(true);
                break;
            case MainAction.BatteryFullHoverLeave:
                SetBatteryFullHover(false);
                break;

            // ---- footer ----
            case MainAction.VersionClick:
                _update?.Update();
                break;
            case MainAction.Updates:
                ToggleSideWindow("updates", 0);
                break;
            case MainAction.Donate:
                DonateClick();
                break;
            case MainAction.DonateAlreadyDonated:
                DonateAlreadyDonated();
                break;
            case MainAction.DonateNotInterested:
                DonateNotInterested();
                break;
            case MainAction.Armoury:
                ArmouryClick();
                break;
            case MainAction.AmdOled:
                Run("AMD OLED", () =>
                {
                    if (VisualControl.DisableOledPowerOptimization()) OnUi(() => _vm.AmdOled.IsVisible = false);
                });
                break;
            case MainAction.EnergySaver:
                Run("Energy saver", () => KeyboardHook.KeyKeyPress(Keys.LWin, Keys.A));
                break;

            case MainAction.Peripheral1:
                OpenPeripheral(0);
                break;
            case MainAction.Peripheral2:
                OpenPeripheral(1);
                break;
            case MainAction.Peripheral3:
                OpenPeripheral(2);
                break;
            case MainAction.PeripheralHover1:
                PeripheralHover(0);
                break;
            case MainAction.PeripheralHover2:
                PeripheralHover(1);
                break;
            case MainAction.PeripheralHover3:
                PeripheralHover(2);
                break;

            case MainAction.Quit:
                Quit();
                break;
        }
    }

    // ============================== SetValue ==================================================================

    public void SetValue(MainValue value, int newValue)
    {
        try
        {
            SetValueCore(value, newValue);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"UI value {value}: {ex.Message}");
        }
    }

    private void SetValueCore(MainValue value, int v)
    {
        switch (value)
        {
            case MainValue.BatteryLimit:
                ScheduleBatteryLimit(v);
                break;

            case MainValue.Gamma:
                // inline: only stores the value and restarts Core's own debounce timer; running it on the queue would
                // let the "brightness" refresh move the slider back while it is being dragged
                VisualControl.SetBrightness(v);
                break;

            case MainValue.AuraMode:
                Run("Aura mode", () =>
                {
                    AppConfig.Set("aura_mode", v);
                    ApplyAura();
                });
                break;

            case MainValue.RearMode:
                Run("Rear mode", () =>
                {
                    AppConfig.Set("rear_mode", v);
                    ApplyAura();
                });
                break;

            case MainValue.MatrixBrightness:
                Run("Matrix brightness", () =>
                {
                    AppConfig.Set("matrix_brightness", v);
                    AniMatrixControl.Instance.SetDevice();
                });
                break;

            case MainValue.MatrixRunning:
                SetMatrixRunning(v);
                break;

            case MainValue.VisualMode:
            case MainValue.ColorTemp:
                ApplyVisualMode();
                break;

            case MainValue.Gamut:
                Run("Gamut", () => VisualControl.SetGamut(v));
                break;

            case MainValue.RunOnStartup:
                Run("Startup", () =>
                {
                    if (v == 1) Startup.Schedule();
                    else Startup.UnSchedule();
                });
                break;

            case MainValue.PerformanceMode:
                Run("Performance mode", () => Program.modeControl.SetPerformanceMode(v));
                break;

            case MainValue.BwTrayIcon:
                AppConfig.Set("bw_icon", v);
                _vm.BwTrayIcon = v == 1;
                break;

            case MainValue.OverlayGameOnly:
                SetOverlayGameOnly(v == 1);
                break;
        }
    }

    private void ScheduleBatteryLimit(int limit)
    {
        // The slider moves continuously; the limit is written once it settled.
        lock (_queueLock)
        {
            if (_disposed) return;
            _batteryDebounce ??= new System.Threading.Timer(_ => ApplyBatteryLimit(), null, Timeout.Infinite, Timeout.Infinite);
            _pendingBatteryLimit = limit;
            _batteryDebounce.Change(BatteryDebounceMs, Timeout.Infinite);
        }
    }

    private int _pendingBatteryLimit = -1;

    /// <summary>Writes the pending slider value now (the debounce timer calls this).</summary>
    public void ApplyBatteryLimit()
    {
        int limit;
        lock (_queueLock)
        {
            limit = _pendingBatteryLimit;
            _pendingBatteryLimit = -1;
        }

        if (limit < 0) return;
        Run("Battery limit", () => BatteryControl.SetBatteryChargeLimit(limit));
    }

    private void ApplyVisualMode()
    {
        int visual = _vm.SelectedVisualMode?.Key ?? AppConfig.Get("visual", (int)VisualControl.GetDefaultVisualMode());
        int temp = _vm.SelectedColorTemp?.Key ?? AppConfig.Get("color_temp", VisualControl.DefaultColorTemp);

        _vm.VisualCombosEnabled = (SplendidCommand)visual != SplendidCommand.Disabled;
        Run("Visual mode", () => VisualControl.SetVisual((SplendidCommand)visual, temp));
    }

    // ============================== Hotkey actions (IUiActionHandler) =========================================

    public void RunAction(string action, int arg)
    {
        switch (action)
        {
            case UiActions.CycleAuraMode:
                CycleAuraMode(arg);
                break;
            case UiActions.CycleVisualMode:
                CycleVisualMode(arg);
                break;
            case UiActions.CycleMatrix:
                CycleMatrix(arg);
                break;
            case UiActions.ToggleOverlay:
                ToggleOverlay(arg == 1);
                break;
        }
    }

    private void CycleVisualMode(int delta)
    {
        var items = _vm.VisualModes;
        if (items.Count < 1) return;

        int index = Cycle(items.IndexOf(_vm.SelectedVisualMode!), items.Count, delta);
        _vm.SelectedVisualMode = items[index]; // reports the new value to SetValue(VisualMode)
        Program.Toast(items[index].Text, ToastIcon.BrightnessUp);
    }

    private void CycleAuraMode(int delta)
    {
        var items = _vm.AuraModes;
        if (items.Count < 1) return;

        int index = Cycle(items.IndexOf(_vm.SelectedAuraMode!), items.Count, delta);
        _vm.SelectedAuraMode = items[index]; // reports the new value to SetValue(AuraMode)
        Program.Toast(items[index].Text, ToastIcon.BacklightUp);
    }

    private void CycleMatrix(int delta)
    {
        var items = _vm.MatrixBrightnessItems;
        int current = Math.Max(0, items.IndexOf(_vm.SelectedMatrixBrightness));
        int index = Math.Clamp(current + delta, 0, items.Count - 1);

        _vm.SelectMatrixBrightness(index);
        AppConfig.Set("matrix_brightness", index);
        Run("Matrix brightness", () => AniMatrixControl.Instance.SetDevice());
        Program.Toast(items[index].Text, delta > 0 ? ToastIcon.BacklightUp : ToastIcon.BacklightDown);
    }

    /// <summary>Wrap-around step used by the combo box hotkeys; an unknown current item counts as the first.</summary>
    internal static int Cycle(int current, int count, int delta)
    {
        if (current < 0) current = 0;
        if (delta > 0) return current < count - 1 ? current + 1 : 0;
        return current > 0 ? current - 1 : count - 1;
    }

    // ============================== Side windows ==============================================================

    /// <summary>Legacy xxxToggle: close the window when it is open, otherwise show it.</summary>
    private void ToggleSideWindow(string name, int arg)
    {
        if (_shell.IsSideWindowOpen(name)) _shell.CloseSideWindow(name);
        else Program.Ui.ShowWindow(name, arg);
    }

    // ============================== Overlay ===================================================================

    public void ToggleOverlay(bool fromHotkey = false)
    {
        bool enable = !AppConfig.IsOverlay();
        AppConfig.Set("overlay", enable ? 1 : 0);
        Logger.WriteLine("Overlay " + (enable ? "On" : "Off") + (AppConfig.IsOverlayGameOnly() ? " (game only)" : ""));

        var overlay = Program.hardwareOverlay;
        if (overlay is not null)
        {
            Run("Overlay", () =>
            {
                if (enable) overlay.StartOverlay();
                else overlay.StopOverlay();
            });
        }

        VisualiseOverlay();

        if (fromHotkey && AppConfig.IsOverlayGameOnly())
            Program.Toast(Properties.Strings.Overlay + " " + (enable ? Properties.Strings.On : Properties.Strings.Off));
    }

    private void SetOverlayGameOnly(bool gameOnly)
    {
        AppConfig.Set("overlay_game_only", gameOnly ? 1 : 0);

        var overlay = Program.hardwareOverlay;
        if (AppConfig.IsOverlay() && overlay is not null)
        {
            Run("Overlay", () =>
            {
                overlay.StopOverlay();
                overlay.StartOverlay();
            });
        }

        VisualiseOverlay();
    }

    // ============================== Quit ======================================================================

    private void Quit()
    {
        // legacy ButtonQuit_Click; the rest of the cleanup runs from the application exit handler (Shutdown)
        Program.Ui.Quit();
    }

    /// <summary>Release devices held by the app. Idempotent; called once from the application exit handler.</summary>
    public static void ReleaseDevices()
    {
        try { AsusLampArray.Release(); }
        catch (Exception ex) { Logger.WriteLine("Release lamp array: " + ex.Message); }

        try { (Program.matrixControl ?? AniMatrixControl.Instance).Dispose(); }
        catch (Exception ex) { Logger.WriteLine("Release matrix: " + ex.Message); }
    }

    public void Dispose()
    {
        lock (_queueLock)
        {
            _disposed = true;
            _batteryDebounce?.Dispose();
            _batteryDebounce = null;
        }

        _sensorTimer?.Dispose();
        _sensorTimer = null;
    }
}
