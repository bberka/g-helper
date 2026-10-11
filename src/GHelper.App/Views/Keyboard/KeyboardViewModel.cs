using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using GHelper.App.ViewModels;
using GHelper.App.Views.Peripherals;
using GHelper.Peripherals;
using GHelper.Peripherals.Keyboard;
using GHelper.Peripherals.Keyboard.Models;
using GHelper.Peripherals.Mouse;
using GHelper.Properties;
using GHelper.USB;

namespace GHelper.App.Views.Keyboard;

/// <summary>Captions of the keyboard window.</summary>
public sealed class KeyboardTexts
{
    public string Lighting => Strings.Lighting;
    public string LightingMode => Strings.AuraLightingMode;
    public string AnimationSpeed => Strings.AnimationSpeed;
    public string SyncAura => Strings.MouseSyncWithAura;
    public string Color => Strings.Color;
    public string Color2 => Strings.Color + " 2";
    public string Background => Strings.Background;
    public string KeyBindings => Strings.KeyBindings;
    public string Profile => Strings.Profile;
    public string Reset => Strings.Reset;
    public string FillAll => Strings.FillAll;
    public string Keys => Strings.Keys;
    public string TestLayout => Strings.TestLayout;
    public string Energy => Strings.EnergySettings;
    public string AutoPowerOff => Strings.MouseAutoPowerOff;
    public string LowBatteryWarning => Strings.MouseLowBatteryWarning;
    public string Oled => "OLED";
    public string Animation => Strings.Animation;
}

/// <summary>
/// State and device logic of the keyboard window (port of app/AsusKeyboardSettings.cs): lighting mode / colours /
/// brightness, per key painting on the key grid, key bindings, profiles, energy settings and the Azoth OLED.
/// Device IO runs through <see cref="PeripheralViewModelBase.Background"/> like the legacy Task.Run calls.
/// </summary>
public sealed class KeyboardViewModel : PeripheralViewModelBase
{
    /// <summary>Size of one key unit and the gap between keys in DIPs.</summary>
    public const double KeyUnit = 38;
    public const double KeyGap = 3;

    private static readonly Dictionary<KeyboardLightingMode, Func<string>> LightingModeNames = new()
    {
        { KeyboardLightingMode.Static, () => Strings.AuraStatic },
        { KeyboardLightingMode.Breathing, () => Strings.AuraBreathe },
        { KeyboardLightingMode.ColorCycle, () => Strings.AuraColorCycle },
        { KeyboardLightingMode.Reactive, () => Strings.AuraReact },
        { KeyboardLightingMode.Wave, () => Strings.AuraRainbow },
        { KeyboardLightingMode.Ripple, () => "Ripple" },
        { KeyboardLightingMode.StarryNight, () => "Starry Night" },
        { KeyboardLightingMode.Quicksand, () => "Quicksand" },
        { KeyboardLightingMode.Current, () => "Current" },
        { KeyboardLightingMode.RainDrop, () => "Rain Drop" },
        { KeyboardLightingMode.Direct, () => "Custom" },
    };

    /// <summary>Quick colours of the paint row (legacy paletteColors).</summary>
    public static readonly Color[] PaletteColors =
    {
        Color.FromRgb(0, 0, 0), Color.FromRgb(255, 255, 255),
        Color.FromRgb(255, 0, 0), Color.FromRgb(255, 128, 0), Color.FromRgb(255, 255, 0),
        Color.FromRgb(128, 255, 0), Color.FromRgb(0, 255, 0), Color.FromRgb(0, 255, 255),
        Color.FromRgb(0, 128, 255), Color.FromRgb(0, 0, 255), Color.FromRgb(128, 0, 255),
        Color.FromRgb(255, 0, 255), Color.FromRgb(255, 0, 128), Color.FromRgb(128, 128, 128),
    };

    // Code is a config-only id, never sent to the device
    private sealed record ComboDef(ushort Code, string Name, ushort[] Keys);

    private static ushort K(string name) => AuraKeyboardLayouts.Keys[name];

