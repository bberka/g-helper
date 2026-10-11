using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using GHelper.App.ViewModels;
using GHelper.App.Views.Peripherals;
using GHelper.Peripherals;
using GHelper.Peripherals.Headset;
using GHelper.Properties;

namespace GHelper.App.Views.Headset;

/// <summary>Captions of the headset window.</summary>
public sealed class HeadsetTexts
{
    public string Lighting => Strings.Lighting;
    public string LightingMode => Strings.AuraLightingMode;
    public string SyncAura => Strings.MouseSyncWithAura;
    public string Audio => Strings.MatrixAudio;
    public string Equalizer => Strings.HeadsetEqualizer;
    public string Sidetone => Strings.HeadsetSidetone;
    public string NoiseReduction => Strings.HeadsetNoiseReduction;
    public string MicrophoneType => Strings.HeadsetMicrophoneType;
    public string VoicePrompt => Strings.HeadsetVoicePrompt;
    public string Anc => "ANC";
    public string AdaptiveAnc => Strings.HeadsetAdaptiveAnc;
    public string Dirac => "Dirac";
    public string Energy => Strings.Power;
    public string AutoPowerOff => Strings.MouseAutoPowerOff;
    public string LowBatteryWarning => Strings.MouseLowBatteryWarning;
    public string Reset => Strings.Reset;
    public string Color => Strings.Color;
}

/// <summary>One vertical slider of the 10 band equalizer.</summary>
public sealed class EqualizerBand : ObservableObject
{
    private int _value;

    public EqualizerBand(int index, string label, int max)
    {
        Index = index;
        Label = label;
        Max = max;
    }

    public int Index { get; }
    public string Label { get; }
    public int Max { get; }

    public int Value { get => _value; set => Set(ref _value, value); }
}

/// <summary>
/// State and device logic of the headset window (port of app/AsusHeadsetSettings.cs). Plain toggles and combos write
/// from their setter (guarded while the view is refreshed), sliders write on commit.
/// </summary>
public sealed class HeadsetViewModel : PeripheralViewModelBase
{
    // combo index -> value sent to the device (legacy voicePrompts)
    private static readonly int[] VoicePrompts = { 1, 2, 0 };

    private static readonly string[] BandLabels = { "32", "64", "125", "250", "500", "1K", "2K", "4K", "8K", "16K" };

    private static readonly Dictionary<HeadsetLightingMode, Func<string>> LightingModeNames = new()
    {
        { HeadsetLightingMode.Off, () => Strings.Off },
        { HeadsetLightingMode.Static, () => Strings.AuraStatic },
        { HeadsetLightingMode.Breathing, () => Strings.AuraBreathe },
        { HeadsetLightingMode.Strobing, () => Strings.AuraStrobe },
        { HeadsetLightingMode.ColorCycle, () => Strings.AuraColorCycle },
        { HeadsetLightingMode.Rainbow, () => Strings.AuraRainbow },
        { HeadsetLightingMode.MqaIndicator, () => "MQA Indicator" },
    };

    private readonly AsusHeadset _headset;
    private readonly List<HeadsetLightingMode> _lightingModes = new();
    private readonly int[] _sleepTimers;
    private readonly (string Name, byte[] Gains)[] _presets;
    private bool _started;

    private int _lightingModeIndex;
    private int _brightness;
    private Color _lightingColor = Colors.Red;
    private bool _colorVisible;
    private bool _syncAura;
    private bool _equalizerEnabled;
    private int _presetIndex = -1;
    private bool _sidetoneEnabled;
    private int _sidetone;
    private bool _noiseReductionEnabled;
    private int _noiseReductionLevel;
    private int _microphoneType;
    private int _voicePromptIndex;
    private int _ancMode;
    private bool _adaptiveAnc;
    private int _ancLevel;
    private bool _dirac;
    private int _autoPowerOffIndex;
    private int _lowBatteryWarning;

