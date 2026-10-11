using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using GHelper.App.Theme;
using GHelper.App.ViewModels;
using GHelper.Properties;

namespace GHelper.App.Views.Extra;

/// <summary>Entry of a hotkey action list (config value + display text).</summary>
internal sealed record ActionItem(string Key, string Text)
{
    public override string ToString() => Text;
}

/// <summary>A check box of the window: text, visibility, state; <see cref="Changed"/> runs for user changes only.</summary>
internal sealed class CheckItem : ObservableObject
{
    private bool _isVisible = true;
    private bool _isChecked;
    private string _text;
    private int _quiet;

    public CheckItem(string text, Action<bool>? changed = null, string? toolTip = null, bool visible = true)
    {
        _text = text;
        _isVisible = visible;
        ToolTip = toolTip;
        Changed = changed;
    }

    public string? ToolTip { get; }
    public Action<bool>? Changed { get; }
    public string? AccessibleName { get; set; }

    public string Text { get => _text; set => Set(ref _text, value); }
    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (!Set(ref _isChecked, value)) return;
            if (_quiet == 0) Changed?.Invoke(value);
        }
    }

    /// <summary>Programmatic initialisation: no side effects (legacy: set before the handler was attached).</summary>
    public void Load(bool value)
    {
        _quiet++;
        try { IsChecked = value; }
        finally { _quiet--; }
    }
}

/// <summary>One row of the key bindings table: label, action list, custom command text.</summary>
internal sealed class KeyBindingRow : ObservableObject
{
    private readonly Action<KeyBindingRow, bool> _changed;
    private string _label;
    private bool _isVisible = true;
    private ActionItem? _selected;
    private string _custom = "";
    private int _quiet;

    public KeyBindingRow(string name, string label, IEnumerable<ActionItem> actions, string? accessibleName,
        Action<KeyBindingRow, bool> changed)
    {
        Name = name;
        _label = label;
        AccessibleName = accessibleName;
        Actions = new ObservableCollection<ActionItem>(actions);
        _changed = changed;
    }

    /// <summary>Config key: m1..m5, fnf4, fnc, fnv, fne, cc, paddle.</summary>
    public string Name { get; set; }
    public string? AccessibleName { get; }
    public ObservableCollection<ActionItem> Actions { get; }

    public string Label { get => _label; set => Set(ref _label, value); }
    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }

    public ActionItem? SelectedAction
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            if (_quiet == 0 && value is not null) _changed(this, false);
        }
    }

    public string CustomText
    {
        get => _custom;
        set
        {
            if (!Set(ref _custom, value ?? "")) return;
            if (_quiet == 0) _changed(this, true);
        }
    }

    public void Load(string? action, string? custom)
    {
        _quiet++;
        try
        {
            SelectedAction = Actions.FirstOrDefault(a => a.Key == (action ?? "")) ?? Actions.First();
            CustomText = custom ?? "";
        }
        finally { _quiet--; }
    }
}

/// <summary>Awake / Sleep / Boot / Battery check boxes of one backlight zone (keyboard, logo, lightbar, lid).</summary>
internal sealed class BacklightZone : ObservableObject
{
    private bool _isVisible = true;
    private bool _batteryVisible = true;
    private bool _labelVisible = true;

    public BacklightZone(string suffix, string label, Action<bool> changed)
    {
        Suffix = suffix;
        Label = label;
        Awake = new CheckItem(Strings.Awake, changed);
        Sleep = new CheckItem(Strings.Sleep, changed);
        Boot = new CheckItem(Strings.Boot + "/" + Strings.Shutdown, changed);
        Battery = new CheckItem(Strings.Battery, changed);
        Awake.AccessibleName = Strings.Awake + " " + Label + " " + Strings.LaptopBacklight;
        Sleep.AccessibleName = Strings.Sleep + " " + Label + " " + Strings.LaptopBacklight;
        Boot.AccessibleName = Strings.Boot + "/" + Strings.Shutdown + " " + Label + " " + Strings.LaptopBacklight;
        Battery.AccessibleName = Strings.Battery + " " + Label + " " + Strings.LaptopBacklight;
    }

