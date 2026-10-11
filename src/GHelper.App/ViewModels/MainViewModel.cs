using System.Collections.ObjectModel;
using GHelper.App.Theme;
using GHelper.Properties;

namespace GHelper.App.ViewModels;

/// <summary>
/// State of the main window: exactly what the legacy SettingsForm.Visualise* methods rendered into its controls.
/// Plain INotifyPropertyChanged. Logic lives behind <see cref="IMainBackend"/>; this class only maps model state to
/// presentation (the Visualise* methods are ports of the legacy ones and are fed plain values, no Core calls).
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private IMainBackend _backend = NullMainBackend.Instance;
    private bool _loading;

    public MainViewModel()
    {
        // performance
        Silent = Btn(Strings.Silent, MainAction.PerformanceSilent, AppColors.Eco);
        Balanced = Btn(Strings.Balanced, MainAction.PerformanceBalanced, AppColors.Standard);
        Turbo = Btn(Strings.Turbo, MainAction.PerformanceTurbo, AppColors.Turbo);
        Fans = Btn(Strings.FansPower, MainAction.FansAndPower, AppColors.Custom);

        // gpu
        Eco = Btn(Strings.EcoMode, MainAction.GpuEco, AppColors.Eco);
        Standard = Btn(Strings.StandardMode, MainAction.GpuStandard, AppColors.Standard);
        Optimized = Btn(Strings.Optimized, MainAction.GpuOptimized, AppColors.Eco);
        Ultimate = Btn(Strings.UltimateMode, MainAction.GpuUltimate, AppColors.Turbo);
        Xgm = Btn("XG Mobile", MainAction.GpuXgm, AppColors.Turbo);
        Xgm.IsVisible = false;
        StopGpu = Btn(Strings.StopGPUApps, MainAction.GpuStopApps, AppColors.Eco);
        StopGpu.IsVisible = false;
        AutoTdp = Btn("AutoTDP", MainAction.AutoTdp, AppColors.Turbo);
        FpsLimit = Btn("FPS Limit OFF", MainAction.FpsLimit, AppColors.Standard);
        OverlayAlly = Btn(Strings.Overlay, MainAction.OverlayAlly, AppColors.Standard);

        // screen
        ScreenAuto = Btn(Strings.AutoMode, MainAction.ScreenAuto, AppColors.Gray);
        Hz60 = Btn("60Hz", MainAction.Screen60Hz, AppColors.Gray);
        Hz120 = Btn("120Hz + OD", MainAction.Screen120Hz, AppColors.Gray);
        MiniLed = Btn(Strings.Multizone, MainAction.ScreenMiniLed, AppColors.Turbo);
        Fhd = Btn("FHD", MainAction.ScreenFhd, AppColors.Gray);
        Fhd.IsVisible = false;
        HdrControl = Btn("HDR Color Control", MainAction.ScreenHdrControl, AppColors.Turbo);
        HdrControl.IsVisible = false;

        // visual / dimming
        InstallColors = Btn(Strings.DownloadColorProfiles, MainAction.InstallColorProfiles, AppColors.Gray);
        InstallColors.IsVisible = false;

        // matrix
        MatrixButton = Btn("Matrix", MainAction.MatrixOpen, AppColors.Gray);
        MatrixButton.Text = Strings.PictureGif;
        MatrixBrightnessItems = new ObservableCollection<ChoiceItem>
        {
            new(0, Strings.MatrixOff), new(1, Strings.MatrixDim), new(2, Strings.MatrixMedium), new(3, Strings.MatrixBright),
        };
        MatrixRunningItems = new ObservableCollection<ChoiceItem>
        {
            new(0, Strings.MatrixBanner), new(1, Strings.MatrixLogo), new(2, Strings.MatrixPicture),
            new(3, Strings.MatrixClock), new(4, Strings.MatrixAudio), new(5, Strings.MatrixText),
        };
        _selectedMatrixBrightness = MatrixBrightnessItems[0];
        _selectedMatrixRunning = MatrixRunningItems[0];

        // ally
        AllyMode = Btn(Strings.AutoMode, MainAction.AllyControllerMode, AppColors.Gray);
        AllyBacklight = Btn("100%", MainAction.AllyBacklight, AppColors.Gray);
        AllyController = Btn(Strings.Controller, MainAction.AllyController, AppColors.Gray);

        // keyboard
        FnLock = Flag("FN-Lock", MainAction.FnLock);
        Overlay = Flag(Strings.Overlay, MainAction.Overlay);
        Overlay.SecondaryCommand = new RelayCommand(() => _backend.Execute(MainAction.OverlayToggleOnly));
        KeyboardColor = Btn(Strings.Color, MainAction.KeyboardColor, AppColors.Gray);
        KeyboardExtra = Btn(Strings.Extra, MainAction.KeyboardExtra, AppColors.Gray);
        RearColor = Btn(Strings.Color, MainAction.RearColor, AppColors.Gray);

        // battery
        BatteryFull = Flag("100%", MainAction.BatteryFull);
        _batteryTitle = Strings.BatteryChargeLimit;
        _batteryAccessibleName = Strings.BatteryChargeLimit;

        // footer
        Donate = Btn(Strings.Donate, MainAction.Donate, AppColors.Turbo);
        Donate.SecondaryCommand = new RelayCommand(() => DonateMenuRequested?.Invoke());
        Updates = Btn(Strings.Updates, MainAction.Updates, AppColors.Gray);
        Updates.AccessibleName = Strings.BiosAndDriverUpdates;
        Quit = Btn(Strings.Quit, MainAction.Quit, AppColors.Gray);
        Quit.AccessibleName = Strings.Quit;

        EnergySaver = Btn("Energy Saver", MainAction.EnergySaver, AppColors.Eco);
        EnergySaver.IsVisible = false;
        EnergySaver.FillColor = AppColors.Eco;
        AmdOled = Btn("AMD OledSaver", MainAction.AmdOled, AppColors.Turbo);
        AmdOled.IsVisible = false;
        AmdOled.FillColor = AppColors.Turbo;
        Armoury = Btn("ArmouryCrate", MainAction.Armoury, AppColors.Turbo);
        Armoury.IsVisible = false;
        Armoury.FillColor = AppColors.Turbo;

        Peripherals = new[]
        {
            Btn("Mouse 1", MainAction.Peripheral1, AppColors.Gray),
            Btn("Mouse 2", MainAction.Peripheral2, AppColors.Gray),
            Btn("Mouse 3", MainAction.Peripheral3, AppColors.Gray),
        };
        foreach (var p in Peripherals) p.IsVisible = false;

        _performanceTitle = Strings.PerformanceMode;
        _gpuTitle = Strings.GPUMode;
        _screenTitle = Strings.LaptopScreen;
        _keyboardTitle = Strings.LaptopKeyboard;
        _matrixTitle = Strings.AnimeMatrix;
        _allyTitle = Strings.AllyController;
        _rearTitle = "Rear Light";
        _gammaTitle = Strings.VisualMode;
        _runOnStartupText = Strings.RunOnStartup;

        VisualModes = new ObservableCollection<ChoiceItem>();
        ColorTemps = new ObservableCollection<ChoiceItem>();
        Gamuts = new ObservableCollection<ChoiceItem>();
        AuraModes = new ObservableCollection<ChoiceItem>();
        RearModes = new ObservableCollection<ChoiceItem>();
    }

    private ButtonState Btn(string text, MainAction action, Avalonia.Media.Color accent)
    {
        return new ButtonState(text)
        {
            BorderColor = accent,
            Command = new RelayCommand(() => _backend.Execute(action)),
        };
    }

    private ButtonState Flag(string text, MainAction action) => new(text)
    {
        Command = new RelayCommand(() => _backend.Execute(action)),
    };

    /// <summary>Right click on the donate button: the view shows its "Already donated / Not interested" menu.</summary>
    public event Action? DonateMenuRequested;

    /// <summary>Attach the logic. Pass null to detach (back to a no-op backend).</summary>
    public IMainBackend Backend
    {
        get => _backend;
        set => _backend = value ?? NullMainBackend.Instance;
    }

    private void Push(MainValue value, int v)
    {
        if (!_loading) _backend.SetValue(value, v);
    }

    /// <summary>
    /// Run <paramref name="change"/> without the view model reporting the changed selections / slider values back to the
    /// backend (legacy sliderGammaIgnore and "SelectedValueChanged not attached yet"). For programmatic updates.
    /// </summary>
    public void WithoutPush(Action change)
    {
        bool old = _loading;
        _loading = true;
        try { change(); }
        finally { _loading = old; }
    }

    private static void Fill(ObservableCollection<ChoiceItem> target, IEnumerable<ChoiceItem> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    /// <summary>Replace the visual mode list and select <paramref name="selectedKey"/> (no backend call).</summary>
    public void SetVisualModes(IEnumerable<ChoiceItem> items, int selectedKey) => WithoutPush(() =>
    {
        Fill(VisualModes, items);
        SelectedVisualMode = VisualModes.FirstOrDefault(i => i.Key == selectedKey) ?? VisualModes.FirstOrDefault();
    });

    public void SetColorTemps(IEnumerable<ChoiceItem> items, int selectedKey) => WithoutPush(() =>
    {
        Fill(ColorTemps, items);
        SelectedColorTemp = ColorTemps.FirstOrDefault(i => i.Key == selectedKey) ?? ColorTemps.FirstOrDefault();
    });

    public void SetGamuts(IEnumerable<ChoiceItem> items, int selectedKey) => WithoutPush(() =>
    {
        Fill(Gamuts, items);
        SelectedGamut = Gamuts.FirstOrDefault(i => i.Key == selectedKey) ?? Gamuts.FirstOrDefault();
    });

    /// <summary>Select a gamut without applying it (legacy VisualiseGamut).</summary>
    public void SelectGamut(int key) => WithoutPush(() =>
        SelectedGamut = Gamuts.FirstOrDefault(i => i.Key == key) ?? SelectedGamut);

    public void SelectAuraMode(int key) => WithoutPush(() =>
        SelectedAuraMode = AuraModes.FirstOrDefault(i => i.Key == key) ?? SelectedAuraMode);

    public void SelectMatrixBrightness(int index) => WithoutPush(() =>
        SelectedMatrixBrightness = MatrixBrightnessItems[Math.Clamp(index, 0, MatrixBrightnessItems.Count - 1)]);

    // ============================== Window / header ===========================================================

    private string _windowTitle = "G-Helper";
    public string WindowTitle { get => _windowTitle; set => Set(ref _windowTitle, value); }

    // ============================== Performance ===============================================================

    public ButtonState Silent { get; }
    public ButtonState Balanced { get; }
    public ButtonState Turbo { get; }
    public ButtonState Fans { get; }

    private string _performanceTitle;
    public string PerformanceTitle { get => _performanceTitle; set => Set(ref _performanceTitle, value); }

    private string _cpuFanText = "";
    /// <summary>Right aligned sensor text of the performance header ("CPU: 45C  Fan: 2100RPM").</summary>
    public string CpuFanText { get => _cpuFanText; set => Set(ref _cpuFanText, value); }

    private int _currentMode = AsusACPI.PerformanceBalanced;
    public int CurrentMode { get => _currentMode; private set => Set(ref _currentMode, value); }

    /// <summary>Port of SettingsForm.VisualiseMode. <paramref name="baseMode"/> is Modes.GetBase(mode).</summary>
    public void VisualiseMode(int mode, int baseMode)
    {
        Silent.Activated = Balanced.Activated = Turbo.Activated = Fans.Activated = false;
        CurrentMode = mode;

        switch (mode)
        {
            case AsusACPI.PerformanceSilent:
                Silent.Activated = true;
                break;
            case AsusACPI.PerformanceTurbo:
                Turbo.Activated = true;
                break;
            case AsusACPI.PerformanceBalanced:
                Balanced.Activated = true;
                break;
            default:
                Fans.Activated = true;
                Fans.BorderColor = baseMode switch
                {
                    AsusACPI.PerformanceSilent => AppColors.Eco,
                    AsusACPI.PerformanceTurbo => AppColors.Turbo,
                    AsusACPI.PerformanceFullSpeed => AppColors.Orange,
                    _ => AppColors.Standard,
                };
                break;
        }
    }

    /// <summary>Port of SetModeLabel: the header shows the name of the active mode.</summary>
    public void SetModeLabel(string modeText)
    {
        PerformanceTitle = modeText;
        PerformanceAccessibleName = modeText;
    }

    private string? _performanceAccessibleName = Strings.PerformanceMode;
    public string? PerformanceAccessibleName { get => _performanceAccessibleName; set => Set(ref _performanceAccessibleName, value); }

    // ============================== GPU =======================================================================

    public ButtonState Eco { get; }
    public ButtonState Standard { get; }
    public ButtonState Optimized { get; }
    public ButtonState Ultimate { get; }
    public ButtonState Xgm { get; }
    public ButtonState StopGpu { get; }
    public ButtonState AutoTdp { get; }
    public ButtonState FpsLimit { get; }
    public ButtonState OverlayAlly { get; }

    private string _gpuTitle;
    public string GpuTitle { get => _gpuTitle; set => Set(ref _gpuTitle, value); }

    private string _gpuFanText = "";
    public string GpuFanText { get => _gpuFanText; set => Set(ref _gpuFanText, value); }

    private string _gpuTip = "";
    public string GpuTip { get => _gpuTip; set => Set(ref _gpuTip, value); }

    private string? _gpuAccessibleName = Strings.GPUMode;
    public string? GpuAccessibleName { get => _gpuAccessibleName; set => Set(ref _gpuAccessibleName, value); }

    private bool _gpuSectionVisible = true;
    public bool GpuSectionVisible { get => _gpuSectionVisible; set => Set(ref _gpuSectionVisible, value); }

    private bool _gpuButtonsVisible = true;
    /// <summary>tableGPU.Visible (hidden on the Ally, which only shows the AutoTDP / Overlay / FPS strip).</summary>
    public bool GpuButtonsVisible { get => _gpuButtonsVisible; set => Set(ref _gpuButtonsVisible, value); }

    private bool _gpuStripVisible;
    /// <summary>tableAMD.Visible: AutoTDP / Overlay / FPS limit (ROG Ally).</summary>
    public bool GpuStripVisible { get => _gpuStripVisible; set => Set(ref _gpuStripVisible, value); }

    private string? _gpuError;
    /// <summary>GPUModeControl.gpuError: when set the GPU icon turns into a warning triangle with this tooltip.</summary>
    public string? GpuError
    {
        get => _gpuError;
        set { if (Set(ref _gpuError, value)) Raise(nameof(HasGpuError)); }
    }
    public bool HasGpuError => !string.IsNullOrEmpty(_gpuError);

    private bool _isMuxGpu = true;
    private bool _isGpuSection = true;
    public bool IsMuxGpu { get => _isMuxGpu; private set => Set(ref _isMuxGpu, value); }
    public bool IsGpuSection { get => _isGpuSection; private set => Set(ref _isGpuSection, value); }

    private int _gpuMode = AsusACPI.GPUModeStandard;
    /// <summary>Last GPU mode shown (drives the tray icon).</summary>
    public int GpuMode { get => _gpuMode; private set => Set(ref _gpuMode, value); }

    /// <summary>Port of VisualiseGPUButtons.</summary>
    public void VisualiseGpuButtons(bool eco = true, bool ultimate = true)
    {
        IsMuxGpu = ultimate;

        if (!eco)
        {
            Eco.IsVisible = false;
            Optimized.IsVisible = false;
            StopGpu.IsVisible = true;
        }
        else
        {
            StopGpu.IsVisible = false;
        }

        if (!ultimate) Ultimate.IsVisible = false;
        UpdateColumns();
    }

    /// <summary>Port of HideGPUModes.</summary>
    public void HideGpuModes(bool gpuExists)
    {
        IsGpuSection = false;
        Eco.IsVisible = Standard.IsVisible = Ultimate.IsVisible = Optimized.IsVisible = false;
        StopGpu.IsVisible = true;
        GpuSectionVisible = gpuExists;
        UpdateColumns();
    }

    /// <summary>Port of LockGPUModes (while the GPU mode is being switched).</summary>
    public void LockGpuModes(string? text = null)
    {
        text ??= Strings.GPUMode + ": " + Strings.GPUChanging + " ...";
        Optimized.IsEnabled = Eco.IsEnabled = Standard.IsEnabled = Ultimate.IsEnabled = Xgm.IsEnabled = false;
        GpuTitle = text;
    }

    /// <summary>Port of VisualizeXGM(connected, activated, GPUMode).</summary>
    public void VisualiseXgm(bool connected, int activated, int gpuMode = -1, bool amdIgpu = false)
    {
        Xgm.IsEnabled = Xgm.IsVisible = connected;
        UpdateColumns();
        if (!connected) return;

        if (gpuMode != -1)
            Xgm.IsEnabled = amdIgpu || gpuMode != AsusACPI.GPUModeEco;

        Xgm.Activated = activated == 1;

        bool enable = activated != 1;
        Optimized.IsEnabled = Eco.IsEnabled = Standard.IsEnabled = Ultimate.IsEnabled = enable;
    }

    /// <summary>Port of VisualiseGPUMode (the labels / activated flags part; Ally / XGM handling stays with the caller).</summary>
    public void VisualiseGpuMode(int gpuMode, bool gpuAuto, bool alwaysUltimate = false, string? gpuError = null, bool ally = false)
    {
        GpuError = gpuError;
        GpuMode = gpuMode;

        if (ally)
        {
            GpuButtonsVisible = false;
            GpuTitle = "GPU";
            return;
        }

        Optimized.IsEnabled = Eco.IsEnabled = Standard.IsEnabled = Ultimate.IsEnabled = true;
        Eco.Activated = Standard.Activated = Ultimate.Activated = Optimized.Activated = false;

        switch (gpuMode)
        {
            case AsusACPI.GPUModeEco:
                Optimized.BorderColor = AppColors.Eco;
                Eco.Activated = !gpuAuto;
                Optimized.Activated = gpuAuto;
                GpuTitle = Strings.GPUMode + ": " + Strings.GPUModeEco;
                GpuAccessibleName = Strings.GPUMode + " - " + (gpuAuto ? Strings.Optimized : Strings.EcoMode);
                break;
            case AsusACPI.GPUModeUltimate:
                Ultimate.Activated = true;
                GpuTitle = Strings.GPUMode + ": " + Strings.GPUModeUltimate;
                GpuAccessibleName = Strings.GPUMode + " - " + Strings.UltimateMode;
                break;
            default:
                Optimized.BorderColor = AppColors.Standard;
                Standard.Activated = !gpuAuto;
                Optimized.Activated = gpuAuto;
                GpuTitle = Strings.GPUMode + ": " + (alwaysUltimate ? Strings.GPUModeUltimate : Strings.GPUModeStandard);
                GpuAccessibleName = Strings.GPUMode + " - " + (gpuAuto ? Strings.Optimized : Strings.StandardMode);
                break;
        }
    }

    /// <summary>Port of VisualiseAlly(true): AutoTDP / Overlay / FPS strip + controller panel, keyboard header hidden.</summary>
    public void VisualiseAlly(bool overlayActive)
    {
        AllySectionVisible = true;
        KeyboardHeaderVisible = false;
        OverlayAlly.Text = Strings.Overlay;
        OverlayAlly.Activated = overlayActive;
        GpuStripVisible = true;
    }

    public void VisualiseController(string text) => AllyMode.Text = text;

    public void VisualiseBacklight(int backlight) =>
        AllyBacklight.Text = Math.Round(backlight * 33.33) + "%";

    public void VisualiseFpsLimit(int limit) =>
        FpsLimit.Text = "FPS Limit " + ((limit > 0 && limit <= 120) ? limit.ToString() : "OFF");

    public void VisualiseAutoTdp(bool status) => AutoTdp.Activated = status;

    // ============================== Screen ====================================================================

    public ButtonState ScreenAuto { get; }
    public ButtonState Hz60 { get; }
    public ButtonState Hz120 { get; }
    public ButtonState MiniLed { get; }
    public ButtonState Fhd { get; }
    public ButtonState HdrControl { get; }

    private string _screenTitle;
    public string ScreenTitle { get => _screenTitle; set => Set(ref _screenTitle, value); }

    private string? _screenAccessibleName = Strings.LaptopScreen;
    public string? ScreenAccessibleName { get => _screenAccessibleName; set => Set(ref _screenAccessibleName, value); }

    private string _midFanText = "";
    public string MidFanText { get => _midFanText; set => Set(ref _midFanText, value); }

    private string _screenTip = "";
    public string ScreenTip { get => _screenTip; set => Set(ref _screenTip, value); }

    private bool _screenSectionVisible = true;
    public bool ScreenSectionVisible { get => _screenSectionVisible; set => Set(ref _screenSectionVisible, value); }

    private bool _screenButtonsVisible = true;
    public bool ScreenButtonsVisible { get => _screenButtonsVisible; set => Set(ref _screenButtonsVisible, value); }

    private int _gpuColumns = 4;
    private int _screenColumns = 4;
    /// <summary>Number of visible buttons in the GPU row (the legacy table re-flowed its columns).</summary>
    public int GpuColumns { get => _gpuColumns; private set => Set(ref _gpuColumns, value); }
    public int ScreenColumns { get => _screenColumns; private set => Set(ref _screenColumns, value); }

    private void UpdateColumns()
    {
        GpuColumns = Math.Max(1, new[] { Eco, Standard, Optimized, Ultimate, Xgm, StopGpu }.Count(b => b.IsVisible));
        ScreenColumns = Math.Max(1, new[] { ScreenAuto, Hz60, Hz120, MiniLed, Fhd, HdrControl }.Count(b => b.IsVisible));
    }

    /// <summary>Port of SettingsForm.VisualiseScreen.</summary>
    public void VisualiseScreen(bool screenEnabled, bool screenAuto, int frequency, int maxFrequency, int overdrive,
        bool overdriveSetting, int miniled1, int miniled2, bool hdr, bool acm, int fhd, int hdrControl,
        int minRate = 60, bool noGpu = false)
    {
        bool advancedColor = hdr || acm;

        ScreenAuto.IsEnabled = Hz60.IsEnabled = Hz120.IsEnabled = MiniLed.IsEnabled = screenEnabled;

        ScreenTitle = screenEnabled
            ? Strings.LaptopScreen + ": " + frequency + "Hz" + (overdrive == 1 ? " + " + Strings.Overdrive : "")
            : Strings.LaptopScreen + ": " + Strings.TurnedOff;
        ScreenAccessibleName = ScreenTitle;

        Hz60.Activated = Hz120.Activated = ScreenAuto.Activated = false;

        if (screenAuto) ScreenAuto.Activated = true;
        else if (frequency == minRate) Hz60.Activated = true;
        else if (frequency > minRate) Hz120.Activated = true;

        Hz60.Text = minRate + "Hz";

        if (maxFrequency > minRate)
        {
            Hz120.Text = maxFrequency + "Hz" + (overdriveSetting ? " + OD" : "");
            ScreenSectionVisible = true;
            ScreenButtonsVisible = true;
        }
        else if (maxFrequency > 0)
        {
            ScreenButtonsVisible = false;
            ScreenSectionVisible = noGpu;
        }

        if (fhd >= 0)
        {
            Fhd.IsVisible = true;
            Fhd.Text = fhd > 0 ? "FHD" : "UHD";
        }

        bool hdrControlVisible = hdr && hdrControl >= 0;

        if (miniled1 >= 0)
        {
            MiniLed.IsVisible = !hdrControlVisible;
            MiniLed.IsEnabled = !hdr;
            MiniLed.Activated = miniled1 == 1 || hdr;
        }
        else if (miniled2 >= 0)
        {
            MiniLed.IsVisible = !hdrControlVisible;
            MiniLed.IsEnabled = !hdr;
            if (hdr) miniled2 = 1; // HDR is shown as Multizone Strong

            switch (miniled2)
            {
                case 0:
                    MiniLed.Text = Strings.Multizone;
                    MiniLed.BorderColor = AppColors.Standard;
                    MiniLed.Activated = true;
                    break;
                case 1:
                    MiniLed.Text = Strings.MultizoneStrong;
                    MiniLed.BorderColor = AppColors.Turbo;
                    MiniLed.Activated = true;
                    break;
                case 2:
                    MiniLed.Text = Strings.OneZone;
                    MiniLed.BorderColor = AppColors.Standard;
                    MiniLed.Activated = false;
                    break;
            }
        }
        else
        {
            MiniLed.IsVisible = false;
        }

        if (hdrControlVisible)
        {
            HdrControl.IsVisible = true;
            HdrControl.Activated = hdrControl > 0;
            HdrControl.BorderColor = AppColors.Turbo;
        }
        else
        {
            HdrControl.IsVisible = false;
        }

        if (advancedColor) VisualHint = Strings.VisualModesHDR;
        if (!screenEnabled) VisualHint = Strings.VisualModesScreen;
        VisualHintVisible = !screenEnabled || advancedColor;

        UpdateColumns();
    }

    // ============================== Flicker-free dimming / visual modes =======================================

    public ButtonState InstallColors { get; }

    private bool _gammaSectionVisible;
    public bool GammaSectionVisible { get => _gammaSectionVisible; set => Set(ref _gammaSectionVisible, value); }

    private string _gammaTitle;
    public string GammaTitle { get => _gammaTitle; set => Set(ref _gammaTitle, value); }

    private bool _gammaSliderVisible;
    public bool GammaSliderVisible { get => _gammaSliderVisible; set => Set(ref _gammaSliderVisible, value); }

    private int _gamma = 100;
    public int Gamma
    {
        get => _gamma;
        set
        {
            if (Set(ref _gamma, value))
            {
                Raise(nameof(GammaText));
                Push(MainValue.Gamma, value);
            }
        }
    }
    public string GammaText => _gamma + "%";

    /// <summary>Port of VisualiseBrightness (does not call back into the backend).</summary>
    public void VisualiseBrightness(int value)
    {
        _loading = true;
        try { Gamma = value; }
        finally { _loading = false; }
    }

    private bool _visualTableVisible;
    public bool VisualTableVisible { get => _visualTableVisible; set => Set(ref _visualTableVisible, value); }

    private bool _visualCombosEnabled = true;
    /// <summary>VisualiseDisabled: gamut / colour temperature are disabled while the visual mode is "Disabled".</summary>
    public bool VisualCombosEnabled { get => _visualCombosEnabled; set => Set(ref _visualCombosEnabled, value); }

    private string _visualHint = Strings.VisualModesHDR;
    public string VisualHint { get => _visualHint; set => Set(ref _visualHint, value); }

    private bool _visualHintVisible;
    public bool VisualHintVisible { get => _visualHintVisible; set => Set(ref _visualHintVisible, value); }

    public ObservableCollection<ChoiceItem> VisualModes { get; }
    public ObservableCollection<ChoiceItem> ColorTemps { get; }
    public ObservableCollection<ChoiceItem> Gamuts { get; }

    private ChoiceItem? _selectedVisualMode;
    public ChoiceItem? SelectedVisualMode
    {
        get => _selectedVisualMode;
        set { if (Set(ref _selectedVisualMode, value) && value is not null) Push(MainValue.VisualMode, value.Key); }
    }

    private ChoiceItem? _selectedColorTemp;
    public ChoiceItem? SelectedColorTemp
    {
        get => _selectedColorTemp;
        set { if (Set(ref _selectedColorTemp, value) && value is not null) Push(MainValue.ColorTemp, value.Key); }
    }

    private ChoiceItem? _selectedGamut;
    public ChoiceItem? SelectedGamut
    {
        get => _selectedGamut;
        set { if (Set(ref _selectedGamut, value) && value is not null) Push(MainValue.Gamut, value.Key); }
    }

    private bool _visualModeVisible, _colorTempVisible, _gamutVisible;
    public bool VisualModeVisible { get => _visualModeVisible; set => Set(ref _visualModeVisible, value); }
    public bool ColorTempVisible { get => _colorTempVisible; set => Set(ref _colorTempVisible, value); }
    public bool GamutVisible { get => _gamutVisible; set => Set(ref _gamutVisible, value); }

    // ============================== Matrix ====================================================================

    public ButtonState MatrixButton { get; }
    public ObservableCollection<ChoiceItem> MatrixBrightnessItems { get; }
    public ObservableCollection<ChoiceItem> MatrixRunningItems { get; }

    private bool _matrixSectionVisible = true;
    public bool MatrixSectionVisible { get => _matrixSectionVisible; set => Set(ref _matrixSectionVisible, value); }

    private string _matrixTitle;
    public string MatrixTitle { get => _matrixTitle; set => Set(ref _matrixTitle, value); }

    private ChoiceItem _selectedMatrixBrightness;
    public ChoiceItem SelectedMatrixBrightness
    {
        get => _selectedMatrixBrightness;
        set { if (value is not null && Set(ref _selectedMatrixBrightness, value)) Push(MainValue.MatrixBrightness, value.Key); }
    }

    private ChoiceItem _selectedMatrixRunning;
    public ChoiceItem SelectedMatrixRunning
    {
        get => _selectedMatrixRunning;
        set { if (value is not null && Set(ref _selectedMatrixRunning, value)) Push(MainValue.MatrixRunning, value.Key); }
    }

    /// <summary>Slash devices replace the running modes list and title (legacy InitMatrix).</summary>
    public void InitMatrix(bool valid, bool isSlash, IEnumerable<ChoiceItem>? slashModes, int brightness, int running)
    {
        _loading = true;
        try
        {
            MatrixSectionVisible = valid;
            if (!valid) return;

            if (isSlash)
            {
                MatrixTitle = "Slash Lighting";
                MatrixButton.Text = "Slash";
                MatrixRunningItems.Clear();
                foreach (var m in slashModes ?? Array.Empty<ChoiceItem>()) MatrixRunningItems.Add(m);
            }

            SelectedMatrixBrightness = MatrixBrightnessItems[Math.Clamp(brightness, 0, MatrixBrightnessItems.Count - 1)];
            if (MatrixRunningItems.Count > 0)
                SelectedMatrixRunning = MatrixRunningItems[Math.Clamp(running, 0, MatrixRunningItems.Count - 1)];
        }
        finally { _loading = false; }
    }

    /// <summary>Port of VisualiseMatrixRunning.</summary>
    public void VisualiseMatrixRunning(int mode)
    {
        _loading = true;
        try
        {
            if (mode >= 0 && mode < MatrixRunningItems.Count) SelectedMatrixRunning = MatrixRunningItems[mode];
        }
        finally { _loading = false; }
    }

    // ============================== Ally ======================================================================

    public ButtonState AllyMode { get; }
    public ButtonState AllyBacklight { get; }
    public ButtonState AllyController { get; }

    private bool _allySectionVisible;
    public bool AllySectionVisible { get => _allySectionVisible; set => Set(ref _allySectionVisible, value); }

    private string _allyTitle;
    public string AllyTitle { get => _allyTitle; set => Set(ref _allyTitle, value); }

    // ============================== Keyboard ==================================================================

    public ButtonState FnLock { get; }
    public ButtonState Overlay { get; }
    public ButtonState KeyboardColor { get; }
    public ButtonState KeyboardExtra { get; }
    public ObservableCollection<ChoiceItem> AuraModes { get; }

    private string _keyboardTitle;
    public string KeyboardTitle { get => _keyboardTitle; set => Set(ref _keyboardTitle, value); }

    private bool _keyboardHeaderVisible = true;
    public bool KeyboardHeaderVisible { get => _keyboardHeaderVisible; set => Set(ref _keyboardHeaderVisible, value); }

    private ChoiceItem? _selectedAuraMode;
    public ChoiceItem? SelectedAuraMode
    {
        get => _selectedAuraMode;
        set { if (Set(ref _selectedAuraMode, value) && value is not null) Push(MainValue.AuraMode, value.Key); }
    }

    public string AuraAccessibleName => Strings.LaptopBacklight;

    private bool _auraModeVisible = true;
    public bool AuraModeVisible { get => _auraModeVisible; set => Set(ref _auraModeVisible, value); }

    private bool _keyboardColorVisible = true;
    public bool KeyboardColorVisible { get => _keyboardColorVisible; set => Set(ref _keyboardColorVisible, value); }

    private Avalonia.Media.Color? _keyboardSwatch;
    public Avalonia.Media.Color? KeyboardSwatch { get => _keyboardSwatch; set => Set(ref _keyboardSwatch, value); }

    private Avalonia.Media.Color? _keyboardSwatch2;
    public Avalonia.Media.Color? KeyboardSwatch2 { get => _keyboardSwatch2; set => Set(ref _keyboardSwatch2, value); }

    private string _backlightHint = "";
    public string BacklightHint { get => _backlightHint; set => Set(ref _backlightHint, value); }

    /// <summary>Port of VisualiseOverlay.</summary>
    public void VisualiseOverlay(bool enabled)
    {
        Overlay.FillColor = enabled ? AppColors.Eco : null;
        OverlayAlly.Activated = enabled;
        OverlayEnabled = enabled;
    }

    /// <summary>Port of VisualiseFnLock.</summary>
    public void VisualiseFnLock(bool on)
    {
        FnLock.FillColor = on ? AppColors.Standard : null;
        FnLock.AccessibleName = on ? "Fn-Lock on" : "Fn-Lock off";
    }

    /// <summary>Port of _VisualiseAura + the mode/colour visibility of InitAura.</summary>
    public void VisualiseAura(Avalonia.Media.Color color1, Avalonia.Media.Color? color2, string backlightHint)
    {
        KeyboardSwatch = color1;
        KeyboardSwatch2 = color2;
        BacklightHint = backlightHint;
    }

    public void SetKeyboardTitle(string text, bool auraSync) => KeyboardTitle = text + (auraSync ? " +" : "");

    public void SetAuraModes(IEnumerable<ChoiceItem> modes, int selectedKey)
    {
        _loading = true;
        try
        {
            AuraModes.Clear();
            foreach (var m in modes) AuraModes.Add(m);
            SelectedAuraMode = AuraModes.FirstOrDefault(m => m.Key == selectedKey) ?? AuraModes.FirstOrDefault();
        }
        finally { _loading = false; }
    }

    // ============================== Rear light ================================================================

    public ButtonState RearColor { get; }
    public ObservableCollection<ChoiceItem> RearModes { get; }

    private string _rearTitle;
    public string RearTitle { get => _rearTitle; set => Set(ref _rearTitle, value); }

    private bool _rearSectionVisible;
    public bool RearSectionVisible { get => _rearSectionVisible; set => Set(ref _rearSectionVisible, value); }

    private ChoiceItem? _selectedRearMode;
    public ChoiceItem? SelectedRearMode
    {
        get => _selectedRearMode;
        set { if (Set(ref _selectedRearMode, value) && value is not null) Push(MainValue.RearMode, value.Key); }
    }

    private Avalonia.Media.Color? _rearSwatch;
    public Avalonia.Media.Color? RearSwatch { get => _rearSwatch; set => Set(ref _rearSwatch, value); }

    public void SetRearModes(IEnumerable<ChoiceItem> modes, int selectedKey)
    {
        _loading = true;
        try
        {
            RearModes.Clear();
            foreach (var m in modes) RearModes.Add(m);
            SelectedRearMode = RearModes.FirstOrDefault(m => m.Key == selectedKey) ?? RearModes.FirstOrDefault();
        }
        finally { _loading = false; }
    }

    // ============================== Peripherals ===============================================================

    public ButtonState[] Peripherals { get; }

    public ButtonState Peripheral1 => Peripherals[0];
    public ButtonState Peripheral2 => Peripherals[1];
    public ButtonState Peripheral3 => Peripherals[2];

    private bool _peripheralsVisible;
    public bool PeripheralsVisible { get => _peripheralsVisible; set => Set(ref _peripheralsVisible, value); }

    // ============================== Battery ===================================================================

    public ButtonState BatteryFull { get; }

    private string _batteryTitle;
    public string BatteryTitle { get => _batteryTitle; set => Set(ref _batteryTitle, value); }

    private string? _batteryAccessibleName;
    public string? BatteryAccessibleName { get => _batteryAccessibleName; set => Set(ref _batteryAccessibleName, value); }

    private int _batteryLimit = 100;
    public int BatteryLimit
    {
        get => _batteryLimit;
        set
        {
            if (Set(ref _batteryLimit, value))
            {
                if (!_loading) VisualiseBatteryTitle(value);
                Push(MainValue.BatteryLimit, value);
            }
        }
    }

    private string _batteryStatus = "";
    /// <summary>"Discharging: 12.3W" right of the battery title.</summary>
    public string BatteryStatus { get => _batteryStatus; set => Set(ref _batteryStatus, value); }

    private IList<int> _batterySupportedValues = new List<int>();
    /// <summary>AppConfig.IsChargeLimit6080: { 60, 65, 70, 75, 80, 100 }. Empty: every 5.</summary>
    public IList<int> BatterySupportedValues { get => _batterySupportedValues; set => Set(ref _batterySupportedValues, value); }

    public void VisualiseBatteryTitle(int limit) =>
        BatteryTitle = Strings.BatteryChargeLimit + ": " + limit + "%";

    /// <summary>Port of VisualiseBattery.</summary>
    public void VisualiseBattery(int limit, bool chargeFull)
    {
        _loading = true;
        try
        {
            BatteryLimit = limit;
        }
        finally { _loading = false; }
        VisualiseBatteryTitle(limit);
        BatteryAccessibleName = Strings.BatteryChargeLimit + ": " + limit + "%";
        VisualiseBatteryFull(chargeFull);
    }

    /// <summary>Port of VisualiseBatteryFull.</summary>
    public void VisualiseBatteryFull(bool chargeFull)
    {
        BatteryFull.FillColor = chargeFull ? AppColors.Standard : null;
        BatteryFull.AccessibleName = Strings.BatteryChargeLimit + (chargeFull ? "100% on" : "100% off");
    }

    // ============================== Startup / version / footer ================================================

    private string _runOnStartupText;
    public string RunOnStartupText { get => _runOnStartupText; set => Set(ref _runOnStartupText, value); }

    private bool _runOnStartup;
    public bool RunOnStartup
    {
        get => _runOnStartup;
        set { if (Set(ref _runOnStartup, value)) Push(MainValue.RunOnStartup, value ? 1 : 0); }
    }

    public void VisualiseRunOnStartup(bool value)
    {
        _loading = true;
        try { RunOnStartup = value; }
        finally { _loading = false; }
    }

    private string _chargeText = "";
    /// <summary>"Battery Charge: 80%" / "Battery Health: 91%" at the right of the startup row.</summary>
    public string ChargeText { get => _chargeText; set => Set(ref _chargeText, value); }

    private string _versionText = "v." + typeof(MainViewModel).Assembly.GetName().Version?.ToString(3);
    public string VersionText { get => _versionText; set => Set(ref _versionText, value); }

    private bool _versionIsUpdate;
    /// <summary>The version label turns red when an update is available (SetVersionLabel(update: true)).</summary>
    public bool VersionIsUpdate { get => _versionIsUpdate; set => Set(ref _versionIsUpdate, value); }

    public void SetVersionLabel(string label, bool update = false)
    {
        VersionText = label;
        if (update) VersionIsUpdate = true;
    }

    public ButtonState EnergySaver { get; }
    public ButtonState AmdOled { get; }
    public ButtonState Armoury { get; }
    public ButtonState Donate { get; }
    public ButtonState Updates { get; }
    public ButtonState Quit { get; }

    // ============================== Sensors ===================================================================

    /// <summary>Port of the BeginInvoke block of RefreshSensors.</summary>
    public void VisualiseSensors(string cpu, string gpu, string? mid, string battery, string? charge)
    {
        CpuFanText = cpu;
        GpuFanText = gpu;
        if (mid is not null) MidFanText = mid;
        BatteryStatus = battery;
        if (charge is not null) ChargeText = charge;
    }

    // ============================== Tray ======================================================================

    private IReadOnlyList<ChoiceItem> _trayModes = Array.Empty<ChoiceItem>();
    /// <summary>Entries of the tray "Performance Mode" section (Modes.GetDictonary()).</summary>
    public IReadOnlyList<ChoiceItem> TrayModes { get => _trayModes; set => Set(ref _trayModes, value); }

    private bool _bwTrayIcon;
    public bool BwTrayIcon { get => _bwTrayIcon; set => Set(ref _bwTrayIcon, value); }

    private bool _overlayEnabled;
    public bool OverlayEnabled { get => _overlayEnabled; private set => Set(ref _overlayEnabled, value); }

    private bool _overlayGameOnly;
    public bool OverlayGameOnly { get => _overlayGameOnly; set => Set(ref _overlayGameOnly, value); }

    private string _trayToolTip = "G-Helper";
    /// <summary>Tooltip of the tray icon (legacy trayIcon.Text: CPU / GPU temps + battery rate).</summary>
    public string TrayToolTip { get => _trayToolTip; set => Set(ref _trayToolTip, value); }

    // ============================== Design time sample ========================================================

    /// <summary>Representative data for the designer / the headless layout test.</summary>
    public static MainViewModel CreateSample(bool ally = false)
    {
        var vm = new MainViewModel();
        vm._loading = true;

        vm.WindowTitle = "G-Helper - ROG Zephyrus G14 GA402RJ";
        vm.VisualiseMode(AsusACPI.PerformanceBalanced, AsusACPI.PerformanceBalanced);
        vm.SetModeLabel(Strings.PerformanceMode + ": " + Strings.Balanced);
        vm.CpuFanText = "CPU: 47°C  " + Strings.FanSpeed + ": 2100RPM";
        vm.GpuFanText = "GPU: 41°C  " + Strings.FanSpeed + ": 2300RPM";

        vm.VisualiseGpuButtons(eco: true, ultimate: true);
        vm.VisualiseGpuMode(AsusACPI.GPUModeStandard, gpuAuto: false);
        vm.GpuTip = Strings.StandardGPUTooltip;

        vm.VisualiseScreen(true, false, 120, 120, 1, true, -1, -1, false, false, -1, -1);
        vm.ScreenTip = "";

        vm.GammaSectionVisible = true;
        vm.GammaSliderVisible = true;
        vm.GammaTitle = Strings.FlickerFreeDimming + " / " + Strings.VisualMode;
        vm.VisualiseBrightness(80);
        vm.VisualTableVisible = true;
        vm.VisualModeVisible = vm.ColorTempVisible = vm.GamutVisible = true;
        vm.VisualModes.Add(new ChoiceItem(0, "Default"));
        vm.VisualModes.Add(new ChoiceItem(1, "Vivid"));
        vm.VisualModes.Add(new ChoiceItem(2, "Eye Care"));
        vm.ColorTemps.Add(new ChoiceItem(50, "Normal"));
        vm.ColorTemps.Add(new ChoiceItem(0, "Cool"));
        vm.Gamuts.Add(new ChoiceItem(0, "Native"));
        vm.Gamuts.Add(new ChoiceItem(1, "sRGB"));
        vm._selectedVisualMode = vm.VisualModes[0];
        vm._selectedColorTemp = vm.ColorTemps[0];
        vm._selectedGamut = vm.Gamuts[0];

        vm.MatrixSectionVisible = true;
        vm._selectedMatrixBrightness = vm.MatrixBrightnessItems[2];
        vm._selectedMatrixRunning = vm.MatrixRunningItems[1];

        vm.AuraModes.Add(new ChoiceItem(0, Strings.AuraStatic));
        vm.AuraModes.Add(new ChoiceItem(1, Strings.AuraBreathe));
        vm.AuraModes.Add(new ChoiceItem(2, Strings.AuraRainbow));
        vm.AuraModes.Add(new ChoiceItem(3, Strings.AuraStrobe));
        vm._selectedAuraMode = vm.AuraModes[0];
        vm.KeyboardSwatch = Avalonia.Media.Color.FromRgb(255, 0, 0);
        vm.KeyboardSwatch2 = Avalonia.Media.Color.FromRgb(0, 120, 255);
        vm.VisualiseFnLock(true);
        vm.VisualiseOverlay(false);

        vm.VisualiseBattery(80, chargeFull: false);
        vm.BatteryStatus = Strings.Discharging + ": 11.4W";
        vm.ChargeText = Strings.BatteryCharge + ": 82%";
        vm._runOnStartup = true;
        vm.VersionText = "v.0.286.0";

        vm.Donate.Badge = 2;
        vm.Armoury.IsVisible = false;

        if (ally)
        {
            vm.VisualiseAlly(overlayActive: true);
            vm.VisualiseGpuMode(AsusACPI.GPUModeStandard, false, ally: true);
            vm.VisualiseController("Gamepad");
            vm.VisualiseBacklight(2);
            vm.VisualiseFpsLimit(60);
            vm.VisualiseAutoTdp(true);
        }

        vm._loading = false;
        vm.TrayModes = new[]
        {
            new ChoiceItem(AsusACPI.PerformanceSilent, Strings.Silent),
            new ChoiceItem(AsusACPI.PerformanceBalanced, Strings.Balanced),
            new ChoiceItem(AsusACPI.PerformanceTurbo, Strings.Turbo),
        };
        return vm;
    }
}

/// <summary>Sample view models for the XAML previewer.</summary>
public static class DesignData
{
    public static MainViewModel Main { get; } = MainViewModel.CreateSample();

    public static MainViewModel Ally { get; } = MainViewModel.CreateSample(ally: true);
}
