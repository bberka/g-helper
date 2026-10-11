using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using GHelper.App.ViewModels;
using GHelper.App.Views.Peripherals;
using GHelper.Peripherals;
using GHelper.Peripherals.Mouse;
using GHelper.Properties;

namespace GHelper.App.Views.Mouse;

/// <summary>Captions of the mouse window (legacy: assigned in the AsusMouseSettings constructor).</summary>
public sealed class MouseTexts
{
    public string PollingRate => Strings.PollingRate;
    public string Lighting => Strings.Lighting;
    public string LightingMode => Strings.AuraLightingMode;
    public string Energy => Strings.EnergySettings;
    public string Performance => Strings.MousePerformance;
    public string RandomColor => Strings.AuraRandomColor;
    public string SyncAura => Strings.MouseSyncWithAura;
    public string LowBatteryWarning => Strings.MouseLowBatteryWarning;
    public string AutoPowerOff => Strings.MouseAutoPowerOff;
    public string Synchronize => Strings.MouseSynchronize;
    public string AngleSnapping => Strings.MouseAngleSnapping;
    public string LiftOffDistance => Strings.MouseLiftOffDistance;
    public string Profile => Strings.Profile;
    public string ButtonDebounce => Strings.MouseButtonResponse;
    public string Acceleration => Strings.Acceleration;
    public string Deceleration => Strings.Deceleration;
    public string MotionSync => Strings.MouseMotionSync;
    public string ZoneMode => Strings.MouseZoneMode;
    public string Export => Strings.Export;
    public string Import => Strings.Import;
    public string AnimationSpeed => Strings.AnimationSpeed;
    public string AnimationDirection => Strings.AnimationDirection;
    public string KeyBindings => Strings.KeyBindings;
    public string Color => Strings.Color;
    public string Reset => Strings.Reset;
    public string Buttons => "Buttons";
    public string SpeedShift => "SpeedShift";
    public string Dpi => "DPI";
    public string ZoneAll => Strings.AuraZoneAll;
    public string ZoneLogo => Strings.AuraZoneLogo;
    public string ZoneScroll => Strings.AuraZoneScroll;
    public string ZoneUnderglow => Strings.AuraZoneUnderglow;
    public string ZoneDock => Strings.AuraZoneDock;
}

/// <summary>One row of the button remapping list: a physical button and the action bound to it.</summary>
public sealed class ButtonBindingRow : ObservableObject
{
    private BindingEntry? _selected;
    private bool _locked;

    public ButtonBindingRow(int slot, string name, IReadOnlyList<BindingEntry> entries)
    {
        Slot = slot;
        Name = name;
        Entries = entries;
    }

    public int Slot { get; }
    public string Name { get; }
    public IReadOnlyList<BindingEntry> Entries { get; }

    public BindingEntry? Selected { get => _selected; set => Set(ref _selected, value); }

    /// <summary>On the Shift layer a button that is bound to "Speed Shift" itself cannot be remapped.</summary>
    public bool Locked
    {
        get => _locked;
        set
        {
            if (Set(ref _locked, value)) Raise(nameof(IsEnabled));
        }
    }

    public bool IsEnabled => !_locked;
}

/// <summary>
/// State and device logic of the mouse window (port of app/AsusMouseSettings.cs). All visibility flags come from the
/// model's Has*/Supports* capability methods; every write goes through the same AsusMouse call as the legacy form.
/// Sliders only write on commit (legacy MouseUp/KeyUp): the view calls the Commit* methods, plain value changes only
/// update the captions.
/// </summary>
public sealed class MouseViewModel : PeripheralViewModelBase
{
    private static readonly PollingRate[] ZoneModePollingRates =
    {
        PollingRate.PR1000Hz, PollingRate.PR2000Hz, PollingRate.PR4000Hz, PollingRate.PR8000Hz,
    };

    private static readonly Dictionary<LightingMode, Func<string>> LightingModeNames = new()
    {
        { LightingMode.Static, () => Strings.AuraStatic },
        { LightingMode.Breathing, () => Strings.AuraBreathe },
        { LightingMode.ColorCycle, () => Strings.AuraColorCycle },
        { LightingMode.Rainbow, () => Strings.AuraRainbow },
        { LightingMode.React, () => Strings.AuraReact },
        { LightingMode.Comet, () => Strings.AuraComet },
        { LightingMode.BatteryState, () => Strings.AuraBatteryState },
        { LightingMode.Off, () => Strings.MatrixOff },
    };

    private static readonly Color[] FallbackDpiColors = { Colors.Red, Colors.Purple, Colors.Blue, Colors.Green };

    private readonly AsusMouse _mouse;
    private readonly List<LightingMode> _supportedLightingModes = new();
    private readonly List<BindingEntry> _bindingEntries = new();
    private LightingZone _visibleZone = LightingZone.All;
    private bool _started;
    private bool _updatingBindings;
    private bool _shiftLayer;

    // current values of the controls
    private int _selectedProfile;
    private int _dpi;
    private Color _dpiColor = Colors.Red;
    private int _pollingRateIndex;
    private bool _angleSnapping;
    private int _angleAdjustment;
    private bool _motionSync;
    private bool _motionSyncEnabled = true;
    private bool _zoneMode;
    private int _zoneModeDpi;
    private int _zoneModePollingRateIndex = 2;
    private int _debounce;
    private int _liftOffIndex;
    private int _acceleration;
    private int _deceleration;
    private int _brightness;
    private int _selectedLightingModeIndex;
    private Color _lightingColor = Colors.Red;
    private bool _randomColor;
    private bool _syncAura;
    private int _animationSpeedIndex;
    private int _animationDirectionIndex;
    private int _autoPowerOffIndex;
    private int _lowBatteryWarning;
    private bool _bindingsVisible;