    public HeadsetViewModel(AsusHeadset headset, Action<Action>? post = null, Action<Action>? background = null)
        : base(post, background)
    {
        _headset = headset;

        _lightingModes.Add(HeadsetLightingMode.Off);
        _lightingModes.AddRange(headset.SupportedLightingModes());
        foreach (var mode in _lightingModes)
            LightingModeItems.Add(LightingModeNames.TryGetValue(mode, out var name) ? name() : mode.ToString());

        _presets = headset.EqualizerPresets;
        foreach (var preset in _presets) PresetItems.Add(preset.Name);
        for (int i = 1; i <= AsusHeadset.CustomPresets; i++) PresetItems.Add(Strings.Custom + " " + i);

        for (int i = 0; i < 10; i++)
            Bands.Add(new EqualizerBand(i, BandLabels[i], Math.Max(1, headset.MaxEqualizerGain())));

        MicrophoneTypeItems.Add(Strings.HeadsetMicBoom);
        MicrophoneTypeItems.Add(Strings.HeadsetMicInline);
        MicrophoneTypeItems.Add(Strings.HeadsetMicExternal);

        NoiseReductionItems.Add(Strings.Low);
        NoiseReductionItems.Add(Strings.Medium);
        NoiseReductionItems.Add(Strings.High);

        VoicePromptItems.Add("English");
        VoicePromptItems.Add("Chinese");
        VoicePromptItems.Add(Strings.HeadsetPromptSound);

        AncItems.Add(Strings.Off);
        AncItems.Add(Strings.On);
        AncItems.Add(Strings.HeadsetAmbient);

        AncLevelItems.Add(Strings.Auto);
        AncLevelItems.Add(Strings.Low);
        AncLevelItems.Add(Strings.Medium);
        AncLevelItems.Add(Strings.High);

        _sleepTimers = headset.SleepTimers();
        foreach (int minutes in _sleepTimers)
            AutoPowerOffItems.Add(minutes == 0 ? Strings.Never : minutes + " " + Strings.Minutes);

        // capability driven sections
        BatteryVisible = headset.HasBattery();
        LightingVisible = headset.HasRGB();
        BrightnessVisible = headset.HasBrightness();
        SidetoneMax = Math.Max(1, headset.MaxSidetone());
        EqualizerVisible = headset.HasEqualizer();
        SidetoneVisible = headset.HasSidetone();
        NoiseReductionVisible = headset.HasNoiseReduction();
        NoiseReductionLevelVisible = headset.HasNoiseReductionLevel();
        MicrophoneTypeVisible = headset.HasMicrophoneType();
        VoicePromptVisible = headset.HasVoicePrompt();
        AudioVisible = EqualizerVisible || SidetoneVisible || NoiseReductionVisible || VoicePromptVisible || MicrophoneTypeVisible;
        AncVisible = headset.HasAnc();
        DiracVisible = headset.HasDirac();
        EnergyVisible = headset.HasPowerSettings();
        LowBatteryWarningVisible = headset.HasLowBatteryWarning();
        ResetVisible = headset.HasReset();

        BeginLoading();
        try { RefreshHeadsetData(); }
        finally { EndLoading(); }
    }

    public AsusHeadset Headset => _headset;

    public HeadsetTexts Texts { get; } = new();

    public override string Title => _headset.GetDisplayName();

    // ---- capabilities
    public bool LightingVisible { get; }
    public bool BrightnessVisible { get; }
    public bool AudioVisible { get; }
    public bool EqualizerVisible { get; }
    public bool SidetoneVisible { get; }
    public int SidetoneMax { get; }
    public bool NoiseReductionVisible { get; }
    public bool NoiseReductionLevelVisible { get; }
    public bool MicrophoneTypeVisible { get; }
    public bool VoicePromptVisible { get; }
    public bool AncVisible { get; }
    public bool DiracVisible { get; }
    public bool EnergyVisible { get; }
    public bool LowBatteryWarningVisible { get; }
    public int LowBatteryWarningMax { get; } = 50;
    public int LowBatteryWarningStep { get; } = 10;
    public bool ResetVisible { get; }

    // ---- lists
    public ObservableCollection<string> LightingModeItems { get; } = new();
    public ObservableCollection<string> PresetItems { get; } = new();
    public ObservableCollection<EqualizerBand> Bands { get; } = new();
    public ObservableCollection<string> MicrophoneTypeItems { get; } = new();
    public ObservableCollection<string> NoiseReductionItems { get; } = new();
    public ObservableCollection<string> VoicePromptItems { get; } = new();
    public ObservableCollection<string> AncItems { get; } = new();
    public ObservableCollection<string> AncLevelItems { get; } = new();
    public ObservableCollection<string> AutoPowerOffItems { get; } = new();

    // ---- lighting
    public int LightingModeIndex
    {
        get => _lightingModeIndex;
        set
        {
            if (value < 0 || !Set(ref _lightingModeIndex, value) || Loading) return;

            HeadsetLightingMode mode = _lightingModes[value];
            if (mode == _headset.LightingMode) return;

            _headset.SetLighting(mode, _headset.HasBrightness() ? _brightness : _headset.Brightness, _headset.LightingColor);
            VisualizeLighting();
        }
    }

    public int Brightness { get => _brightness; set => Set(ref _brightness, value); }

    public Color LightingColor { get => _lightingColor; private set => Set(ref _lightingColor, value); }

    /// <summary>The colour button only applies to the static, breathing and strobing effects.</summary>
    public bool ColorVisible { get => _colorVisible; private set => Set(ref _colorVisible, value); }