    /// <summary>"" keyboard, "_logo", "_bar", "_lid" (config key suffix).</summary>
    public string Suffix { get; }
    public string Label { get; }
    public CheckItem Awake { get; }
    public CheckItem Sleep { get; }
    public CheckItem Boot { get; }
    public CheckItem Battery { get; }

    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }
    public bool LabelVisible { get => _labelVisible; set => Set(ref _labelVisible, value); }
    public bool BatteryVisible { get => _batteryVisible; set { Set(ref _batteryVisible, value); Battery.IsVisible = value; } }
}

/// <summary>
/// "Extra settings" window state and rules: port of app/Extra.cs without the WinForms controls (hotkey remapping,
/// backlight behaviour, power user options, services, cores / iGPU memory ...).
/// </summary>
internal sealed class ExtraViewModel : ObservableObject
{
    private const string Empty = "--------------";

    private readonly IExtraBackend _b;
    private readonly ExtraCaps _caps;
    private int _quiet;

    private int _brightness;
    private string _speedText = "";
    private ChoiceItem? _speed;
    private decimal? _backlightTime = 60;
    private decimal? _backlightPluggedTime = 0;
    private bool _xgmVisible;
    private decimal? _hibernate = 0;
    private bool _hibernateVisible = true;

    private bool _coresVisible;
    private IReadOnlyList<string> _coresP = Array.Empty<string>();
    private IReadOnlyList<string> _coresE = Array.Empty<string>();
    private int _coreP, _coreE;
    private CoresInfo? _cores;

    private bool _apuVisible;
    private IReadOnlyList<string> _apuItems = Array.Empty<string>();
    private int _apuIndex;
    private ApuMemory? _apu;

    private bool _optimalVisible;
    private int _optimalIndex;

    private bool _acpiVisible;
    private string _acpiCommand = "110034";
    private string _acpiParam = "0x0303";
    private string _acpiTitle = "ACPI DEVS Test";

    private string _servicesText = Strings.AsusServicesRunning + ":  0";
    private string _servicesButton = Strings.Start;
    private bool _servicesEnabled = true;
    private bool _servicesRunning;

    public ExtraViewModel(IExtraBackend backend)
    {
        _b = backend;
        _caps = backend.ReadCaps();

        ResetBindingsCommand = new RelayCommand(ResetBindings);
        HelpCommand = new RelayCommand(() =>
            _b.OpenUrl("https://github.com/seerge/g-helper/wiki/Power-user-settings#custom-hotkey-actions"));
        LogCommand = new RelayCommand(() => _b.OpenUrl(_b.LogFile));
        ScanCommand = new RelayCommand(Scan);
        ApplyCoresCommand = new RelayCommand(ApplyCores);
        AcpiSendCommand = new RelayCommand(AcpiSend);
        ServicesCommand = new RelayCommand(ServicesClick);

        BuildKeyRows();
        BuildSpeeds();
        BuildZones();
        BuildChecks();
        LoadAll();
    }

    // ================= captions =================

    public string WindowTitle => Strings.ExtraSettings;
    public string KeyBindingsCaption => Strings.KeyBindings;
    public string ResetCaption => Strings.Reset;
    public string BacklightCaption => Strings.LaptopBacklight;
    public string OtherCaption => Strings.Other;
    public string SpeedCaption => Strings.AnimationSpeed;
    public string TimeoutCaption => Strings.BacklightTimeout;
    public string TimeoutPluggedName => Strings.BacklightTimeoutPlugged;
    public string TimeoutBatteryName => Strings.BacklightTimeoutBattery;
    public string OffText => Strings.Off;
    public string HibernateCaption => Strings.HibernateAfter;
    public string ApuCaption => Strings.APUMemory;
    public string CoresCaption => Strings.CPUCoresConfiguration;
    public string OptimalCaption => Strings.OptimalDisplayBrightness;
    public string ApplyCaption => "Apply";
    public string ActionPlaceholder => "action";
    public string SpeedAccessibleName => Strings.LaptopBacklight + " " + Strings.AnimationSpeed;

    public ICommand ResetBindingsCommand { get; }
    public ICommand HelpCommand { get; }
    public ICommand LogCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand ApplyCoresCommand { get; }
    public ICommand AcpiSendCommand { get; }
    public ICommand ServicesCommand { get; }