    // capability driven visibility
    private bool _randomColorVisible;
    private bool _colorVisible;
    private bool _animationSpeedVisible;
    private bool _animationDirectionVisible;
    private bool _zoneModeControlsEnabled;
    private bool _normalControlsEnabled = true;

    public MouseViewModel(AsusMouse mouse, Action<Action>? post = null, Action<Action>? background = null)
        : base(post, background)
    {
        _mouse = mouse;

        BeginLoading();
        try
        {
            InitCapabilities();
            RefreshMouseData();
        }
        finally
        {
            EndLoading();
        }
    }

    public AsusMouse Mouse => _mouse;

    public MouseTexts Texts { get; } = new();

    public override string Title => _mouse.GetDisplayName();

    // ---------------------------------------------------------------- capabilities (set once)

    public bool ProfilesVisible { get; private set; }
    public bool BottomButtonsVisible { get; private set; }

    public bool PerformanceVisible { get; private set; }
    public bool DpiColorVisible { get; private set; }
    public int DpiMin { get; private set; }
    public int DpiMax { get; private set; }
    public int DpiStep { get; private set; }
    public string DpiMinText { get; private set; } = "";
    public string DpiMaxText { get; private set; } = "";

    public bool PollingRateVisible { get; private set; }
    public bool AngleSnappingPanelVisible { get; private set; }
    public bool AngleSnappingVisible { get; private set; }
    public bool AngleTuningVisible { get; private set; }
    public int AngleMin { get; private set; }
    public int AngleMax { get; private set; } = 30;
    public int AngleStep { get; private set; } = 1;
    public bool MotionSyncVisible { get; private set; }
    public bool ZoneModeVisible { get; private set; }
    public bool DebounceVisible { get; private set; }
    public int DebounceMin { get; private set; }
    public int DebounceMax { get; private set; } = 7;
    public bool LiftOffVisible { get; private set; }
    public bool AccelerationVisible { get; private set; }
    public int AccelerationMax { get; private set; } = 10;
    public bool DecelerationVisible { get; private set; }
    public int DecelerationMax { get; private set; } = 10;

    public bool LightingVisible { get; private set; }
    public bool ZonesVisible { get; private set; }
    public bool ZoneLogoVisible { get; private set; }
    public bool ZoneScrollVisible { get; private set; }
    public bool ZoneUnderglowVisible { get; private set; }
    public bool ZoneDockVisible { get; private set; }
    public int BrightnessMax { get; private set; } = 100;

    public bool EnergyVisible { get; private set; }
    public bool AutoPowerOffVisible { get; private set; }
    public bool LowBatteryWarningVisible { get; private set; }
    public int LowBatteryWarningMax { get; private set; } = 100;
    public int LowBatteryWarningStep { get; private set; } = 1;

    public bool HasButtonBindings { get; private set; }
    public bool HasSpeedShift { get; private set; }

    // ---------------------------------------------------------------- lists

    public ObservableCollection<string> ProfileItems { get; } = new();
    public ObservableCollection<string> PollingRateItems { get; } = new();
    public ObservableCollection<string> ZoneModePollingRateItems { get; } = new();
    public ObservableCollection<string> LiftOffItems { get; } = new();
    public ObservableCollection<string> AutoPowerOffItems { get; } = new();
    public ObservableCollection<string> LightingModeItems { get; } = new();
    public ObservableCollection<string> AnimationSpeedItems { get; } = new();
    public ObservableCollection<string> AnimationDirectionItems { get; } = new();
    public ObservableCollection<DpiSlot> DpiSlots { get; } = new();
    public ObservableCollection<ButtonBindingRow> BindingRows { get; } = new();

    // ---------------------------------------------------------------- bindable state

    public int SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (value < 0 || !Set(ref _selectedProfile, value) || Loading) return;
            if (_mouse.Profile == value) return;

