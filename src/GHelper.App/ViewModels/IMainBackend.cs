namespace GHelper.App.ViewModels;

/// <summary>User gestures on the main window (legacy SettingsForm *_Click handlers).</summary>
public enum MainAction
{
    PerformanceSilent, PerformanceBalanced, PerformanceTurbo, FansAndPower,
    /// <summary>Click on the CPU / GPU sensor text: toggle fan RPM / percent (legacy LabelCPUFan_Click).</summary>
    ToggleFanUnits,

    GpuEco, GpuStandard, GpuOptimized, GpuUltimate, GpuStopApps, GpuXgm,
    /// <summary>Click on the GPU title icon when it shows the warning triangle (legacy PictureGPU_Click).</summary>
    GpuErrorClick,

    ScreenAuto, Screen60Hz, Screen120Hz, ScreenMiniLed, ScreenFhd, ScreenHdrControl,
    InstallColorProfiles, DismissVisualHint,

    MatrixOpen,

    KeyboardColor, KeyboardColor2, KeyboardExtra, RearColor,
    FnLock, Overlay, OverlayToggleOnly,
    /// <summary>Click on the hint below the keyboard row (disable Dynamic Lighting).</summary>
    BacklightHintClick,

    AllyControllerMode, AllyBacklight, AllyController, AutoTdp, FpsLimit, OverlayAlly,

    /// <summary>Click on the "Discharging: 12W" text (legacy LabelBattery_Click: toggle watts / percent).</summary>
    BatteryStatusClick,
    BatteryFull, BatteryReport, ChargeLabelClick,
    /// <summary>Pointer enters / leaves the "Battery Charge" text (legacy shows the battery health while hovered).</summary>
    ChargeHoverEnter, ChargeHoverLeave,
    /// <summary>Pointer enters / leaves the 100% button (legacy shows "Battery limit: full" in the charge text).</summary>
    BatteryFullHoverEnter, BatteryFullHoverLeave,
    /// <summary>Pointer entered a peripheral button (legacy re-reads the battery of a device that is not ready).</summary>
    PeripheralHover1, PeripheralHover2, PeripheralHover3,

    VersionClick, Updates, Donate, DonateAlreadyDonated, DonateNotInterested,
    Armoury, AmdOled, EnergySaver,
    Peripheral1, Peripheral2, Peripheral3,
    Quit,
}

/// <summary>Values changed by the user on the main window.</summary>
public enum MainValue
{
    BatteryLimit,
    Gamma,
    AuraMode,
    RearMode,
    MatrixBrightness,
    MatrixRunning,
    VisualMode,
    ColorTemp,
    Gamut,
    /// <summary>1 / 0: run on startup check box.</summary>
    RunOnStartup,
    /// <summary>Performance mode picked in the tray menu (value = AsusACPI performance mode id).</summary>
    PerformanceMode,
    /// <summary>1 / 0: tray menu "black and white icon".</summary>
    BwTrayIcon,
    /// <summary>1 / 0: tray menu "overlay only in games".</summary>
    OverlayGameOnly,
}

/// <summary>
/// The seam between the UI shell and the application logic. The shell owns layout, bindings and state mapping
/// (MainViewModel.Visualise*), the logic owner (wave 2: Settings.cs behaviour on top of GHelper.Core) implements this:
/// <list type="bullet">
/// <item><see cref="Execute"/>: user pressed something. Run the legacy handler.</item>
/// <item><see cref="SetValue"/>: user changed a slider / combo / check box.</item>
/// <item><see cref="RefreshArea"/>: Core asked (<c>IUiHost.Refresh(area)</c>) to repaint an area; re-read the model
/// and push it into the view model with its Visualise* methods. Called on the UI thread, coalesced.</item>
/// </list>
/// All calls happen on the UI thread.
/// </summary>
public interface IMainBackend
{
    void Execute(MainAction action);
    void SetValue(MainValue value, int newValue);
    void RefreshArea(string area, MainViewModel viewModel);
}

/// <summary>Does nothing; used until the real backend is attached and by tests.</summary>
public sealed class NullMainBackend : IMainBackend
{
    public static readonly NullMainBackend Instance = new();

    public void Execute(MainAction action) { }
    public void SetValue(MainValue value, int newValue) { }
    public void RefreshArea(string area, MainViewModel viewModel) { }
}