    // ================= key bindings =================

    public ObservableCollection<KeyBindingRow> KeyRows { get; } = new();

    private IEnumerable<ActionItem> ActionsFor(string name)
    {
        var actions = new List<ActionItem>
        {
            new("", Empty),
            new("volume_down", Strings.VolumeDown),
            new("volume_up", Strings.VolumeUp),
            new("backlight_down", Strings.BacklightDown),
            new("backlight_up", Strings.BacklightUp),
            new("mute", Strings.VolumeMute),
            new("screenshot", Strings.PrintScreen),
            new("play", Strings.PlayPause),
            new("aura", Strings.ToggleAura),
            new("performance", Strings.PerformanceMode),
            new("screen", Strings.ToggleScreen),
            new("lock", Strings.LockScreen),
            new("miniled", Strings.ToggleMiniled),
            new("fnlock", Strings.ToggleFnLock),
            new("brightness_down", Strings.BrightnessDown),
            new("brightness_up", Strings.BrightnessUp),
            new("visual", Strings.VisualMode),
            new("touchpad", Strings.ToggleTouchpad),
            new("touchscreen", Strings.ToggleTouchscreen),
            new("micmute", Strings.MuteMic),
            new("ghelper", Strings.OpenGHelper),
            new("overlay", Strings.Overlay),
            new("custom", Strings.Custom),
        };

        if (_caps.IsDuo)
        {
            actions.Add(new("screenpad_down", Strings.ScreenPadDown));
            actions.Add(new("screenpad_up", Strings.ScreenPadUp));
        }

        if (_caps.IsAlly) actions.Add(new("controller", "Controller Mode"));

        // the "default" entry is named after the key's own function and that action is not offered twice
        void Default(string text, string? remove = null)
        {
            actions[0] = new ActionItem("", text);
            if (remove is not null) actions.RemoveAll(a => a.Key == remove);
        }

        switch (name)
        {
            case "m1": Default(Strings.VolumeDown, "volume_down"); break;
            case "m2": Default(Strings.VolumeUp, "volume_up"); break;
            case "m3": Default(Strings.MuteMic, "micmute"); break;
            case "m4": Default(Strings.OpenGHelper, "ghelper"); break;
            case "m5": Default(Strings.PerformanceMode, "performance"); break;
            case "fnf4": Default(Strings.ToggleAura, "aura"); break;
            case "fnc": Default(Strings.ToggleFnLock, "fnlock"); break;
            case "fnv": Default(Strings.VisualMode, "visual"); break;
            case "fne": Default("Calculator"); break;
            case "paddle":
            case "cc": Default(Empty); break;
        }

        return actions;
    }

    private KeyBindingRow Row(string name, string label, string accessibleName, string? configName = null) =>
        new(configName ?? name, label, ActionsFor(configName ?? name), accessibleName, OnBindingChanged);