    private static readonly ComboDef[] KeyCombos =
    {
        new(0xC101, "Copy", new[] { K("Ctrl"), K("C") }),
        new(0xC102, "Paste", new[] { K("Ctrl"), K("V") }),
        new(0xC103, "Cut", new[] { K("Ctrl"), K("X") }),
        new(0xC104, "Undo", new[] { K("Ctrl"), K("Z") }),
        new(0xC105, "Select All", new[] { K("Ctrl"), K("A") }),
        new(0xC106, "Alt + Tab", new[] { K("Alt"), K("Tab") }),
        new(0xC107, "Alt + F4", new[] { K("Alt"), K("F4") }),
        new(0xC108, "Show Desktop", new[] { K("Win"), K("D") }),
        new(0xC109, "Lock PC", new[] { K("Win"), K("L") }),
        new(0xC10A, "Task Manager", new[] { K("Ctrl"), K("Shift"), K("Esc") }),
        new(0xC10B, "Screenshot", new[] { K("Win"), K("PrtSc") }),
        new(0xC10C, "Snipping Tool", new[] { K("Win"), K("Shift"), K("S") }),
    };

    private readonly AsusKeyboard _keyboard;
    private readonly Azoth? _oled;
    private readonly List<KeyboardLightingMode> _supportedModes;
    private readonly List<KeyTile> _tiles = new();
    private readonly List<BindingEntry> _bindingEntries = new();
    private readonly KeyboardPreview _preview = new();
    private System.Drawing.Color[] _keyColors = Array.Empty<System.Drawing.Color>();
    private (float X, float Y)[] _centers = Array.Empty<(float, float)>();
    private Color[] _previewColors = Array.Empty<Color>();

    private bool _started;
    private bool _settingsChanged;
    private bool _updatingBindings;
    private KeyTile? _selectedTile;
    private int? _launchSlot;

    private int _modeIndex;
    private int _speedIndex = 1;
    private int _brightness = 100;
    private Color _color1 = Colors.Red;
    private Color _color2 = Colors.Black;
    private Color _color3 = Colors.Black;
    private Color _paintColor = Colors.Red;
    private bool _syncAura;
    private int _profileIndex;
    private int _autoPowerOffIndex;
    private int _lowBatteryWarning;
    private int _oledBrightness = 100;
    private int _oledModeIndex;
    private int _testLayoutIndex;
    private BindingEntry? _selectedBinding;
    private string _launchPath = "";
    private string _selectedKeyCaption;

    public KeyboardViewModel(AsusKeyboard keyboard, Action<Action>? post = null, Action<Action>? background = null)
        : base(post, background)
    {
        _keyboard = keyboard;
        _oled = keyboard as Azoth;
        _supportedModes = new List<KeyboardLightingMode>(keyboard.SupportedLightingModes());
        _selectedKeyCaption = Strings.KeyBindings;

        try { keyboard.ReadProfile(); } catch { /* device busy: the stored profile is used */ }

        foreach (var mode in _supportedModes)
            ModeItems.Add(LightingModeNames.TryGetValue(mode, out var name) ? name() : mode.ToString());

        foreach (var speed in Aura.GetSpeeds().Values)
            SpeedItems.Add(speed);

        BrightnessMax = Math.Max(1, keyboard.MaxBrightness());
        _paintColor = PeripheralColors.ToAvalonia(keyboard.StoredColor);

        GridVisible = keyboard.HasPerKeyRGB();
        if (GridVisible) BuildKeyGrid();

        BuildBindingEntries();

        SetupEnergy();
        SetupOled();

        TestLayoutVisible = keyboard.TestMode && keyboard.HasPerKeyRGB();
        if (TestLayoutVisible) BuildTestLayoutSelector();

        ProfilesVisible = keyboard.HasProfiles();
        if (ProfilesVisible)
        {
            ProfileItems.Add("Default");
            for (int i = 1; i < keyboard.ProfileCount(); i++)
                ProfileItems.Add(Strings.Profile + " " + i);
        }

        BatteryVisible = keyboard.HasBattery();
        VisualizeBatteryState();

        BeginLoading();
        try { LoadSettings(); }
        finally { EndLoading(); }

        if (ProfilesVisible)
        {
            _profileIndex = Math.Clamp(keyboard.Profile, 0, ProfileItems.Count - 1);
            Raise(nameof(ProfileIndex));
        }
    }

    public AsusKeyboard Keyboard => _keyboard;

    public KeyboardTexts Texts { get; } = new();

    public override string Title => _keyboard.GetDisplayName();

    // ---------------------------------------------------------------- structure