            _mouse.SetProfile(value);
            RefreshMouseData();
        }
    }

    /// <summary>
    /// Value of the DPI slider / box of the active profile (display only). The view writes the user's changes here and
    /// calls <see cref="CommitDpi"/> when the slider is released / the box edited.
    /// </summary>
    public int Dpi
    {
        get => _dpi;
        set => Set(ref _dpi, value);
    }

    public Color DpiColor { get => _dpiColor; private set => Set(ref _dpiColor, value); }

    public int PollingRateIndex
    {
        get => _pollingRateIndex;
        set
        {
            if (value < 0 || !Set(ref _pollingRateIndex, value) || Loading) return;
            PollingRate[] rates = _mouse.SupportedPollingrates();
            if (value >= rates.Length) return;

            _mouse.SetPollingRate(rates[value]);
            UpdateMotionSyncState();
        }
    }

    public bool AngleSnapping
    {
        get => _angleSnapping;
        set
        {
            if (!Set(ref _angleSnapping, value) || Loading) return;
            _mouse.SetAngleSnapping(value);
            _mouse.SetAngleAdjustment((short)_angleAdjustment);
        }
    }

    public int AngleAdjustment
    {
        get => _angleAdjustment;
        set
        {
            if (Set(ref _angleAdjustment, value)) Raise(nameof(AngleAdjustmentText));
        }
    }

    public string AngleAdjustmentText => _angleAdjustment + "°";

    public bool MotionSync
    {
        get => _motionSync;
        set
        {
            if (!Set(ref _motionSync, value) || Loading) return;
            _mouse.SetMotionSync(value);
        }
    }

    /// <summary>Motion sync is not available at 8000 Hz.</summary>
    public bool MotionSyncEnabled { get => _motionSyncEnabled; private set => Set(ref _motionSyncEnabled, value); }

    public bool ZoneMode
    {
        get => _zoneMode;
        set
        {
            if (!Set(ref _zoneMode, value) || Loading) return;
            _mouse.SetZoneMode(value);
            UpdateZoneModeUiState();
        }
    }

    /// <summary>Zone mode DPI (display only, written by <see cref="CommitZoneModeDpi"/>).</summary>
    public int ZoneModeDpi
    {
        get => _zoneModeDpi;
        set => Set(ref _zoneModeDpi, value);
    }

    public int ZoneModePollingRateIndex
    {
        get => _zoneModePollingRateIndex;
        set
        {
            if (value < 0 || !Set(ref _zoneModePollingRateIndex, value) || Loading) return;
            if (value >= ZoneModePollingRates.Length) return;

            _mouse.ZoneModePollingRate = ZoneModePollingRates[value];
            if (_mouse.ZoneMode) _mouse.UpdateZoneModePollingRate(_mouse.ZoneModePollingRate);
        }
    }

    /// <summary>Zone mode takes over DPI / polling rate: these are enabled by the toggle.</summary>
    public bool ZoneModeControlsEnabled { get => _zoneModeControlsEnabled; private set => Set(ref _zoneModeControlsEnabled, value); }

    /// <summary>The regular DPI profiles + polling rate are disabled while zone mode is on.</summary>
    public bool NormalControlsEnabled { get => _normalControlsEnabled; private set => Set(ref _normalControlsEnabled, value); }

    public int ButtonDebounce
    {
        get => _debounce;
        set
        {
            if (Set(ref _debounce, value)) Raise(nameof(ButtonDebounceText));
        }
    }

    public string ButtonDebounceText => _mouse.HasDebounceSetting()
        ? _mouse.DebounceTimeInMS((DebounceTime)_debounce) + "ms"
        : "";

    public int LiftOffIndex
    {
        get => _liftOffIndex;
        set
        {
            if (value < 0 || !Set(ref _liftOffIndex, value) || Loading) return;
            _mouse.SetLiftOffDistance((LiftOffDistance)value);
        }
    }

    public int Acceleration
    {
        get => _acceleration;
        set
        {
            if (Set(ref _acceleration, value)) Raise(nameof(AccelerationText));
        }
    }

    public string AccelerationText => _acceleration.ToString();

    public int Deceleration
    {
        get => _deceleration;
        set
        {
            if (Set(ref _deceleration, value)) Raise(nameof(DecelerationText));
        }
    }

    public string DecelerationText => _deceleration.ToString();

    // lighting ---------------------------------------------------------------

    public bool ZoneAllActivated => _visibleZone == LightingZone.All;
    public bool ZoneLogoActivated => _visibleZone == LightingZone.Logo;
    public bool ZoneScrollActivated => _visibleZone == LightingZone.Scrollwheel;
    public bool ZoneUnderglowActivated => _visibleZone == LightingZone.Underglow;
    public bool ZoneDockActivated => _visibleZone == LightingZone.Dock;

    /// <summary>"All" is the secondary (dim) button when the zones differ, the individual zones when they are equal.</summary>
    public bool ZoneAllSecondary { get; private set; }
    public bool ZoneIndividualSecondary { get; private set; }

    public int Brightness
    {
        get => _brightness;
        set => Set(ref _brightness, value);
    }

    public int SelectedLightingModeIndex
    {
        get => _selectedLightingModeIndex;
        set
        {
            if (value < 0 || !Set(ref _selectedLightingModeIndex, value) || Loading) return;
            ApplyLightingMode(value);
        }
    }

    public Color LightingColor { get => _lightingColor; private set => Set(ref _lightingColor, value); }

    public bool RandomColor
    {
        get => _randomColor;
        set
        {
            if (!Set(ref _randomColor, value) || Loading) return;

            LightingSetting? ls = _mouse.LightingSettingForZone(_visibleZone);
            if (ls is null) return;
            ls.RandomColor = value;
            UpdateLightingSettings(ls);
        }
    }

    public bool RandomColorVisible
    {
        get => _randomColorVisible;
        private set
        {
            if (Set(ref _randomColorVisible, value)) Raise(nameof(ColorRowVisible));
        }
    }

    public bool ColorVisible
    {
        get => _colorVisible;
        private set
        {
            if (Set(ref _colorVisible, value)) Raise(nameof(ColorRowVisible));
        }
    }

    /// <summary>The colour button and the random colour box share one row.</summary>
    public bool ColorRowVisible => _colorVisible || _randomColorVisible;
    public bool AnimationSpeedVisible { get => _animationSpeedVisible; private set => Set(ref _animationSpeedVisible, value); }
    public bool AnimationDirectionVisible { get => _animationDirectionVisible; private set => Set(ref _animationDirectionVisible, value); }

    public bool SyncAura
    {
        get => _syncAura;
        set
        {
            if (!Set(ref _syncAura, value)) return;
            Raise(nameof(LightingControlsEnabled));
            if (Loading) return;

            PeripheralsProvider.SetAuraSync(value);
            PeripheralHost.Refresh("keyboard");

            if (value)
            {
                _mouse.SyncFromKeyboardAura();
                VisualizeLightingSettings();
            }
        }
    }

    /// <summary>Mode, colour, speed and direction follow the laptop keyboard while "sync with Aura" is on.</summary>
    public bool LightingControlsEnabled => !_syncAura;

    public int AnimationSpeedIndex
    {
        get => _animationSpeedIndex;
        set
        {
            if (value < 0 || !Set(ref _animationSpeedIndex, value) || Loading) return;

            LightingSetting? ls = _mouse.LightingSettingForZone(_visibleZone);
            if (ls is null) return;
            // 0 => 0x9, 1 => 0x7, 2 => 0x5
            ls.AnimationSpeed = (AnimationSpeed)(0x9 - value * 0x2);
            UpdateLightingSettings(ls);
        }
    }

    public int AnimationDirectionIndex
    {
        get => _animationDirectionIndex;
        set
        {
            if (value < 0 || !Set(ref _animationDirectionIndex, value) || Loading) return;

            LightingSetting? ls = _mouse.LightingSettingForZone(_visibleZone);
            if (ls is null) return;
            ls.AnimationDirection = (AnimationDirection)value;
            UpdateLightingSettings(ls);
        }
    }

    // energy -----------------------------------------------------------------

    public int AutoPowerOffIndex
    {
        get => _autoPowerOffIndex;
        set
        {
            if (value < 0 || !Set(ref _autoPowerOffIndex, value) || Loading) return;

            var values = Enum.GetValues(typeof(PowerOffSetting));
            if (value >= values.Length) return;
            _mouse.SetEnergySettings(_mouse.LowBatteryWarning, (PowerOffSetting)values.GetValue(value)!);
        }
    }

    public int LowBatteryWarning
    {
        get => _lowBatteryWarning;
        set
        {
            if (Set(ref _lowBatteryWarning, value)) Raise(nameof(LowBatteryWarningText));
        }
    }

    public string LowBatteryWarningText => _lowBatteryWarning + "%";

    // bindings ---------------------------------------------------------------

    /// <summary>The remap list is shown once the device reported its bindings.</summary>
    public bool BindingsVisible { get => _bindingsVisible; private set => Set(ref _bindingsVisible, value); }

    public bool ShiftLayer => _shiftLayer;
    public bool NormalLayerActivated => !_shiftLayer;
    public bool ShiftLayerActivated => _shiftLayer;

    public ICommand SyncCommand => new RelayCommand(RefreshMouseData);
    public ICommand ResetBindingsCommand => new RelayCommand(ResetBindings);
    public ICommand NormalLayerCommand => new RelayCommand(() => SetBindingLayer(false));
    public ICommand ShiftLayerCommand => new RelayCommand(() => SetBindingLayer(true));
    public ICommand ZoneAllCommand => new RelayCommand(() => SwitchLightingZone(LightingZone.All));
    public ICommand ZoneLogoCommand => new RelayCommand(() => SwitchLightingZone(LightingZone.Logo));
    public ICommand ZoneScrollCommand => new RelayCommand(() => SwitchLightingZone(LightingZone.Scrollwheel));
    public ICommand ZoneUnderglowCommand => new RelayCommand(() => SwitchLightingZone(LightingZone.Underglow));
    public ICommand ZoneDockCommand => new RelayCommand(() => SwitchLightingZone(LightingZone.Dock));

    /// <summary>View hook: choose a .gmp1 file and return its content (null = cancelled).</summary>
    public Func<Task<byte[]?>>? PickImportData { get; set; }

    /// <summary>View hook: ask for a target file and write the data to it.</summary>
    public Func<byte[], Task>? SaveExportData { get; set; }

    // ================================================================= start / stop

    public override void Start()
    {
        if (_started || IsDisposed) return;
        _started = true;

        _mouse.Disconnect += Mouse_Disconnect;
        _mouse.BatteryUpdated += Mouse_BatteryUpdated;
        _mouse.MouseReadyChanged += Mouse_ReadyChanged;
        _mouse.ProfileChanged += Mouse_ProfileChanged;
        _mouse.ButtonBindingsChanged += Mouse_ButtonBindingsChanged;
        _mouse.StartEventListener();
    }

    public override void Dispose()
    {
        if (_started)
        {
            _started = false;
            try { _mouse.StopEventListener(); } catch { }
            _mouse.ProfileChanged -= Mouse_ProfileChanged;
            _mouse.BatteryUpdated -= Mouse_BatteryUpdated;
            _mouse.Disconnect -= Mouse_Disconnect;
            _mouse.MouseReadyChanged -= Mouse_ReadyChanged;
            _mouse.ButtonBindingsChanged -= Mouse_ButtonBindingsChanged;
        }
        base.Dispose();
    }

    private void Mouse_Disconnect(object? sender, EventArgs e) => Post(RequestClose);

    private void Mouse_ReadyChanged(object? sender, EventArgs e)
    {
        if (!_mouse.IsDeviceReady) Post(RequestClose);
    }

    private void Mouse_BatteryUpdated(object? sender, EventArgs e) => Post(VisualizeBatteryState);

    private void Mouse_ProfileChanged(object? sender, EventArgs e) => Post(() =>
    {
        if (IsDisposed) return;
        BeginLoading();
        try { VisualizeMouseSettings(); }
        finally { EndLoading(); }
    });

    private void Mouse_ButtonBindingsChanged(object? sender, EventArgs e) => Post(() =>
    {
        if (IsDisposed) return;
        BindingsVisible = HasButtonBindings && _mouse.ButtonBindingsReady;
        if (BindingsVisible) VisualizeButtonBindings();
    });

    // ================================================================= setup

    private void InitCapabilities()
    {
        var mouse = _mouse;

        for (int i = 0; i < mouse.ProfileCount(); ++i)
            ProfileItems.Add(Strings.Profile + " " + (i + 1));

        DpiMin = mouse.MinDPI();
        DpiMax = mouse.MaxDPI();
        DpiStep = Math.Max(1, mouse.DPIIncrements());
        DpiMinText = DpiMin.ToString();
        DpiMaxText = DpiMax.ToString();
        DpiColorVisible = mouse.HasDPIColors();

        for (int i = 0; i < 4; ++i)
        {
            int index = i;
            var slot = new DpiSlot(i)
            {
                Command = new RelayCommand(() => OnDpiSlotClicked(index)),
                DeleteCommand = new RelayCommand(() => DeleteDpiProfile(index)),
            };

            if (!mouse.HasDPIColors())
            {
                // no colours on the device: fixed dots like legacy (red, purple, blue, green)
                slot.DotColor = FallbackDpiColors[i];
                slot.BorderColor = FallbackDpiColors[i];
            }

            slot.IsVisible = i < Math.Max(1, mouse.DPIProfileCount());
            DpiSlots.Add(slot);
        }

        PerformanceVisible = mouse.DPIProfileCount() != 0;

        PollingRateVisible = mouse.CanSetPollingRate();
        if (PollingRateVisible)
        {
            foreach (PollingRate pr in mouse.SupportedPollingrates())
                PollingRateItems.Add(mouse.PollingRateDisplayString(pr));
        }

        AngleSnappingVisible = mouse.HasAngleSnapping();
        AngleTuningVisible = mouse.HasAngleTuning();
        AngleSnappingPanelVisible = AngleSnappingVisible || AngleTuningVisible;
        if (AngleTuningVisible)
        {
            AngleMin = mouse.AngleTuningMin();
            AngleMax = mouse.AngleTuningMax();
            AngleStep = Math.Max(1, mouse.AngleTuningStep());
        }

        MotionSyncVisible = mouse.HasMotionSync();

        ZoneModeVisible = mouse.HasZoneMode();
        if (ZoneModeVisible)
        {
            foreach (var rate in ZoneModePollingRates)
                ZoneModePollingRateItems.Add(mouse.PollingRateDisplayString(rate));
            _zoneModePollingRateIndex = 2; // default 4000 Hz
        }

        AccelerationVisible = mouse.HasAcceleration();
        if (AccelerationVisible) AccelerationMax = mouse.MaxAcceleration();
        DecelerationVisible = mouse.HasDeceleration();
        if (DecelerationVisible) DecelerationMax = mouse.MaxDeceleration();

        LiftOffVisible = mouse.HasLiftOffSetting();
        if (LiftOffVisible)
        {
            LiftOffItems.Add(Strings.Low);
            LiftOffItems.Add(Strings.High);
        }

        DebounceVisible = mouse.HasDebounceSetting();
        if (DebounceVisible)
        {
            DebounceMin = (int)mouse.MinDebounce();
            DebounceMax = (int)mouse.MaxDebounce();
        }

        BatteryVisible = mouse.HasBattery();

        ProfilesVisible = mouse.HasProfiles();
        BottomButtonsVisible = mouse.HasProfiles();

        AutoPowerOffVisible = mouse.HasAutoPowerOff();
        if (AutoPowerOffVisible)
        {
            AutoPowerOffItems.Add("1 " + Strings.Minute);
            AutoPowerOffItems.Add("2 " + Strings.Minutes);
            AutoPowerOffItems.Add("3 " + Strings.Minutes);
            AutoPowerOffItems.Add("5 " + Strings.Minutes);
            AutoPowerOffItems.Add("10 " + Strings.Minutes);
            AutoPowerOffItems.Add(Strings.Never);
        }

        LowBatteryWarningVisible = mouse.HasLowBatteryWarning();
        if (LowBatteryWarningVisible)
        {
            LowBatteryWarningStep = Math.Max(1, mouse.LowBatteryWarningStep());
            LowBatteryWarningMax = mouse.LowBatteryWarningMax();
        }

        EnergyVisible = AutoPowerOffVisible || LowBatteryWarningVisible;

        LightingVisible = mouse.HasRGB();
        if (LightingVisible)
        {
            LightingZone[] zones = mouse.SupportedLightingZones();
            ZonesVisible = zones.Length > 1;
            if (ZonesVisible)
            {
                ZoneLogoVisible = zones.Contains(LightingZone.Logo);
                ZoneScrollVisible = zones.Contains(LightingZone.Scrollwheel);
                ZoneUnderglowVisible = zones.Contains(LightingZone.Underglow);
                ZoneDockVisible = zones.Contains(LightingZone.Dock);
            }

            _syncAura = PeripheralsProvider.IsAuraSync;
            BrightnessMax = Math.Max(1, mouse.MaxBrightness());

            InitLightingModes();

            AnimationDirectionItems.Add(Strings.AuraClockwise);
            AnimationDirectionItems.Add(Strings.AuraCounterClockwise);
            AnimationSpeedItems.Add(Strings.AuraSlow);
            AnimationSpeedItems.Add(Strings.AuraNormal);
            AnimationSpeedItems.Add(Strings.AuraFast);
        }

        HasButtonBindings = mouse.HasButtonBindings();
        if (HasButtonBindings) InitBindingRows();
    }

    private void InitLightingModes()
    {
        LightingModeItems.Clear();
        _supportedLightingModes.Clear();
        foreach (LightingMode lm in Enum.GetValues(typeof(LightingMode)))
        {
            if (_mouse.IsLightingModeSupported(lm) && _mouse.IsLightingModeSupportedForZone(lm, _visibleZone))
            {
                LightingModeItems.Add(LightingModeNames.TryGetValue(lm, out var name) ? name() : lm.ToString());
                _supportedLightingModes.Add(lm);
            }
        }

        // the list was rebuilt: the combo lost its selection, re-push the current index
        Raise(nameof(SelectedLightingModeIndex));
    }

    private void InitBindingRows()
    {
        _bindingEntries.Clear();
        foreach (var (groupLabel, items) in _mouse.BindingGroups)
        {
            _bindingEntries.Add(BindingEntry.Separator(groupLabel));
            foreach (var (code, name) in items)
                _bindingEntries.Add(BindingEntry.Item(code, name));
        }

        HasSpeedShift = _mouse.HasSpeedShift;

        foreach (var (slot, (_, name)) in _mouse.ButtonSlots)
        {
            var row = new ButtonBindingRow(slot, name, _bindingEntries);
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ButtonBindingRow.Selected)) OnBindingSelected(row);
            };
            BindingRows.Add(row);
        }
    }

    // ================================================================= refresh from the device

    /// <summary>Legacy RefreshMouseData: read everything again and repaint (also the "Synchronize" button).</summary>
    public void RefreshMouseData()
    {
        _mouse.SynchronizeDevice();

        Logger.WriteLine(_mouse.GetDisplayName() + " (GUI): Mouse data synchronized");
        if (!_mouse.IsDeviceReady)
        {
            Logger.WriteLine(_mouse.GetDisplayName() + " (GUI): Mouse is not ready. Closing view.");
            RequestClose();
            return;
        }

        if (IsDisposed) return;

        BeginLoading();
        try
        {
            VisualizeMouseSettings();
            VisualizeBatteryState();
            BindingsVisible = HasButtonBindings && _mouse.ButtonBindingsReady;
            if (BindingsVisible) VisualizeButtonBindings();
        }
        finally
        {
            EndLoading();
        }
    }

    private void VisualizeMouseSettings()
    {
        var mouse = _mouse;

        if (mouse.Profile < ProfileItems.Count)
            SelectedProfile = mouse.Profile;

        if (mouse.HasRGB())
        {
            // if the current lighting is zoned pre-select the first zone and not "All"
            if (mouse.IsLightingZoned())
            {
                _visibleZone = mouse.SupportedLightingZones()[0];
                InitLightingModes();
            }
        }

        VisualizeDpiButtons();
        VisualizeCurrentDpiProfile();
        VisualizeLightingSettings();

        if (mouse.CanSetPollingRate())
        {
            int idx = mouse.PollingRateIndex(mouse.PollingRate);
            if (idx != -1) PollingRateIndex = idx;
        }

        if (mouse.HasAngleSnapping())
            AngleSnapping = mouse.AngleSnapping;

        if (mouse.HasAngleTuning())
            AngleAdjustment = mouse.AngleAdjustmentDegrees;

        if (mouse.HasAutoPowerOff())
        {
            AutoPowerOffIndex = mouse.PowerOffSetting == PowerOffSetting.Never
                ? AutoPowerOffItems.Count - 1
                : (int)mouse.PowerOffSetting;
        }

        if (mouse.HasLowBatteryWarning())
            LowBatteryWarning = mouse.LowBatteryWarning;

        if (mouse.HasLiftOffSetting())
            LiftOffIndex = (int)mouse.LiftOffDistance;

        if (mouse.HasDebounceSetting())
            ButtonDebounce = (int)mouse.Debounce;

        if (mouse.HasAcceleration())
            Acceleration = mouse.Acceleration;

        if (mouse.HasDeceleration())
            Deceleration = mouse.Deceleration;

        if (mouse.HasMotionSync())
            UpdateMotionSyncState();

        if (mouse.HasZoneMode())
        {
            ZoneMode = mouse.ZoneMode;
            ZoneModeDpi = mouse.ZoneModeDPI;

            int prIndex = Array.IndexOf(ZoneModePollingRates, mouse.ZoneModePollingRate);
            if (prIndex >= 0) ZoneModePollingRateIndex = prIndex;

            UpdateZoneModeUiState();
        }
    }

    private void UpdateMotionSyncState()
    {
        if (!_mouse.HasMotionSync()) return;

        bool is8000Hz = _mouse.PollingRate == PollingRate.PR8000Hz;
        MotionSyncEnabled = !is8000Hz;

        // keep the toggle write-free: only the view state changes here
        BeginLoading();
        try { MotionSync = !is8000Hz && _mouse.MotionSync; }
        finally { EndLoading(); }
    }

    private void UpdateZoneModeUiState()
    {
        if (!_mouse.HasZoneMode()) return;

        bool on = _mouse.ZoneMode;
        ZoneModeControlsEnabled = on;
        NormalControlsEnabled = !on;
    }

    private void VisualizeBatteryState()
    {
        if (!_mouse.HasBattery()) return;

        BatteryText = _mouse.Battery + "%";
        BatteryCharging = _mouse.Charging;
    }

    // ----------------------------------------------------------------- DPI

    private void VisualizeDpiButtons()
    {
        int count = _mouse.CurrentDPIProfileCount;

        for (int i = 0; i < 4; ++i)
        {
            DpiSlot slot = DpiSlots[i];

            if (i < count)
            {
                AsusMouseDPI? dpi = i < _mouse.DpiSettings.Length ? _mouse.DpiSettings[i] : null;
                if (dpi is null) continue;

                slot.IsVisible = true;
                slot.IsAdd = false;
                if (_mouse.HasDPIColors())
                {
                    Color c = PeripheralColors.ToAvalonia(dpi.Color);
                    slot.DotColor = c;
                    slot.BorderColor = c;
                }

                slot.Activated = _mouse.DpiProfile - 1 == i;
                slot.Text = "DPI " + (i + 1) + "\n" + dpi.DPI;
                slot.CanDelete = _mouse.CanChangeDPICount() && count > 2;
            }
            else if (_mouse.CanChangeDPICount() && i == count)
            {
                // the "add profile" slot
                slot.IsVisible = true;
                slot.IsAdd = true;
                slot.DotColor = null;
                slot.Text = "+";
                slot.Activated = false;
                slot.BorderColor = Colors.Transparent;
                slot.CanDelete = false;
            }
            else
            {
                slot.IsVisible = false;
                slot.IsAdd = false;
                slot.CanDelete = false;
            }
        }
    }

    private void VisualizeCurrentDpiProfile()
    {
        if (_mouse.DpiProfile > _mouse.DpiSettings.Length || _mouse.DpiProfile < 1)
        {
            Logger.WriteLine($"Wrong mouse DPI: {_mouse.DpiProfile}");
            return;
        }

        AsusMouseDPI? dpi = _mouse.DpiSettings[_mouse.DpiProfile - 1];
        if (dpi is null) return;

        BeginLoading();
        try
        {
            Dpi = (int)dpi.DPI;
            DpiColor = PeripheralColors.ToAvalonia(dpi.Color);
        }
        finally { EndLoading(); }
    }

    /// <summary>Write the displayed DPI to the active profile (slider released, numeric box edited).</summary>
    public void CommitDpi()
    {
        if (_mouse.DpiProfile < 1 || _mouse.DpiProfile > _mouse.DpiSettings.Length) return;

        AsusMouseDPI? dpi = _mouse.DpiSettings[_mouse.DpiProfile - 1];
        if (dpi is null || dpi.DPI == (uint)_dpi) return;

        dpi.DPI = (uint)_dpi;
        _mouse.SetDPIForProfile(dpi, _mouse.DpiProfile);

        BeginLoading();
        try
        {
            VisualizeDpiButtons();
            VisualizeCurrentDpiProfile();
        }
        finally { EndLoading(); }
    }

    /// <summary>Colour picked for the active DPI profile (live apply).</summary>
    public void SetDpiColor(Color color)
    {
        if (_mouse.DpiProfile < 1 || _mouse.DpiProfile > _mouse.DpiSettings.Length) return;

        AsusMouseDPI? dpi = _mouse.DpiSettings[_mouse.DpiProfile - 1];
        if (dpi is null) return;

        dpi.Color = PeripheralColors.ToDrawingOpaque(color);
        _mouse.SetDPIForProfile(dpi, _mouse.DpiProfile);

        BeginLoading();
        try
        {
            VisualizeDpiButtons();
            VisualizeCurrentDpiProfile();
        }
        finally { EndLoading(); }
    }

    private void OnDpiSlotClicked(int index)
    {
        // index < count: switch profile, index == count: add a profile
        if (index < _mouse.CurrentDPIProfileCount)
        {
            _mouse.SetDPIProfile(index + 1);
        }
        else if (index == _mouse.CurrentDPIProfileCount)
        {
            _mouse.AddDPIProfile();
        }

        BeginLoading();
        try
        {
            VisualizeDpiButtons();
            VisualizeCurrentDpiProfile();
        }
        finally { EndLoading(); }
    }

    /// <summary>The X in the corner of a profile button.</summary>
    public void DeleteDpiProfile(int index)
    {
        if (!_mouse.CanChangeDPICount() || index >= _mouse.CurrentDPIProfileCount || _mouse.CurrentDPIProfileCount <= 2) return;

        _mouse.DeleteDPIProfile(index);

        BeginLoading();
        try
        {
            VisualizeDpiButtons();
            VisualizeCurrentDpiProfile();
        }
        finally { EndLoading(); }
    }

    // ----------------------------------------------------------------- slider commits

    public void CommitAngleAdjustment() => _mouse.SetAngleAdjustment((short)_angleAdjustment);

    public void CommitButtonDebounce() => _mouse.SetDebounce((DebounceTime)_debounce);

    public void CommitAcceleration() => _mouse.SetAcceleration(_acceleration);

    public void CommitDeceleration() => _mouse.SetDeceleration(_deceleration);

    public void CommitLowBatteryWarning() => _mouse.SetEnergySettings(_lowBatteryWarning, _mouse.PowerOffSetting);

    public void CommitZoneModeDpi()
    {
        if (_mouse.ZoneMode && _mouse.ZoneModeDPI == _zoneModeDpi) return;

        _mouse.ZoneModeDPI = _zoneModeDpi;
        if (_mouse.ZoneMode) _mouse.UpdateZoneModeDPI(_zoneModeDpi);
    }

    public void CommitBrightness()
    {
        LightingSetting? ls = _mouse.LightingSettingForZone(_visibleZone);
        if (ls is null) return;
        ls.Brightness = _brightness;
        UpdateLightingSettings(ls);
    }

    // ----------------------------------------------------------------- lighting

    private void SwitchLightingZone(LightingZone zone)
    {
        if (!_mouse.HasRGB()) return;

        _visibleZone = zone;
        BeginLoading();
        try
        {
            InitLightingModes();
            VisualizeLightingSettings();
        }
        finally { EndLoading(); }
    }

    private void ApplyLightingMode(int index)
    {
        if (!_mouse.HasRGB()) return;
        if (_supportedLightingModes.Count == 0) return;

        LightingMode lm = _supportedLightingModes[index >= 0 && index < _supportedLightingModes.Count ? index : 0];

        LightingSetting? ls = _mouse.LightingSettingForZone(_visibleZone);
        if (ls is null || ls.LightingMode == lm) return;

        ls.LightingMode = lm;
        UpdateLightingSettings(ls);
    }

    /// <summary>Colour picked for the visible lighting zone (live apply).</summary>
    public void SetLightingColor(Color color)
    {
        LightingSetting? ls = _mouse.LightingSettingForZone(_visibleZone);
        if (ls is null) return;

        ls.RGBColor = PeripheralColors.ToDrawingOpaque(color);
        UpdateLightingSettings(ls);
    }

    public Color CurrentLightingColor => _lightingColor;

    private void UpdateLightingSettings(LightingSetting settings)
    {
        _mouse.SetLightingSetting(settings, _visibleZone);

        BeginLoading();
        try { VisualizeLightingSettings(); }
        finally { EndLoading(); }
    }

    private void VisualizeLightingSettings()
    {
        if (!_mouse.HasRGB()) return;

        VisualizeLightingZones();

        LightingSetting? ls = _mouse.LightingSettingForZone(_visibleZone);
        if (ls is null) return; // lighting settings not loaded

        Brightness = ls.Brightness;

        bool randomColor = _mouse.SupportsRandomColor(ls.LightingMode);
        RandomColorVisible = randomColor;
        ColorVisible = _mouse.SupportsColorSetting(ls.LightingMode);
        AnimationSpeedVisible = _mouse.SupportsAnimationSpeed(ls.LightingMode);
        AnimationDirectionVisible = _mouse.SupportsAnimationDirection(ls.LightingMode);

        SelectedLightingModeIndex = Math.Max(0, _supportedLightingModes.IndexOf(ls.LightingMode));

        if (randomColor)
        {
            RandomColor = ls.RandomColor;
            ColorVisible = !ls.RandomColor;
        }

        LightingColor = PeripheralColors.ToAvalonia(ls.RGBColor);

        // 0x09 => 0, 0x07 => 1, 0x05 => 2
        AnimationSpeedIndex = Math.Clamp(2 - (((int)ls.AnimationSpeed - 5) / 2), 0, 2);

        int direction = (int)ls.AnimationDirection;
        AnimationDirectionIndex = direction >= 0 && direction < AnimationDirectionItems.Count ? direction : 0;
    }

    private void VisualizeLightingZones()
    {
        bool zoned = _mouse.IsLightingZoned();
        ZoneAllSecondary = zoned;
        ZoneIndividualSecondary = !zoned;

        Raise(nameof(ZoneAllActivated));
        Raise(nameof(ZoneLogoActivated));
        Raise(nameof(ZoneScrollActivated));
        Raise(nameof(ZoneUnderglowActivated));
        Raise(nameof(ZoneDockActivated));
        Raise(nameof(ZoneAllSecondary));
        Raise(nameof(ZoneIndividualSecondary));
    }

    // ----------------------------------------------------------------- bindings

    private void SetBindingLayer(bool shift)
    {
        _shiftLayer = shift;
        Raise(nameof(ShiftLayer));
        Raise(nameof(NormalLayerActivated));
        Raise(nameof(ShiftLayerActivated));
        VisualizeButtonBindings();
    }

    private void ResetBindings()
    {
        _mouse.ResetButtonBindings();
        VisualizeButtonBindings();
        PeripheralHost.RegisterKeys();
    }

    private void OnBindingSelected(ButtonBindingRow row)
    {
        if (_updatingBindings || row.Selected is not { } entry) return;
        if (entry.IsSeparator)
        {
            // headings cannot be chosen: step forward to the first real item of the group
            int next = _bindingEntries.IndexOf(entry) + 1;
            if (next < _bindingEntries.Count && !_bindingEntries[next].IsSeparator) row.Selected = _bindingEntries[next];
            return;
        }

        if (_shiftLayer)
        {
            // a button cannot be bound to "Speed Shift" on the shift layer itself
            if (entry.Code == AsusMouse.SpeedShiftCode) VisualizeButtonBindings();
            else _mouse.SetSpeedShiftBinding(row.Slot, entry.Code);
            return;
        }

        _mouse.SetButtonBinding(row.Slot, entry.Code);
        PeripheralHost.RegisterKeys();
    }

    private void VisualizeButtonBindings()
    {
        if (!_mouse.HasButtonBindings()) return;

        _updatingBindings = true;
        try
        {
            foreach (ButtonBindingRow row in BindingRows)
            {
                bool locked = _shiftLayer && _mouse.ButtonBindings[row.Slot] == AsusMouse.SpeedShiftCode;
                ushort code = _shiftLayer && !locked ? _mouse.GetSpeedShiftBinding(row.Slot) : _mouse.ButtonBindings[row.Slot];
                row.Locked = locked;
                row.Selected = _bindingEntries.FirstOrDefault(e => !e.IsSeparator && e.Code == code);
            }
        }
        finally
        {
            _updatingBindings = false;
        }
    }

    // ----------------------------------------------------------------- export / import

    public async Task ExportAsync()
    {
        if (SaveExportData is null) return;
        await SaveExportData(_mouse.Export());
    }

    public async Task ImportAsync()
    {
        if (PickImportData is null) return;

        byte[]? data = await PickImportData();
        if (data is null) return; // user aborted

        if (!_mouse.Import(data))
        {
            Logger.WriteLine("Failed to import mouse profile");
            PeripheralHost.Alert("", Strings.MouseImportFailed);
            return;
        }

        if (!_mouse.IsLightingZoned()) _visibleZone = LightingZone.All;
        RefreshMouseData();
    }
}