    private void BuildKeyRows()
    {
        var m1 = Row("m1", "M1:", "M1 Action");
        var m2 = Row("m2", "M2:", "M2 Action");
        var m3 = Row("m3", "M3:", "M3 Action");
        var m4 = Row("m4", "M4:", "M4 Action");
        var m5 = Row("m5", "M4:", "M5 Action");
        var f4 = Row("fnf4", "FN+F4:", "Fn+F4 Action");
        var fc = Row("fnc", "FN+C:", "Fn+C Action");
        var fv = Row("fnv", "FN+V:", "Fn+V Action");
        var fe = Row("fne", "FN+NmEnt:", "Fn+Numpad Action");

        m5.IsVisible = false;

        if (_caps.IsArcnm)
        {
            m3.Label = "FN+F6";
            m1.IsVisible = m2.IsVisible = m4.IsVisible = f4.IsVisible = false;
        }

        if (_caps.NoMKeys)
        {
            m1.Label = "FN+F2";
            m2.Label = "FN+F3";
            m3.Label = "FN+F4";
            m4.IsVisible = _caps.IsM4Button;
            f4.IsVisible = false;
        }

        if (_caps.IsVivoZenPro)
        {
            m1.IsVisible = m2.IsVisible = m3.IsVisible = f4.IsVisible = false;
            m4.Label = "FN+F12";
        }

        if (_caps.MediaKeys) f4.IsVisible = false;
        if (!_caps.IsTuf) fe.IsVisible = false;
        if (_caps.IsNoFnV) fv.IsVisible = false;

        if (_caps.IsAlly)
        {
            // Change text and hide irrelevant options on the ROG Ally, which is a bit of a special case piece of hardware.
            m1.IsVisible = m2.IsVisible = false;
            m3.Label = "Cmd Center";
            m4.Label = "ROG";
            f4.Label = "Back Paddles";
            fc.IsVisible = fv.IsVisible = false;
            fe.IsVisible = false; // the Ally branch never set the numpad row up

            // re-bind the Ally rows to their own config keys
            m3 = Replace(m3, "cc");
            f4 = Replace(f4, "paddle");
        }

        if (_caps.IsStrix)
        {
            m4.Label = "M5/ROG";
            m5.IsVisible = true;
        }

        foreach (var r in new[] { m1, m2, m3, m4, m5, f4, fc, fv, fe }) KeyRows.Add(r);

        KeyBindingRow Replace(KeyBindingRow old, string configName) =>
            new(configName, old.Label, ActionsFor(configName), old.AccessibleName, OnBindingChanged)
            {
                IsVisible = old.IsVisible,
            };
    }

    private void OnBindingChanged(KeyBindingRow row, bool custom)
    {
        if (_quiet > 0) return;

        // legacy: SelectedValueChanged stored the action (and re-registered M keys), TextChanged the custom command
        if (custom)
        {
            _b.SetString(row.Name + "_custom", row.CustomText);
            return;
        }

        _b.SetString(row.Name, row.SelectedAction?.Key ?? "");
        if (row.Name is "m1" or "m2" or "m3" or "m4" or "m5") _b.ApplyMKeys();
    }

    /// <summary>Legacy ButtonResetBindings_Click: only the M keys (first five rows) are cleared.</summary>
    private void ResetBindings()
    {
        var rows = KeyRows.Take(5).ToList();

        _quiet++;
        try
        {
            foreach (var row in rows) row.Load("", "");
        }
        finally { _quiet--; }

        foreach (var row in rows)
        {
            _b.SetString(row.Name, "");
            _b.SetString(row.Name + "_custom", "");
        }

        _b.ResetMKeys();
    }

    // ================= keyboard backlight =================

    public ObservableCollection<ChoiceItem> Speeds { get; } = new();

    public ChoiceItem? SelectedSpeed
    {
        get => _speed;
        set
        {
            if (!Set(ref _speed, value)) return;
            if (_quiet > 0 || value is null) return;
            _b.SetInt("aura_speed", value.Key);
            _b.ApplyAura();
        }
    }

    private void BuildSpeeds()
    {
        foreach (var (key, text) in _b.AuraSpeeds) Speeds.Add(new ChoiceItem(key, text));
        _quiet++;
        try { SelectedSpeed = Speeds.FirstOrDefault(s => s.Key == _b.AuraSpeed); }
        finally { _quiet--; }
    }

    /// <summary>Laptop backlight level 0..3 (the header slider).</summary>
    public int Brightness
    {
        get => _brightness;
        set
        {
            if (!Set(ref _brightness, value)) return;
            Raise(nameof(BrightnessAccessibleName));
            if (_quiet > 0) return;

            if (_b.IsOnBatteryPower) _b.SetInt("keyboard_brightness_ac", value);
            else _b.SetInt("keyboard_brightness", value);

            _b.ApplyBrightness(value);
        }
    }

    public string BrightnessAccessibleName => Strings.LaptopBacklight + ": " + _brightness;

    /// <summary>Hotkey / Core changed the level (legacy VisualiseBacklight): show it without applying again.</summary>
    public void VisualiseBacklight(int value)
    {
        _quiet++;
        try { Brightness = value; }
        finally { _quiet--; }
    }

    public ObservableCollection<BacklightZone> Zones { get; } = new();