    public bool SyncAura
    {
        get => _syncAura;
        set
        {
            if (!Set(ref _syncAura, value) || Loading) return;

            PeripheralsProvider.SetHeadsetAuraSync(value);
            if (value) PeripheralsProvider.SyncHeadsetsWithAura();
        }
    }

    public void CommitBrightness()
    {
        if (_headset.LightingMode == HeadsetLightingMode.Off) return;
        _headset.SetLighting(_headset.LightingMode, _brightness, _headset.LightingColor);
    }

    /// <summary>Colour picked in the dialog (live apply).</summary>
    public void SetLightingColor(Color color)
    {
        System.Drawing.Color c = PeripheralColors.ToDrawingOpaque(color);
        _headset.SetLighting(_headset.LightingMode, _brightness, c);
        LightingColor = color;
    }

    // ---- audio
    public bool EqualizerEnabled
    {
        get => _equalizerEnabled;
        set
        {
            if (!Set(ref _equalizerEnabled, value) || Loading) return;
            _headset.SetEqualizer(value);
        }
    }

    public int PresetIndex
    {
        get => _presetIndex;
        set
        {
            // -1: the gains match no preset (nothing selected)
            if (!Set(ref _presetIndex, value) || Loading || value < 0) return;

            _headset.SetEqualizerGains(PresetGains(value));

            BeginLoading();
            try
            {
                EqualizerEnabled = true;
                SyncBands();
            }
            finally { EndLoading(); }
        }
    }

    /// <summary>A band slider was released (legacy BandChanged).</summary>
    public void CommitBand(int band)
    {
        if (band < 0 || band >= Bands.Count) return;

        _headset.SetEqualizerBand(band, Bands[band].Value);

        int index = _presetIndex;
        BeginLoading();
        try
        {
            EqualizerEnabled = true;
            if (index >= _presets.Length) _headset.SaveCustomPreset(index - _presets.Length + 1, _headset.EqualizerGains);
            else PresetIndex = FindPreset();
        }
        finally { EndLoading(); }
    }

    public bool SidetoneEnabled
    {
        get => _sidetoneEnabled;
        set
        {
            if (!Set(ref _sidetoneEnabled, value) || Loading) return;
            _headset.SetSidetone(value, _sidetone);
        }
    }

    public int Sidetone
    {
        get => _sidetone;
        set
        {
            if (Set(ref _sidetone, value)) Raise(nameof(SidetoneText));
        }
    }

    public void CommitSidetone() => _headset.SetSidetone(_sidetoneEnabled, _sidetone);

    public bool NoiseReductionEnabled
    {
        get => _noiseReductionEnabled;
        set
        {
            if (!Set(ref _noiseReductionEnabled, value) || Loading) return;
            _headset.SetNoiseReduction(value, Math.Max(0, _noiseReductionLevel));
        }
    }

    public int NoiseReductionLevel
    {
        get => _noiseReductionLevel;
        set
        {
            if (value < 0 || !Set(ref _noiseReductionLevel, value) || Loading) return;
            _headset.SetNoiseReduction(_noiseReductionEnabled, value);
        }
    }

    public int MicrophoneType
    {
        get => _microphoneType;
        set
        {
            if (value < 0 || !Set(ref _microphoneType, value) || Loading) return;
            _headset.SetMicrophoneType(value);
        }
    }

    public int VoicePromptIndex
    {
        get => _voicePromptIndex;
        set
        {
            if (value < 0 || !Set(ref _voicePromptIndex, value) || Loading) return;
            _headset.SetVoicePrompt(VoicePrompts[value]);
        }
    }

    // ---- ANC / Dirac
    public int AncMode
    {
        get => _ancMode;
        set
        {
            if (value < 0 || !Set(ref _ancMode, value)) return;
            Raise(nameof(AncLevelEnabled));
            if (Loading) return;

            _headset.SetAnc(value);
        }
    }

    /// <summary>The ANC level only applies while ANC is on (mode 1).</summary>
    public bool AncLevelEnabled => _ancMode == 1;

    public bool AdaptiveAnc
    {
        get => _adaptiveAnc;
        set
        {
            if (!Set(ref _adaptiveAnc, value) || Loading) return;
            _headset.SetAncLevel(value, Math.Max(0, _ancLevel));
        }
    }

    public int AncLevel
    {
        get => _ancLevel;
        set
        {
            if (value < 0 || !Set(ref _ancLevel, value) || Loading) return;
            _headset.SetAncLevel(_adaptiveAnc, value);
        }
    }

    public bool Dirac
    {
        get => _dirac;
        set
        {
            if (!Set(ref _dirac, value) || Loading) return;
            _headset.SetDirac(value);
        }
    }