    public bool GridVisible { get; }
    public bool ProfilesVisible { get; }
    public bool TestLayoutVisible { get; private set; }
    public bool OledVisible => _oled is not null;
    public int BrightnessMax { get; }

    public IReadOnlyList<KeyTile> Tiles => _tiles;

    /// <summary>Size of the key grid in DIPs.</summary>
    public double GridWidth { get; private set; }
    public double GridHeight { get; private set; }

    public IReadOnlyList<BindingEntry> BindingEntries => _bindingEntries;

    public ObservableCollection<string> ModeItems { get; } = new();
    public ObservableCollection<string> SpeedItems { get; } = new();
    public ObservableCollection<string> ProfileItems { get; } = new();
    public ObservableCollection<string> AutoPowerOffItems { get; } = new();
    public ObservableCollection<string> OledModeItems { get; } = new();
    public ObservableCollection<string> TestLayoutItems { get; } = new();

    public bool AutoPowerOffVisible { get; private set; }
    public bool LowBatteryWarningVisible { get; private set; }
    public bool EnergyVisible => AutoPowerOffVisible || LowBatteryWarningVisible;
    public int LowBatteryWarningMax { get; private set; } = 50;
    public int LowBatteryWarningStep { get; private set; } = 25;

    // ---------------------------------------------------------------- lighting

    public KeyboardLightingMode SelectedMode =>
        _modeIndex >= 0 && _modeIndex < _supportedModes.Count ? _supportedModes[_modeIndex] : KeyboardLightingMode.Static;

    public int ModeIndex
    {
        get => _modeIndex;
        set
        {
            if (value < 0 || !Set(ref _modeIndex, value)) return;
            VisualizeControls();
            if (!Loading) ApplySettings();
        }
    }

    public int SpeedIndex
    {
        get => _speedIndex;
        set
        {
            if (value < 0 || !Set(ref _speedIndex, value) || Loading) return;
            ApplySettings();
        }
    }

    public int Brightness { get => _brightness; set => Set(ref _brightness, value); }

    public Color Color1 { get => _color1; private set => Set(ref _color1, value); }
    public Color Color2 { get => _color2; private set => Set(ref _color2, value); }
    public Color Color3 { get => _color3; private set => Set(ref _color3, value); }

    public bool SyncAura
    {
        get => _syncAura;
        set
        {
            if (!Set(ref _syncAura, value)) return;
            VisualizeControls();
            if (Loading) return;

            PeripheralsProvider.SetKeyboardAuraSync(value);

            if (value) PeripheralsProvider.SyncKeyboardsWithAura();
            else ApplySettings();
        }
    }

    // visibility / enabled state recomputed by VisualizeControls
    public bool Color1Visible { get; private set; }
    public bool Color2Visible { get; private set; }
    public bool Color3Visible { get; private set; }
    public bool ManualEnabled { get; private set; } = true;
    public bool PaintRowVisible { get; private set; }
    public bool PaintEnabled { get; private set; }
    public bool BindingsVisible { get; private set; }
    public bool SpeedEnabled { get; private set; } = true;
    public bool AnimatedPreview { get; private set; }

    // ---------------------------------------------------------------- key painting / bindings

    public Color PaintColor { get => _paintColor; private set => Set(ref _paintColor, value); }

    public KeyTile? SelectedTile => _selectedTile;

    /// <summary>"Key bindings: Q" - the legacy label shows the selected key.</summary>
    public string SelectedKeyCaption { get => _selectedKeyCaption; private set => Set(ref _selectedKeyCaption, value); }

    public bool BindingComboEnabled => _selectedTile is not null && _selectedTile.Usage != 0;

    public BindingEntry? SelectedBinding
    {
        get => _selectedBinding;
        set
        {
            if (!Set(ref _selectedBinding, value)) return;
            OnBindingSelected();
        }
    }

    public bool LaunchVisible => _launchSlot is not null;

    public string LaunchPath
    {
        get => _launchPath;
        set
        {
            if (!Set(ref _launchPath, value ?? "")) return;
            if (_updatingBindings || _launchSlot is not { } slot) return;

            AsusKeyboard.SetLaunchCommand(slot, _launchPath);
            PeripheralHost.RegisterKeys();
        }
    }

    public ICommand FillAllCommand => new RelayCommand(FillAll);
    public ICommand ResetBindingsCommand => new RelayCommand(ResetBindings);