    private void BuildZones()
    {
        Zones.Add(new BacklightZone("", Strings.Keyboard, OnZoneChanged));
        Zones.Add(new BacklightZone("_logo", Strings.Logo, OnZoneChanged));
        Zones.Add(new BacklightZone("_bar", Strings.Lightbar, OnZoneChanged));
        Zones.Add(new BacklightZone("_lid", Strings.Lid, OnZoneChanged));

        foreach (var z in Zones)
        {
            z.Awake.Load(_b.IsNotFalse("keyboard_awake" + z.Suffix));
            z.Battery.Load(_b.IsOnBattery("keyboard_awake" + z.Suffix));
            z.Boot.Load(_b.IsNotFalse("keyboard_boot" + z.Suffix));
            z.Sleep.Load(_b.IsNotFalse("keyboard_sleep" + z.Suffix));
        }

        var keyboard = Zones[0];
        if (!_caps.IsBacklightZones || _caps.IsArcnm)
        {
            keyboard.LabelVisible = false;
            keyboard.BatteryVisible = false;
        }

        Zones[1].IsVisible = _caps.HasLogo;
        Zones[2].IsVisible = _caps.HasLightbar;
        Zones[3].IsVisible = _caps.HasRearglow;
    }

    private void OnZoneChanged(bool _)
    {
        // legacy CheckPower_CheckedChanged writes every zone (and the shutdown_* twins of boot_*) each time
        foreach (var z in Zones)
        {
            string s = z.Suffix;
            _b.SetInt("keyboard_awake" + s, z.Awake.IsChecked ? 1 : 0);
            _b.SetInt("keyboard_boot" + s, z.Boot.IsChecked ? 1 : 0);
            _b.SetInt("keyboard_sleep" + s, z.Sleep.IsChecked ? 1 : 0);
            _b.SetInt("keyboard_shutdown" + s, z.Boot.IsChecked ? 1 : 0);
        }

        if (_caps.IsBacklightZones)
        {
            _b.SetInt("keyboard_awake_bat", Zones[0].Battery.IsChecked ? 1 : 0);
            _b.SetInt("keyboard_awake_bar_bat", Zones[2].Battery.IsChecked ? 1 : 0);
            _b.SetInt("keyboard_awake_lid_bat", Zones[3].Battery.IsChecked ? 1 : 0);
            _b.SetInt("keyboard_awake_logo_bat", Zones[1].Battery.IsChecked ? 1 : 0);
        }

        _b.ApplyAuraPower();
        if (_b.IsOldStrix) _b.ApplyAura();
    }

    public decimal? BacklightTime
    {
        get => _backlightTime;
        set
        {
            if (!Set(ref _backlightTime, value)) return;
            if (_quiet == 0) OnTimeoutChanged();
        }
    }

    public decimal? BacklightPluggedTime
    {
        get => _backlightPluggedTime;
        set
        {
            if (!Set(ref _backlightPluggedTime, value)) return;
            if (_quiet == 0) OnTimeoutChanged();
        }
    }

    private void OnTimeoutChanged()
    {
        _b.SetInt("keyboard_timeout", (int)(_backlightTime ?? 0));
        _b.SetInt("keyboard_ac_timeout", (int)(_backlightPluggedTime ?? 0));
        _b.InitBacklightTimer();
    }

    public bool XgmVisible { get => _xgmVisible; private set => Set(ref _xgmVisible, value); }
    public CheckItem Xgm { get; private set; } = null!;

    // ================= other settings =================

    public ObservableCollection<CheckItem> SettingChecks { get; } = new();

    public CheckItem Aspm { get; private set; } = null!;
    public CheckItem StandbyNetworking { get; private set; } = null!;
    public CheckItem StatusLed { get; private set; } = null!;
    public CheckItem NumberPad { get; private set; } = null!;
    public CheckItem NvPlatform { get; private set; } = null!;
    public CheckItem GpuApps { get; private set; } = null!;
    public CheckItem Usbc { get; private set; } = null!;
    public CheckItem BootSound { get; private set; } = null!;
    public CheckItem KeystoneSound { get; private set; } = null!;
    public CheckItem NoOverdrive { get; private set; } = null!;
    public CheckItem Topmost { get; private set; } = null!;
    public CheckItem Clamshell { get; private set; } = null!;
    public CheckItem Elmb { get; private set; } = null!;