    // ---- energy
    public int AutoPowerOffIndex
    {
        get => _autoPowerOffIndex;
        set
        {
            if (value < 0 || !Set(ref _autoPowerOffIndex, value) || Loading) return;
            if (value >= _sleepTimers.Length) return;
            _headset.SetEnergySettings(_lowBatteryWarning, _sleepTimers[value]);
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

    public void CommitLowBatteryWarning() => _headset.SetEnergySettings(_lowBatteryWarning, _headset.SleepTimer);

    public ICommand ResetCommand => new RelayCommand(ResetToDefaults);

    public string SidetoneText => _sidetone.ToString();

    // ================================================================= lifetime

    public override void Start()
    {
        if (_started || IsDisposed) return;
        _started = true;

        _headset.Disconnect += Headset_Disconnect;
        _headset.BatteryUpdated += Headset_BatteryUpdated;
        _headset.HeadsetReadyChanged += Headset_ReadyChanged;
    }

    public override void Dispose()
    {
        if (_started)
        {
            _started = false;
            _headset.Disconnect -= Headset_Disconnect;
            _headset.BatteryUpdated -= Headset_BatteryUpdated;
            _headset.HeadsetReadyChanged -= Headset_ReadyChanged;
        }
        base.Dispose();
    }

    private void Headset_Disconnect(object? sender, EventArgs e) => Post(RequestClose);

    private void Headset_ReadyChanged(object? sender, EventArgs e)
    {
        if (!_headset.IsDeviceReady) Post(RequestClose);
    }

    private void Headset_BatteryUpdated(object? sender, EventArgs e) => Post(VisualizeBatteryState);

    // ================================================================= refresh

    /// <summary>Legacy RefreshHeadsetData: read the device again and repaint.</summary>
    public void RefreshHeadsetData()
    {
        _headset.SynchronizeDevice();

        if (!_headset.IsDeviceReady)
        {
            Logger.WriteLine(_headset.GetDisplayName() + " (GUI): Headset is not ready. Closing view.");
            RequestClose();
            return;
        }

        if (IsDisposed) return;

        BeginLoading();
        try
        {
            VisualizeBatteryState();
            VisualizeLighting();
            VisualizeAudio();
            VisualizeEnergy();
        }
        finally { EndLoading(); }
    }

    private void ResetToDefaults()
    {
        _headset.ResetToDefaults();
        RefreshHeadsetData();
    }

    private void VisualizeBatteryState()
    {
        if (!_headset.HasBattery()) return;

        BatteryText = _headset.BatteryText();
        BatteryCharging = _headset.Charging;
    }

    private void VisualizeLighting()
    {
        if (!_headset.HasRGB()) return;

        BeginLoading();
        try
        {
            SyncAura = PeripheralsProvider.IsHeadsetAuraSync;
            LightingModeIndex = Math.Max(0, _lightingModes.IndexOf(_headset.LightingMode));
            Brightness = _headset.Brightness;
            LightingColor = PeripheralColors.ToAvalonia(_headset.LightingColor);
            ColorVisible = _headset.LightingMode is HeadsetLightingMode.Static or HeadsetLightingMode.Breathing or HeadsetLightingMode.Strobing;
        }
        finally { EndLoading(); }
    }

    private void VisualizeAudio()
    {
        EqualizerEnabled = _headset.EqualizerEnabled;
        PresetIndex = FindPreset();
        SyncBands();

        SidetoneEnabled = _headset.SidetoneEnabled;
        Sidetone = _headset.Sidetone;

        NoiseReductionEnabled = _headset.NoiseReductionEnabled;
        NoiseReductionLevel = Math.Max(0, _headset.NoiseReduction);
        MicrophoneType = Math.Max(0, _headset.MicrophoneType);

        VoicePromptIndex = Math.Max(0, Array.IndexOf(VoicePrompts, _headset.VoicePrompt));
        AncMode = Math.Max(0, _headset.AncMode);
        AdaptiveAnc = _headset.AdaptiveAnc;
        AncLevel = Math.Max(0, _headset.AncLevel);
        Raise(nameof(AncLevelEnabled));
        Dirac = _headset.Dirac;
    }

    private void VisualizeEnergy()
    {
        AutoPowerOffIndex = Math.Max(0, Array.IndexOf(_sleepTimers, _headset.SleepTimer));
        LowBatteryWarning = Math.Min(_headset.LowBatteryWarning, LowBatteryWarningMax);
    }

    private void SyncBands()
    {
        for (int i = 0; i < Bands.Count && i < _headset.EqualizerGains.Length; i++)
            Bands[i].Value = _headset.EqualizerGains[i];
    }

    private byte[] PresetGains(int index) =>
        index < _presets.Length ? _presets[index].Gains : _headset.GetCustomPreset(index - _presets.Length + 1);

    private int FindPreset()
    {
        for (int i = 0; i < _presets.Length + AsusHeadset.CustomPresets; i++)
            if (PresetGains(i).SequenceEqual(_headset.EqualizerGains)) return i;
        return -1;
    }
}