    // ---------------------------------------------------------------- profile / energy / oled

    public int ProfileIndex
    {
        get => _profileIndex;
        set
        {
            if (value < 0 || !Set(ref _profileIndex, value) || Loading) return;
            if (value == _keyboard.Profile) return;

            // persist pending edits while the old profile is still active on the device
            if (_settingsChanged)
            {
                if (_keyColors.Length > 0) _keyboard.StoreKeyColors(_keyColors);
                _keyboard.SaveLighting();
            }

            _settingsChanged = false;
            _keyboard.SetProfile(value);

            BeginLoading();
            try { LoadSettings(); }
            finally { EndLoading(); }

            Background(() =>
            {
                if (_keyboard.HasTransientLighting) _keyboard.ApplyStoredLighting();
                _keyboard.ReadBattery();
                Post(VisualizeEnergy);
            });
        }
    }

    public int AutoPowerOffIndex
    {
        get => _autoPowerOffIndex;
        set
        {
            if (value < 0 || !Set(ref _autoPowerOffIndex, value) || Loading) return;

            var values = Enum.GetValues(typeof(PowerOffSetting));
            if (value >= values.Length) return;
            ApplyEnergySettings(_keyboard.LowBatteryWarning, (PowerOffSetting)values.GetValue(value)!);
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

    public string LowBatteryWarningText => _lowBatteryWarning == 0 ? Strings.Never : _lowBatteryWarning + "%";

    public int OledBrightness { get => _oledBrightness; set => Set(ref _oledBrightness, value); }

    public int OledModeIndex
    {
        get => _oledModeIndex;
        set
        {
            if (value < 0 || !Set(ref _oledModeIndex, value) || Loading) return;
            ApplyOledMode(value);
        }
    }

    public int TestLayoutIndex
    {
        get => _testLayoutIndex;
        set
        {
            if (value < 0 || !Set(ref _testLayoutIndex, value) || Loading) return;

            _keyboard.ForcedLayoutName = value <= 0 ? null : TestLayoutItems[value];
            ReopenRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Test mode only: the layout changed, the window must be rebuilt.</summary>
    public event EventHandler? ReopenRequested;

    // ================================================================= lifetime

    public override void Start()
    {
        if (_started || IsDisposed) return;
        _started = true;

        _keyboard.BatteryUpdated += Keyboard_BatteryUpdated;
        _keyboard.Disconnect += Keyboard_Disconnect;
        _keyboard.ProfileChanged += Keyboard_ProfileChanged;
        _keyboard.StartEventListener();
    }

    /// <summary>Window closing: stop listening and persist the key colours painted live (legacy FormClosing).</summary>
    public override void Dispose()
    {
        if (_started)
        {
            _started = false;
            try { _keyboard.StopEventListener(); } catch { }
            _keyboard.ProfileChanged -= Keyboard_ProfileChanged;
            _keyboard.BatteryUpdated -= Keyboard_BatteryUpdated;
            _keyboard.Disconnect -= Keyboard_Disconnect;
        }

        if (_settingsChanged)
        {
            _settingsChanged = false;
            // key colors are painted live over HID, persisted to config once here
            if (_keyColors.Length > 0) _keyboard.StoreKeyColors(_keyColors);
            Background(() => _keyboard.SaveLighting());
        }

        base.Dispose();
    }

    private void Keyboard_BatteryUpdated(object? sender, EventArgs e) => Post(VisualizeBatteryState);

    private void Keyboard_Disconnect(object? sender, EventArgs e) => Post(RequestClose);

    private void Keyboard_ProfileChanged(object? sender, EventArgs e) => Post(ReloadProfile);

    private void ReloadProfile()
    {
        if (IsDisposed || !_keyboard.HasProfiles() || _profileIndex == _keyboard.Profile) return;

        BeginLoading();
        try
        {
            ProfileIndex = Math.Clamp(_keyboard.Profile, 0, ProfileItems.Count - 1);
            LoadSettings();
        }
        finally { EndLoading(); }
    }

    // ================================================================= setup

    private void SetupEnergy()
    {
        AutoPowerOffVisible = _keyboard.HasAutoPowerOff();
        if (AutoPowerOffVisible)
        {
            AutoPowerOffItems.Add("1 " + Strings.Minute);
            AutoPowerOffItems.Add("2 " + Strings.Minutes);
            AutoPowerOffItems.Add("3 " + Strings.Minutes);
            AutoPowerOffItems.Add("5 " + Strings.Minutes);
            AutoPowerOffItems.Add("10 " + Strings.Minutes);
            AutoPowerOffItems.Add(Strings.Never);
        }

        LowBatteryWarningVisible = _keyboard.HasLowBatteryWarning();
        LowBatteryWarningMax = Math.Max(1, _keyboard.LowBatteryWarningMax());
        LowBatteryWarningStep = Math.Max(1, _keyboard.LowBatteryWarningStep());
    }

    // index 0 = OLED off, the rest are the built-in animations, the last one the clock
    private void SetupOled()
    {
        if (_oled is null) return;

        OledModeItems.Add(Strings.Off);
        for (int i = 1; i <= _oled.OledAnimationCount(); i++)
            OledModeItems.Add("Animation " + i);
        OledModeItems.Add("Clock");
    }

    private void BuildTestLayoutSelector()
    {
        TestLayoutItems.Add("(device)");
        foreach (var name in AuraKeyboardLayouts.Data.Keys.OrderBy(n => n))
            TestLayoutItems.Add(name);

        _testLayoutIndex = _keyboard.ForcedLayoutName is null
            ? 0
            : Math.Max(0, TestLayoutItems.IndexOf(_keyboard.ForcedLayoutName));
    }

    private void BuildKeyGrid()
    {
        var rows = new List<KeyDef[]>(_keyboard.KeyLayout());

        if (_keyboard.MediaKeyCount() > 0)
            rows.Add(Enumerable.Range(1, _keyboard.MediaKeyCount()).Select(i => new KeyDef("M" + i)).ToArray());

        _keyColors = _keyboard.StoredKeyColors();

        double unit = KeyUnit;
        int led = 0;
        double width = 0;

        for (int row = 0; row < rows.Count; row++)
        {
            double x = 0;
            KeyTile? prev = null;

            foreach (KeyDef def in rows[row])
            {
                x += def.Gap * unit;

                // the 3 parts of a split space bar are one key with 3 LEDs
                if (prev is not null && def.Gap == 0
                    && AuraKeyboardLayouts.Legend(def.Name, _keyboard.MultiLayout) == prev.Legend)
                {
                    prev.Width = x + def.Width * unit - prev.X - KeyGap;
                    prev.Span++;
                    x += def.Width * unit;
                    led++;
                    continue;
                }

                ushort code = AuraKeyboardLayouts.Keys.GetValueOrDefault(def.Name);
                if (code == 0) code = AuraKeyboardLayouts.MediaKeys.GetValueOrDefault(def.Name);

                var tile = new KeyTile(led, def.Name, AuraKeyboardLayouts.Legend(def.Name, _keyboard.MultiLayout),
                    x, row * unit, def.Width * unit - KeyGap, def.Height * unit - KeyGap, code);
                _tiles.Add(tile);

                x += def.Width * unit;
                led++;
                prev = tile;
            }

            width = Math.Max(width, x);
        }

        GridWidth = width;
        GridHeight = rows.Count * unit;

        _centers = _tiles
            .Select(t => ((float)((t.X + t.Width / 2) / Math.Max(1, GridWidth)), (float)((t.Y + t.Height / 2) / Math.Max(1, GridHeight))))
            .ToArray();
        _previewColors = new Color[_tiles.Count];
        _preview.Reset(_tiles.Count);
    }

    private void BuildBindingEntries()
    {
        var list = _bindingEntries;
        if (!GridVisible || !_keyboard.HasKeyBindings()) return;

        var region = AuraKeyboardLayouts.RegionalLegends.GetValueOrDefault(_keyboard.MultiLayout);

        list.Add(BindingEntry.Separator("Keyboard"));
        foreach (var (name, code) in AuraKeyboardLayouts.Keys)
            list.Add(BindingEntry.Item(code, region?.GetValueOrDefault(name) ?? name));

        list.Add(BindingEntry.Separator("Multimedia"));
        foreach (var (name, code) in AuraKeyboardLayouts.MultimediaTargets)
            list.Add(BindingEntry.Item(code, name));

        list.Add(BindingEntry.Separator("Mouse"));
        foreach (var (name, code) in AuraKeyboardLayouts.MouseTargets)
            list.Add(BindingEntry.Item(code, name));

        list.Add(BindingEntry.Separator("Combos"));
        foreach (var combo in KeyCombos)
            list.Add(BindingEntry.Item(combo.Code, combo.Name, combo.Keys));

        list.Add(BindingEntry.Separator("Launch"));
        for (int slot = 0; slot < AsusKeyboard.LaunchSlots; slot++)
            list.Add(BindingEntry.Item((ushort)(AsusKeyboard.LaunchCode + slot), "Launch App " + (slot + 1),
                new[] { K("Ctrl"), K("Shift"), K("Alt"), K("F" + (slot + 1)) }));
    }

    // ================================================================= refresh

    private void LoadSettings()
    {
        SyncAura = PeripheralsProvider.IsKeyboardAuraSync;

        if (_keyColors.Length > 0) _keyColors = _keyboard.StoredKeyColors();

        int modeIndex = _supportedModes.IndexOf(_keyboard.StoredMode);
        ModeIndex = modeIndex >= 0 ? modeIndex : 0;
        SpeedIndex = Math.Clamp((int)_keyboard.StoredSpeed, 0, Math.Max(0, SpeedItems.Count - 1));
        Brightness = Math.Clamp(_keyboard.StoredBrightness, 0, BrightnessMax);
        Color1 = PeripheralColors.ToAvalonia(_keyboard.StoredColor);
        Color2 = PeripheralColors.ToAvalonia(_keyboard.StoredColor2);
        Color3 = PeripheralColors.ToAvalonia(_keyboard.StoredColor3);

        RefreshBindingMarks();
        VisualizeControls();
        VisualizeEnergy();
        VisualizeOled();
    }

    private void VisualizeControls()
    {
        bool manual = !_syncAura;
        KeyboardLightingMode mode = SelectedMode;
        bool grid = GridVisible;

        Color1Visible = _keyboard.SupportsColorSetting(mode);
        Color2Visible = _keyboard.SupportsColor2Setting(mode);
        Color3Visible = _keyboard.SupportsColor3Setting(mode);
        ManualEnabled = manual;
        SpeedEnabled = manual;

        bool paint = manual && mode == KeyboardLightingMode.Direct;
        PaintRowVisible = grid && mode == KeyboardLightingMode.Direct;
        PaintEnabled = paint;
        BindingsVisible = grid && _keyboard.HasKeyBindings();
        AnimatedPreview = grid && KeyboardPreview.IsAnimated(mode);

        RaiseVisualState();
        RefreshKeyGrid();
    }

    private void RaiseVisualState()
    {
        Raise(nameof(Color1Visible));
        Raise(nameof(Color2Visible));
        Raise(nameof(Color3Visible));
        Raise(nameof(ManualEnabled));
        Raise(nameof(SpeedEnabled));
        Raise(nameof(PaintRowVisible));
        Raise(nameof(PaintEnabled));
        Raise(nameof(BindingsVisible));
        Raise(nameof(AnimatedPreview));
        Raise(nameof(BindingComboEnabled));
    }

    private void VisualizeBatteryState()
    {
        if (!_keyboard.HasBattery()) return;

        BatteryText = _keyboard.Battery + "%";
        BatteryCharging = _keyboard.Charging;
    }

    private void VisualizeEnergy()
    {
        if (!_keyboard.HasAutoPowerOff() && !_keyboard.HasLowBatteryWarning()) return;

        BeginLoading();
        try
        {
            if (_keyboard.HasAutoPowerOff())
            {
                AutoPowerOffIndex = _keyboard.PowerOffSetting == PowerOffSetting.Never
                    ? AutoPowerOffItems.Count - 1
                    : (int)_keyboard.PowerOffSetting;
            }

            if (_keyboard.HasLowBatteryWarning())
                LowBatteryWarning = Math.Clamp(_keyboard.LowBatteryWarning, 0, LowBatteryWarningMax);
        }
        finally { EndLoading(); }
    }

    private void VisualizeOled()
    {
        if (_oled is null) return;

        BeginLoading();
        try
        {
            OledBrightness = _oled.OledBrightness < 0 ? 100 : Math.Clamp(_oled.OledBrightness, 0, 100);

            if (!_oled.OledEnabled)
            {
                OledModeIndex = 0;
                return;
            }

            if (_oled.OledClock)
            {
                OledModeIndex = OledModeItems.Count - 1;
                return;
            }

            int current = _oled.ReadOledAnimation();
            if (current < 0) current = Math.Max(_oled.OledAnimation, 0);
            OledModeIndex = current + 1;
        }
        finally { EndLoading(); }
    }

    // ================================================================= actions

    private void ApplyEnergySettings(int lowBatteryWarning, PowerOffSetting powerOff) =>
        Background(() => _keyboard.SetEnergySettings(lowBatteryWarning, powerOff));

    public void CommitLowBatteryWarning() => ApplyEnergySettings(_lowBatteryWarning, _keyboard.PowerOffSetting);

    public void CommitOledBrightness()
    {
        if (_oled is null) return;
        Background(() => _oled.SetOledBrightness(_oledBrightness));
    }

    private void ApplyOledMode(int index)
    {
        if (_oled is not { } oled) return;

        Background(() =>
        {
            if (index == 0)
            {
                oled.SetOledClock(false);
                oled.SetOledEnabled(false);
                return;
            }

            if (!oled.OledEnabled) oled.SetOledEnabled(true);

            if (index > oled.OledAnimationCount())
            {
                oled.SetOledClock(true);
            }
            else
            {
                oled.SetOledClock(false);
                oled.SetOledAnimation(index - 1);
            }
        });
    }

    private void StoreSettings()
    {
        _keyboard.StoreLighting(
            SelectedMode,
            PeripheralColors.ToDrawingOpaque(_color1),
            PeripheralColors.ToDrawingOpaque(_color2),
            PeripheralColors.ToDrawingOpaque(_color3),
            (AuraSpeed)Math.Max(0, _speedIndex),
            _brightness);
        if (_keyColors.Length > 0) _keyboard.StoreKeyColors(_keyColors);
        _settingsChanged = true;
    }

    private void ApplySettings()
    {
        if (Loading || _syncAura) return;

        VisualizeControls();
        StoreSettings();

        Background(() =>
        {
            _keyboard.ApplyStoredLighting();
            _keyboard.SaveLighting();
        });
    }

    /// <summary>Brightness slider released.</summary>
    public void CommitBrightness()
    {
        if (Loading) return;

        StoreSettings();

        if (_syncAura) Background(() => _keyboard.SyncFromLaptopAura());
        else Background(() =>
        {
            _keyboard.ApplyStoredLighting();
            _keyboard.SaveLighting();
        });
    }

    /// <summary>A lighting colour (1 = colour, 2, 3 = background) was picked.</summary>
    public void SetColor(int which, Color color)
    {
        switch (which)
        {
            case 2: Color2 = color; break;
            case 3: Color3 = color; break;
            default: Color1 = color; break;
        }
        ApplySettings();
    }

    /// <summary>The colour used by the key tiles / fill all.</summary>
    public void SetPaintColor(Color color) => PaintColor = color;

    private void FillAll()
    {
        if (_syncAura || SelectedMode != KeyboardLightingMode.Direct) return;

        Array.Fill(_keyColors, PeripheralColors.ToDrawingOpaque(_paintColor));
        _settingsChanged = true;
        RefreshKeyGrid();

        System.Drawing.Color[] colors = (System.Drawing.Color[])_keyColors.Clone();
        Background(() => _keyboard.SetLedColors(colors));
    }

    private void ResetBindings()
    {
        _settingsChanged = true;
        Background(() =>
        {
            _keyboard.ResetAllBindings();
            Post(() =>
            {
                RefreshBindingMarks();
                if (_selectedTile is not null) SelectKey(_selectedTile);
            });
        });
    }

    // ----------------------------------------------------------------- keys

    public void SelectKey(KeyTile tile)
    {
        if (_selectedTile is not null) _selectedTile.Selected = false;
        _selectedTile = tile;
        tile.Selected = true;

        SelectedKeyCaption = Strings.KeyBindings + ": " + tile.Legend;
        Raise(nameof(SelectedTile));
        Raise(nameof(BindingComboEnabled));

        bool known = tile.Usage != 0;

        _updatingBindings = true;
        try
        {
            SelectedBinding = null;
            if (known)
            {
                ushort current = _keyboard.GetKeyBinding(tile.Usage);
                if (current == 0) current = tile.Usage;
                SelectedBinding = _bindingEntries.FirstOrDefault(e => !e.IsSeparator && e.Code == current);
                VisualizeLaunchPath(current);
            }
            else
            {
                VisualizeLaunchPath(0);
            }
        }
        finally
        {
            _updatingBindings = false;
        }
    }

    /// <summary>Click on a key: select it and, in custom mode, paint it with the paint colour.</summary>
    public void ClickKey(KeyTile tile)
    {
        SelectKey(tile);

        if (_syncAura) return;
        if (SelectedMode != KeyboardLightingMode.Direct) return;

        System.Drawing.Color paint = PeripheralColors.ToDrawingOpaque(_paintColor);
        bool changed = false;
        for (int i = 0; i < tile.Span; i++)
        {
            int led = tile.Led + i;
            if (led < _keyColors.Length && _keyColors[led].ToArgb() != paint.ToArgb())
            {
                _keyColors[led] = paint;
                changed = true;
            }
        }

        if (!changed) return;

        tile.BorderColor = _paintColor;
        _settingsChanged = true;

        System.Drawing.Color[] colors = (System.Drawing.Color[])_keyColors.Clone();
        Background(() => _keyboard.SetLedColors(colors));
    }

    private void OnBindingSelected()
    {
        if (_updatingBindings || _selectedTile is null) return;

        if (_selectedBinding is { IsSeparator: true } heading)
        {
            // step forward to the first real item of the group
            int next = _bindingEntries.IndexOf(heading) + 1;
            if (next < _bindingEntries.Count && !_bindingEntries[next].IsSeparator)
                SelectedBinding = _bindingEntries[next];
            return;
        }

        if (_selectedBinding is { } item && _selectedTile.Usage != 0)
        {
            ushort source = _selectedTile.Usage;
            KeyTile bound = _selectedTile;

            _settingsChanged = true;

            _updatingBindings = true;
            try { VisualizeLaunchPath(item.Code); }
            finally { _updatingBindings = false; }

            Background(() =>
            {
                if (item.ComboKeys is null) _keyboard.SetKeyBinding(source, item.Code);
                else _keyboard.SetKeyCombo(source, item.Code, item.ComboKeys);
                _keyboard.SaveLighting();
                Post(() => bound.Bound = _keyboard.GetKeyBinding(source) != 0);
            });
        }
    }

    private void VisualizeLaunchPath(ushort code)
    {
        int slot = code - AsusKeyboard.LaunchCode;
        bool launch = code != 0 && slot >= 0 && slot < AsusKeyboard.LaunchSlots;

        _launchSlot = launch ? slot : null;
        bool was = _updatingBindings;
        _updatingBindings = true;
        try { LaunchPath = launch ? AsusKeyboard.LaunchCommand(slot) : ""; }
        finally { _updatingBindings = was; }
        Raise(nameof(LaunchVisible));
    }

    private void RefreshBindingMarks()
    {
        foreach (KeyTile tile in _tiles)
            if (tile.Usage != 0) tile.Bound = _keyboard.GetKeyBinding(tile.Usage) != 0;
    }

    // ----------------------------------------------------------------- key grid colours

    private Color DisplayColorForKey(int led)
    {
        KeyboardLightingMode mode = SelectedMode;
        if (mode == KeyboardLightingMode.Direct)
            return led < _keyColors.Length ? PeripheralColors.ToAvalonia(_keyColors[led]) : Colors.Gray;
        if (_keyboard.SupportsColorSetting(mode)) return _color1;
        return Colors.Gray;
    }

    private void RefreshKeyGrid()
    {
        foreach (KeyTile tile in _tiles)
            tile.BorderColor = DisplayColorForKey(tile.Led);
    }

    /// <summary>One step of the animated preview (the window calls this every 120 ms while <see cref="AnimatedPreview"/>).</summary>
    public void PreviewTick()
    {
        if (_tiles.Count == 0 || !AnimatedPreview) return;

        KeyboardLightingMode mode = SelectedMode;
        Color bg = _keyboard.SupportsColor3Setting(mode) ? _color3 : Colors.Black;

        _preview.Tick(mode, (AuraSpeed)Math.Max(0, _speedIndex), _color1, _color2, bg, _centers, _previewColors);

        for (int i = 0; i < _tiles.Count; i++)
            _tiles[i].BorderColor = _previewColors[i];
    }
}