    private void BuildChecks()
    {
        Xgm = new CheckItem("XG Mobile", on =>
        {
            _b.SetInt("xmg_light", on ? 1 : 0);
            _b.SetXgmLight(on);
        });

        Aspm = new CheckItem(Strings.DisablePCIeASPM, on =>
        {
            _b.SetInt("aspm", on ? 1 : 0);
            _b.SetAspm(on);
        }, Strings.DisablePCIeASPMTooltip);

        StandbyNetworking = new CheckItem(Strings.DisableStandbyNetworking, on =>
        {
            _b.SetInt("standby_networking", on ? 1 : 0);
            _b.SetStandbyNetworking(on);
        }, Strings.DisableStandbyNetworkingTooltip);

        StatusLed = new CheckItem(Strings.LEDStatusIndicators, on => _b.SetStatusLed(on), visible: false);
        NumberPad = new CheckItem("Touchpad NumberPad", on => _b.SetNumberPad(on), visible: false);
        NvPlatform = new CheckItem(Strings.StopStartNVServices, on => _b.SetInt("nv_platform", on ? 1 : 0),
            Strings.NVPlatformTooltip);
        GpuApps = new CheckItem(Strings.KillGpuApps, on => _b.SetInt("kill_gpu_apps", on ? 1 : 0));
        Usbc = new CheckItem(Strings.OptimizedUSBC, on => _b.SetInt("optimized_usbc", on ? 1 : 0));
        BootSound = new CheckItem(Strings.BootSound, on =>
        {
            int value = on ? 1 : 0;
            _b.SetBootSound(on);
            _b.SetInt("boot_sound", value);
        }, visible: false);
        KeystoneSound = new CheckItem(Strings.KeystoneSound, on => _b.SetKeystone(on), visible: false);
        NoOverdrive = new CheckItem(Strings.DisableOverdrive, on =>
        {
            _b.SetInt("no_overdrive", on ? 1 : 0);
            _b.SetNoOverdrive(on);
        });
        Topmost = new CheckItem(Strings.WindowTop, on =>
        {
            _b.SetInt("topmost", on ? 1 : 0);
            _b.SetTopmost(on);
        });
        Clamshell = new CheckItem(Strings.ToggleClamshellMode, on =>
        {
            _b.SetInt("toggle_clamshell_mode", on ? 1 : 0);
            _b.SetClamshell(on);
        }, Strings.ClamshellModeTooltip);
        Elmb = new CheckItem(Strings.ExtremeLowMotionBlur, on =>
        {
            _b.SetInt("elmb", on ? 1 : 0);
            _b.SetElmb(on);
        }, visible: false);

        // top to bottom as the legacy panel docks them
        foreach (var c in new[]
                 {
                     Aspm, StandbyNetworking, StatusLed, NumberPad, NvPlatform, GpuApps, Usbc, BootSound,
                     KeystoneSound, NoOverdrive, Topmost, Clamshell, Elmb,
                 })
            SettingChecks.Add(c);
    }

    private void LoadAll()
    {
        // ---- checks ----
        Clamshell.Load(_b.Is("toggle_clamshell_mode"));
        Topmost.Load(_b.Is("topmost"));
        NoOverdrive.Load(_b.Is("no_overdrive"));
        NoOverdrive.IsVisible = _caps.OverdriveSupported;

        int elmb = _b.GetElmb();
        Elmb.IsVisible = elmb >= 0;
        Elmb.Load(elmb == 1);

        Usbc.Load(_b.Is("optimized_usbc"));
        GpuApps.Load(_b.Is("kill_gpu_apps"));

        if (!_caps.GpuModeSupported)
        {
            GpuApps.IsVisible = false;
            Usbc.IsVisible = false;
        }

        if (_caps.IsAlly)
        {
            GpuApps.IsVisible = false;
            Usbc.IsVisible = false;
            Clamshell.IsVisible = false;
        }

        XgmVisible = _caps.XgmConnected;
        Xgm.Load(_b.GetInt("xmg_light", 1) != 0);

        int bootSound = _b.GetBootSound();
        BootSound.IsVisible = bootSound >= 0;
        if (bootSound < 0 || bootSound > ushort.MaxValue) bootSound = _b.GetInt("boot_sound", 0);
        BootSound.Load(bootSound == 1);

        int statusLed = _b.GetStatusLed();
        StatusLed.IsVisible = statusLed >= 0;
        StatusLed.Load(statusLed > 0);

        int numberPad = _b.GetNumberPad();
        NumberPad.IsVisible = numberPad >= 0;
        NumberPad.Load(numberPad == 1);

        NvPlatform.IsVisible = _caps.NvidiaGpu;
        NvPlatform.Load(_b.Is("nv_platform"));
        Aspm.Load(_b.Is("aspm"));
        StandbyNetworking.Load(_b.Is("standby_networking"));

        KeystoneSound.IsVisible = _caps.Keystone;
        KeystoneSound.Load(_b.IsKeystoneEnabled());

        // ---- backlight ----
        _quiet++;
        try
        {
            Brightness = _b.GetBacklight();
            BacklightTime = _b.GetInt("keyboard_timeout", 60);
            BacklightPluggedTime = _b.GetInt("keyboard_ac_timeout", 0);
        }
        finally { _quiet--; }

        // ---- key bindings ----
        foreach (var row in KeyRows)
            row.Load(_b.GetString(row.Name), _b.GetString(row.Name + "_custom"));

        // ---- optimal brightness ----
        int optimal = _b.GetOptimalBrightness();
        if (optimal >= 0)
        {
            OptimalVisible = true;
            _quiet++;
            try { OptimalIndex = _b.GetInt("optimal_brightness", optimal); }
            finally { _quiet--; }
        }

        InitCores();
        InitHibernate();
        InitApu();
        InitAcpiTesting();
    }

    // ---- optimal display brightness ----
    public IReadOnlyList<string> OptimalItems { get; } = new[] { Strings.Off, Strings.OnAlways, Strings.OnBattery };
    public bool OptimalVisible { get => _optimalVisible; private set => Set(ref _optimalVisible, value); }

    public int OptimalIndex
    {
        get => _optimalIndex;
        set
        {
            if (!Set(ref _optimalIndex, value)) return;
            if (_quiet == 0 && value >= 0) _b.SetOptimalBrightness(value);
        }
    }

    // ---- hibernate ----
    public bool HibernateVisible { get => _hibernateVisible; private set => Set(ref _hibernateVisible, value); }

    public decimal? HibernateAfter
    {
        get => _hibernate;
        set
        {
            if (!Set(ref _hibernate, value)) return;
            if (_quiet == 0) _b.SetHibernateAfter((int)(value ?? 0));
        }
    }

    private void InitHibernate()
    {
        try
        {
            int hibernate = _b.GetHibernateAfter();
            if (hibernate < 0 || hibernate > 3000000) hibernate = 0;
            _quiet++;
            try { HibernateAfter = hibernate; }
            finally { _quiet--; }
        }
        catch (Exception ex)
        {
            HibernateVisible = false;
            Logger.WriteLine(ex.ToString());
        }
    }

    // ---- cores ----
    public bool CoresVisible { get => _coresVisible; private set => Set(ref _coresVisible, value); }
    public IReadOnlyList<string> CoresPItems { get => _coresP; private set => Set(ref _coresP, value); }
    public IReadOnlyList<string> CoresEItems { get => _coresE; private set => Set(ref _coresE, value); }
    public int CoresPIndex { get => _coreP; set => Set(ref _coreP, value); }
    public int CoresEIndex { get => _coreE; set => Set(ref _coreE, value); }

    private void InitCores()
    {
        _cores = _b.ReadCores();
        if (_cores is null)
        {
            CoresVisible = false;
            return;
        }

        CoresPItems = _cores.PItems;
        CoresEItems = _cores.EItems;
        CoresPIndex = _cores.PIndex;
        CoresEIndex = _cores.EIndex;
        CoresVisible = true;
    }

    private void ApplyCores()
    {
        if (_cores is null) return;
        if (!_b.Confirm(Strings.AlertAPUMemoryRestartTitle, Strings.AlertAPUMemoryRestart)) return;

        _cores.Apply(Math.Max(0, _coreE), Math.Max(0, _coreP));
        _b.Restart();
    }

    // ---- iGPU memory ----
    public bool ApuVisible { get => _apuVisible; private set => Set(ref _apuVisible, value); }
    public IReadOnlyList<string> ApuItems { get => _apuItems; private set => Set(ref _apuItems, value); }

    public int ApuIndex
    {
        get => _apuIndex;
        set
        {
            if (!Set(ref _apuIndex, value)) return;
            if (_quiet > 0 || value < 0 || _apu is null) return;

            if (!_apu.Apply(value)) return;
            if (_b.Confirm(Strings.AlertAPUMemoryRestartTitle, Strings.AlertAPUMemoryRestart)) _b.Restart();
        }
    }

    private void InitApu()
    {
        _apu = _b.ReadApuMemory();
        if (_apu is null) return;

        _quiet++;
        try
        {
            ApuItems = _apu.Options;
            ApuIndex = _apu.SelectedIndex;
        }
        finally { _quiet--; }

        ApuVisible = true;
    }

    // ---- ACPI test (debug only) / scan ----
    public bool AcpiVisible { get => _acpiVisible; private set => Set(ref _acpiVisible, value); }
    public string AcpiCommand { get => _acpiCommand; set => Set(ref _acpiCommand, value); }
    public string AcpiParam { get => _acpiParam; set => Set(ref _acpiParam, value); }
    public string AcpiTitle { get => _acpiTitle; private set => Set(ref _acpiTitle, value); }

    private void InitAcpiTesting() => AcpiVisible = _caps.Debug;

    private void AcpiSend() => AcpiTitle = _b.AcpiTest(_acpiCommand, _acpiParam);

    private void Scan() => Task.Run(() => _b.OpenUrl(_b.ScanRange()));

    // ================= services =================

    public string ServicesText { get => _servicesText; private set => Set(ref _servicesText, value); }
    public string ServicesButtonText { get => _servicesButton; private set => Set(ref _servicesButton, value); }
    public bool ServicesButtonEnabled { get => _servicesEnabled; private set => Set(ref _servicesEnabled, value); }

    public bool ServicesRunning
    {
        get => _servicesRunning;
        private set
        {
            if (Set(ref _servicesRunning, value)) Raise(nameof(ServicesBrush));
        }
    }

    /// <summary>Red while Asus services run (they fight G-Helper), blue when stopped (legacy colorTurbo / colorStandard).</summary>
    public IBrush ServicesBrush => new SolidColorBrush(_servicesRunning ? AppColors.Turbo : AppColors.Standard);

    public async Task InitServicesAsync()
    {
        ServicesButtonEnabled = false;
        int count = await Task.Run(() => _b.GetRunningServicesCount());
        VisualiseServices(count);
    }

    private void VisualiseServices(int count)
    {
        ServicesRunning = count > 0;
        ServicesButtonText = count > 0 ? Strings.Stop : Strings.Start;
        ServicesText = Strings.AsusServicesRunning + ":  " + count;
        ServicesButtonEnabled = true;
    }

    /// <summary>Legacy ServiesToggle (also the target of the "--services" launch argument).</summary>
    public async Task ToggleServicesAsync()
    {
        ServicesButtonEnabled = false;

        bool running = await Task.Run(() => _b.GetRunningServicesCount() > 0);
        if (running)
        {
            ServicesText = Strings.StoppingServices + " ...";
            await Task.Run(() =>
            {
                _b.StopServices();
                _b.ReinitInput();
            });
        }
        else
        {
            ServicesText = Strings.StartingServices + " ...";
            await Task.Run(() => _b.StartServices());
        }

        await InitServicesAsync();
    }

    private void ServicesClick()
    {
        if (_b.IsAdministrator) _ = ToggleServicesAsync();
        else _b.RunAsAdmin("services");
    }

    /// <summary>Handles IUiHost.Refresh(area) requests that concern this window.</summary>
    public void OnAreaRefreshed(string area)
    {
        if (area is "keyboard" or "backlight") VisualiseBacklight(_b.GetBacklight());
    }
}
