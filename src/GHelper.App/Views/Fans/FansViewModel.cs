using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using GHelper.App.Theme;
using GHelper.App.ViewModels;
using GHelper.Properties;

namespace GHelper.App.Views.Fans;

/// <summary>One fan curve of the chart column (legacy chartCPU / chartGPU / chartMid / chartXGM + their series).</summary>
internal sealed class FanCurveModel : ObservableObject
{
    private bool _isVisible;
    private bool _isActive = true;
    private string _title = "";

    public FanCurveModel(AsusFan device, Color activeColor)
    {
        Device = device;
        ActiveColor = activeColor;
    }

    public AsusFan Device { get; }
    public Color ActiveColor { get; }

    /// <summary>8 points: X = temperature (C), Y = fan speed (percent).</summary>
    public (double x, double y)[] Points { get; set; } = new (double, double)[8];

    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }

    /// <summary>Coloured while the curve is applied (auto apply on), gray otherwise (legacy series colour).</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (Set(ref _isActive, value)) Raise(nameof(LineBrush));
        }
    }

    public string Title { get => _title; set => Set(ref _title, value); }

    public IBrush LineBrush => new SolidColorBrush(IsActive ? ActiveColor : Colors.Gray);

    /// <summary>Raised when <see cref="Points"/> were replaced from the model (load / reset), not for user edits.</summary>
    public event Action? PointsReloaded;

    internal void RaisePointsReloaded() => PointsReloaded?.Invoke();
}

/// <summary>
/// "Fans and Power" window state and rules - the port of app/Fans.cs without the WinForms controls. Slider values keep
/// the legacy semantics: the Scroll handlers (save to the mode config, keep SPL/sPPT/fPPT ordered ...) run only for
/// changes made by the user; programmatic loads happen "quietly".
/// </summary>
internal sealed class FansViewModel : ObservableObject
{
    public const int TempMin = 20;
    public const int TempMax = 110;
    public const int FanMaxPercent = 100;

    private static readonly string[] BoostItems =
    {
        "Disabled", "Enabled", "Aggressive", "Efficient Enabled", "Efficient Aggressive",
        "Aggressive at Guaranteed", "Efficient at Guaranteed",
    };

    private static bool _fanRpm = true; // legacy: static, not persisted

    private readonly IFansBackend _b;
    private int _quiet;
    private bool _clampFanDots;
    private int _gpuPowerBase;
    private bool _gpuVisible = true;

    // ---- navigation ----
    private int _tab;
    private bool _advancedButtonVisible;
    private bool _gpuButtonVisible = true;

    // ---- modes ----
    private ChoiceItem? _selectedMode;
    private bool _isCustom;
    private bool _isRenaming;
    private string _modeName = "";

    // ---- power limits ----
    private bool _totalVisible, _slowVisible, _fastVisible, _cpuVisible;
    private bool _crossVisible, _gpuToCpuVisible, _cpuTempVisible, _powerTitleVisible;
    private string _totalTitle = "SPL", _slowTitle = "SPPT", _fastTitle = "FPPT", _cpuTitle = "CPU";
    private int _total, _slow, _fast, _cpu, _cross, _gpuToCpu, _cpuTemp;
    private bool _applyPower, _applyFans;

    // ---- power plan ----
    private int _boostIndex;
    private PowerModeItem? _selectedPowerMode;
    private bool _powerModeEnabled = true;

    // ---- advanced (PawnIO) ----
    private bool _pawnVisible, _downloadVisible, _uvVisible, _iGpuUvVisible, _uvTitleVisible, _riskyVisible;
    private int _uv, _iGpuUv, _temp;
    private bool _applyUV, _applyUVEnabled;
    private string _riskyText = Strings.UndervoltingRisky;

    // ---- gpu ----
    private string _gpuTitle = Strings.GPUSettings;
    private int _gpuCore, _gpuMemory, _gpuClockLimit, _gpuBoost, _gpuTemp, _gpuPower;
    private bool _gpuBoostVisible = true, _gpuTempVisible = true, _gpuPowerVisible;
    private int _gpuPowerMax;

    // ---- hysteresis / status ----
    private bool _hysteresisVisible;
    private int _hysteresisUp = 3, _hysteresisDown = 3;
    private string _resultText = "";
    private bool _hint;
    private bool _calibrateEnabled = true;
    private bool _calibrateVisible = true;
    private string _windowTitle = Strings.FansAndPower;

    public FansViewModel(IFansBackend backend)
    {
        _b = backend;

        Cpu = new FanCurveModel(AsusFan.CPU, AppColors.Standard);
        Gpu = new FanCurveModel(AsusFan.GPU, AppColors.Turbo);
        Mid = new FanCurveModel(AsusFan.Mid, AppColors.Eco);
        Xgm = new FanCurveModel(AsusFan.XGM, AppColors.Orange);
        Curves = new[] { Cpu, Gpu, Mid, Xgm };
        Cpu.IsVisible = Gpu.IsVisible = true;

        // ranges are fixed for the lifetime of the window (AsusACPI.Min*/Max* statics, legacy constructor)
        PowerTotalRange = (AsusACPI.MinTotal, AsusACPI.MaxTotal);
        PowerCpuRange = (AsusACPI.MinCPU, AsusACPI.MaxCPU);
        CrossLoadRange = (AsusACPI.MinCrossLoad, AsusACPI.MaxCrossLoad);
        GpuToCpuRange = (AsusACPI.MinGPUtoCPU, AsusACPI.MaxGPUtoCPU);
        CpuTempRange = (AsusACPI.MinCPUTemp, AsusACPI.MaxCPUTemp);
        GpuClockRange = (backend.GpuClockMin, backend.GpuClockMax);
        GpuCoreRange = (backend.GpuCoreMin, backend.GpuCoreMax);
        GpuMemoryRange = (backend.GpuMemoryMin, backend.GpuMemoryMax);
        GpuBoostRange = (AsusACPI.MinGPUBoost, AsusACPI.MaxGPUBoost);
        GpuTempRange = (AsusACPI.MinGPUTemp, AsusACPI.MaxGPUTemp);
        GpuPowerRange = (AsusACPI.MinGPUPower, AsusACPI.MaxGPUPower);
        _gpuPowerMax = AsusACPI.MaxGPUPower;
        UvRange = (backend.MinCpuUV, backend.MaxCpuUV);
        IGpuUvRange = (backend.MinIGpuUV, backend.MaxIGpuUV);
        TempRange = (backend.MinCpuTemp, backend.DefaultCpuTemp);

        _clampFanDots = backend.IsClampFanDots;

        PowerModes = new ObservableCollection<PowerModeItem>(backend.PowerModes);
        BoostModes = BoostItems;

        ShowCpuCommand = new RelayCommand(() => SelectTab(0));
        ShowGpuCommand = new RelayCommand(() => SelectTab(1));
        ShowAdvancedCommand = new RelayCommand(() => SelectTab(2));
        AddModeCommand = new RelayCommand(AddMode);
        RemoveModeCommand = new RelayCommand(RemoveMode);
        RenameModeCommand = new RelayCommand(RenameToggle);
        ResetCommand = new RelayCommand(ResetToDefaults);
        CalibrateCommand = new RelayCommand(Calibrate);
        ApplyAdvancedCommand = new RelayCommand(ApplyAdvanced);
        ReadLimitsCommand = new RelayCommand(ReadLimits);
        DownloadCommand = new RelayCommand(() => _b.OpenUrl("https://pawnio.eu/"));

        InitCpuName();
        InitAll();
        _ = InitGpuAsync();
    }

    // ================= static data for the view =================

    public FanCurveModel Cpu { get; }
    public FanCurveModel Gpu { get; }
    public FanCurveModel Mid { get; }
    public FanCurveModel Xgm { get; }
    public FanCurveModel[] Curves { get; }

    public (int min, int max) PowerTotalRange { get; }
    public (int min, int max) PowerCpuRange { get; }
    public (int min, int max) CrossLoadRange { get; }
    public (int min, int max) GpuToCpuRange { get; }
    public (int min, int max) CpuTempRange { get; }
    public (int min, int max) GpuClockRange { get; }
    public (int min, int max) GpuCoreRange { get; }
    public (int min, int max) GpuMemoryRange { get; }
    public (int min, int max) GpuBoostRange { get; }
    public (int min, int max) GpuTempRange { get; }
    public (int min, int max) GpuPowerRange { get; private set; }
    public (int min, int max) UvRange { get; }
    public (int min, int max) IGpuUvRange { get; }
    public (int min, int max) TempRange { get; }
    public int GpuToCpuStep => AsusACPI.StepGPUtoCPU;

    public ObservableCollection<ChoiceItem> Modes { get; } = new();
    public ObservableCollection<PowerModeItem> PowerModes { get; }
    public string[] BoostModes { get; }

    public ICommand ShowCpuCommand { get; }
    public ICommand ShowGpuCommand { get; }
    public ICommand ShowAdvancedCommand { get; }
    public ICommand AddModeCommand { get; }
    public ICommand RemoveModeCommand { get; }
    public ICommand RenameModeCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand CalibrateCommand { get; }
    public ICommand ApplyAdvancedCommand { get; }
    public ICommand ReadLimitsCommand { get; }
    public ICommand DownloadCommand { get; }

    // ---- captions (legacy: set from Properties.Strings in the constructor, or literal designer text) ----
    public string PowerLimitsCaption => Strings.PowerLimits;
    public string ApplyPowerCaption => Strings.ApplyPowerLimits;
    public string FanCurvesCaption => "BIOS " + Strings.FanCurves;
    public string BoostCaption => Strings.CPUBoost;
    public string ResetCaption => Strings.FactoryDefaults;
    public string ApplyFansCaption => Strings.ApplyFanCurve;
    public string CalibrateCaption => Strings.Calibrate;
    public string ClampCaption => Strings.ClampToGrid;
    public string GpuCoreTitle => Strings.GPUCoreClockOffset;
    public string GpuMemoryTitle => Strings.GPUMemoryClockOffset;
    public string GpuBoostTitle => Strings.GPUBoost;
    public string GpuTempTitle => Strings.GPUTempTarget;
    public string GpuPowerTitle => Strings.GPUPower;
    public string GpuClockTitle => "Core Clock Limit";
    public string ApplyCaption => Strings.Apply;
    public string AutoApplyCaption => Strings.AutoApply;
    public string ReadLimitsCaption => Strings.ReadLimits;
    public string DownloadCaption => Strings.InstallPawnIODriver;
    public string HysteresisUpCaption => Strings.HysteresisUp;
    public string HysteresisDownCaption => Strings.HysteresisDown;
    public string PowerModeCaption => "Windows Power Mode";
    public string UndervoltingCaption => "Undervolting";
    public string UvTitle => "CPU";
    public string IGpuUvTitle => "iGPU";
    public string TempLimitCaption => "Temp Limit";
    public string TempTitle => "CPU Temp Limit";
    public string CrossLoadTitle => "Cross Loading Processor Power";
    public string GpuToCpuTitle => "GPU to CPU Dynamic Boost";
    public string CpuTempTitle => "CPU Temperature Limit";
    public string ModeNamePlaceholder => Strings.Custom;

    public string WindowTitle { get => _windowTitle; private set => Set(ref _windowTitle, value); }

    // ================= navigation =================

    public int Tab
    {
        get => _tab;
        private set
        {
            if (!Set(ref _tab, value)) return;
            Raise(nameof(IsCpuTab));
            Raise(nameof(IsGpuTab));
            Raise(nameof(IsAdvancedTab));
        }
    }

    public bool IsCpuTab => _tab != 1 && _tab != 2;
    public bool IsGpuTab => _tab == 1;
    public bool IsAdvancedTab => _tab == 2;

    public bool GpuButtonVisible { get => _gpuButtonVisible; private set => Set(ref _gpuButtonVisible, value); }
    public bool AdvancedButtonVisible { get => _advancedButtonVisible; private set => Set(ref _advancedButtonVisible, value); }

    /// <summary>Legacy ToggleNavigation: 0 CPU/power, 1 GPU, 2 advanced. Unknown values show the first tab.</summary>
    public void SelectTab(int index)
    {
        Tab = index is 1 or 2 ? index : 0;
    }

    // ================= modes =================

    public ChoiceItem? SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (!Set(ref _selectedMode, value)) return;
            if (_quiet > 0 || value is null) return;
            if (value.Key == _b.CurrentMode) return;
            _b.SetPerformanceMode(value.Key);
        }
    }

    public bool IsCustomMode { get => _isCustom; private set { if (Set(ref _isCustom, value)) { Raise(nameof(RenameVisible)); } } }
    public bool RenameVisible => _isCustom;
    public bool RemoveVisible => _isCustom;
    public bool IsRenaming
    {
        get => _isRenaming;
        private set
        {
            if (Set(ref _isRenaming, value)) Raise(nameof(ModePickerVisible));
        }
    }

    public bool ModePickerVisible => !_isRenaming;

    /// <summary>Text of the mode name editor (legacy: the combo box switched to DropDown style and saved on every key).</summary>
    public string ModeName
    {
        get => _modeName;
        set
        {
            if (!Set(ref _modeName, value)) return;
            if (_quiet > 0 || !_isRenaming || !_b.IsCurrentCustom) return;
            _b.SetCurrentModeName(value);
        }
    }

    private void FillModes(bool refreshContextMenu = true)
    {
        using (Quiet())
        {
            Modes.Clear();
            foreach (var m in _b.GetModes()) Modes.Add(m);
            IsRenaming = false;
            SelectCurrentMode();
        }

        if (refreshContextMenu) _b.RefreshContextMenu();
    }

    private void SelectCurrentMode()
    {
        int mode = _b.CurrentMode;
        SelectedMode = Modes.FirstOrDefault(m => m.Key == mode);
        IsCustomMode = _b.IsCurrentCustom;
    }

    public void InitMode()
    {
        using (Quiet())
        {
            // the list is filled by FillModes after add / remove; re-read it if it is out of date
            if (Modes.All(m => m.Key != _b.CurrentMode)) FillModes(false);
            SelectCurrentMode();
            if (!_isRenaming) ModeName = SelectedMode?.Text ?? "";
        }
    }

    private void AddMode()
    {
        int mode = _b.AddCustomMode();
        FillModes();
        _b.SetPerformanceMode(mode);
    }

    private void RemoveMode()
    {
        int mode = _b.CurrentMode;
        if (!_b.IsCurrentCustom) return;

        _b.RemoveCustomMode(mode);
        FillModes();
        _b.SetPerformanceMode(AsusACPI.PerformanceBalanced);
    }

    /// <summary>Legacy RenameToggle: first call starts editing the name, second one commits and re-reads the list.</summary>
    public void RenameToggle()
    {
        if (!_isRenaming)
        {
            using (Quiet()) ModeName = SelectedMode?.Text ?? "";
            IsRenaming = true;
            return;
        }

        FillModes();
    }

    // ================= power limits =================

    public bool TotalVisible { get => _totalVisible; private set => Set(ref _totalVisible, value); }
    public bool SlowVisible { get => _slowVisible; private set => Set(ref _slowVisible, value); }
    public bool FastVisible { get => _fastVisible; private set => Set(ref _fastVisible, value); }
    public bool CpuLimitVisible { get => _cpuVisible; private set => Set(ref _cpuVisible, value); }
    public bool CrossVisible { get => _crossVisible; private set => Set(ref _crossVisible, value); }
    public bool GpuToCpuVisible { get => _gpuToCpuVisible; private set => Set(ref _gpuToCpuVisible, value); }
    public bool CpuTempLimitVisible { get => _cpuTempVisible; private set => Set(ref _cpuTempVisible, value); }

    /// <summary>"Power Limits" header + the apply checkbox (legacy panelTitleCPU / panelApplyPower).</summary>
    public bool PowerSectionVisible { get => _powerTitleVisible; private set => Set(ref _powerTitleVisible, value); }

    public string TotalTitle { get => _totalTitle; private set => Set(ref _totalTitle, value); }
    public string SlowTitle { get => _slowTitle; private set => Set(ref _slowTitle, value); }
    public string FastTitle { get => _fastTitle; private set => Set(ref _fastTitle, value); }
    public string CpuLimitTitle { get => _cpuTitle; private set => Set(ref _cpuTitle, value); }

    public int Total { get => _total; set { if (Set(ref _total, value)) { Raise(nameof(TotalText)); if (_quiet == 0) OnTotalScroll(); } } }
    public int Slow { get => _slow; set { if (Set(ref _slow, value)) { Raise(nameof(SlowText)); if (_quiet == 0) OnSlowScroll(); } } }
    public int Fast { get => _fast; set { if (Set(ref _fast, value)) { Raise(nameof(FastText)); if (_quiet == 0) OnFastScroll(); } } }
    public int CpuLimit { get => _cpu; set { if (Set(ref _cpu, value)) { Raise(nameof(CpuLimitText)); if (_quiet == 0) OnCpuScroll(); } } }
    public int CrossLoad { get => _cross; set { if (Set(ref _cross, value)) { Raise(nameof(CrossLoadText)); if (_quiet == 0) OnCrossScroll(); } } }
    public int GpuToCpu { get => _gpuToCpu; set { if (Set(ref _gpuToCpu, value)) { Raise(nameof(GpuToCpuText)); if (_quiet == 0) OnCrossScroll(); } } }
    public int CpuTempLimit { get => _cpuTemp; set { if (Set(ref _cpuTemp, value)) { Raise(nameof(CpuTempLimitText)); if (_quiet == 0) OnCrossScroll(); } } }

    public string TotalText => _total + "W";
    public string SlowText => _slow + "W";
    public string FastText => _fast + "W";
    public string CpuLimitText => _cpu + "W";
    public string CrossLoadText => _cross + "W";
    public string GpuToCpuText => _gpuToCpu + "W";
    public string CpuTempLimitText => _b.FormatTemp(_cpuTemp);

    public bool ApplyPower
    {
        get => _applyPower;
        set
        {
            if (!Set(ref _applyPower, value) || _quiet > 0) return;
            _b.SetMode("auto_apply_power", value ? 1 : 0);
            _b.SetPerformanceMode();
        }
    }

    public bool ApplyFans
    {
        get => _applyFans;
        set
        {
            if (!Set(ref _applyFans, value) || _quiet > 0) return;
            _b.SetMode("auto_apply", value ? 1 : 0);
            _b.SetPerformanceMode();
        }
    }

    /// <summary>Slider released / key up on any power limit (legacy TrackPower_MouseUp / KeyUp).</summary>
    public void CommitPower() => Task.Run(() => _b.AutoPower());

    public void InitPower()
    {
        bool modeA = _b.IsSupported(AsusACPI.PPT_APUA0) || _b.IsAmd;
        bool modeB0 = _b.IsAllAmdPPT;
        bool modeC1 = _b.IsSupported(AsusACPI.PPT_APUC1);

        TotalVisible = modeA;
        CpuLimitVisible = modeB0;
        CrossVisible = _b.IsSupported(AsusACPI.PPT_CROSS9F);
        GpuToCpuVisible = _b.IsSupported(AsusACPI.PPT_GPUCPU9C);
        CpuTempLimitVisible = _b.IsSupported(AsusACPI.PPT_TEMP9E);
        PowerSectionVisible = modeA || modeB0 || modeC1;

        // All AMD version has B0 but doesn't have C0 (Nvidia GPU) settings
        if (modeB0)
        {
            TotalTitle = "Platform (CPU + GPU)";
            CpuLimitTitle = "CPU";
            FastVisible = false;
            SlowVisible = false;
        }
        else
        {
            SlowVisible = modeA;

            if (_b.IsAmd)
            {
                TotalTitle = "SPL (CPU sustained)";
                SlowTitle = "sPPT (CPU long boost)";
                FastTitle = "fPPT (CPU short boost)";
                FastVisible = modeC1;
            }
            else
            {
                TotalTitle = "PL1 (CPU sustained)";
                SlowTitle = "PL2 (CPU long boost)";
                FastVisible = false;
            }
        }

        int limitTotal = _b.GetMode("limit_total", AsusACPI.DefaultTotal);
        int limitSlow = _b.GetMode("limit_slow", limitTotal);
        int limitFast = _b.GetMode("limit_fast", limitTotal);
        int limitCpu = _b.GetMode("limit_cpu", AsusACPI.DefaultCPU);

        limitTotal = Math.Clamp(limitTotal, AsusACPI.MinTotal, AsusACPI.MaxTotal);
        limitCpu = Math.Clamp(limitCpu, AsusACPI.MinCPU, AsusACPI.MaxCPU);
        limitSlow = Math.Clamp(limitSlow, AsusACPI.MinTotal, AsusACPI.MaxTotal);
        limitFast = Math.Clamp(limitFast, AsusACPI.MinTotal, AsusACPI.MaxTotal);

        int cross = Math.Clamp(_b.GetMode("limit_crossload", AsusACPI.MaxCrossLoad), AsusACPI.MinCrossLoad, AsusACPI.MaxCrossLoad);
        int gpuCpu = Math.Clamp(_b.GetMode("limit_gpucpu", AsusACPI.MaxGPUtoCPU), AsusACPI.MinGPUtoCPU, AsusACPI.MaxGPUtoCPU);
        int cpuTemp = Math.Clamp(_b.GetMode("limit_cputemp", AsusACPI.MaxCPUTemp), AsusACPI.MinCPUTemp, AsusACPI.MaxCPUTemp);

        using (Quiet())
        {
            ApplyPower = _b.IsApplyPower;
            Total = limitTotal;
            Slow = limitSlow;
            CpuLimit = limitCpu;
            Fast = limitFast;
            CrossLoad = cross;
            GpuToCpu = gpuCpu;
            CpuTempLimit = cpuTemp;
        }

        SavePower();
    }

    private void SavePower()
    {
        _b.SetMode("limit_total", _total);
        _b.SetMode("limit_slow", _slow);
        _b.SetMode("limit_cpu", _cpu);
        _b.SetMode("limit_fast", _fast);

        if (_b.IsSupported(AsusACPI.PPT_CROSS9F)) _b.SetMode("limit_crossload", _cross);
        if (_b.IsSupported(AsusACPI.PPT_GPUCPU9C)) _b.SetMode("limit_gpucpu", _gpuToCpu);
        if (_b.IsSupported(AsusACPI.PPT_TEMP9E)) _b.SetMode("limit_cputemp", _cpuTemp);
    }

    private void OnTotalScroll()
    {
        using (Quiet())
        {
            if (_total > _slow) Slow = _total;
            if (_total > _fast) Fast = _total;
            if (_total < _cpu) CpuLimit = _total;
        }
        SavePower();
    }

    private void OnSlowScroll()
    {
        using (Quiet())
        {
            if (_slow < _total) Total = _slow;
            if (_slow > _fast) Fast = _slow;
        }
        SavePower();
    }

    private void OnFastScroll()
    {
        using (Quiet())
        {
            if (_fast < _slow) Slow = _fast;
            if (_fast < _total) Total = _fast;
        }
        SavePower();
    }

    private void OnCpuScroll()
    {
        using (Quiet())
        {
            if (_cpu > _total) Total = _cpu;
        }
        SavePower();
    }

    // legacy shares one handler for the three newer limits and (only) snaps the GPU to CPU boost to its 5W step
    private void OnCrossScroll()
    {
        using (Quiet())
        {
            GpuToCpu = _gpuToCpu - _gpuToCpu % AsusACPI.StepGPUtoCPU;
        }
        SavePower();
    }

    // ================= windows power plan =================

    public int BoostIndex
    {
        get => _boostIndex;
        set
        {
            if (!Set(ref _boostIndex, value) || _quiet > 0 || value < 0) return;
            if (_b.GetMode("auto_boost") != value) _b.SetCpuBoost(value);
            _b.SetMode("auto_boost", value);
        }
    }

    public PowerModeItem? SelectedPowerMode
    {
        get => _selectedPowerMode;
        set
        {
            if (!Set(ref _selectedPowerMode, value) || _quiet > 0 || value is null) return;

            _b.SetPowerMode(value.Key);
            if (_b.GetDefaultPowerMode(_b.CurrentBaseMode) != value.Key)
                _b.SetMode("powermode", value.Key);
            else
                _b.RemoveMode("powermode");
        }
    }

    public bool PowerModeEnabled { get => _powerModeEnabled; private set => Set(ref _powerModeEnabled, value); }

    public void InitPowerPlan()
    {
        int boost = _b.GetCpuBoost();
        string powerMode = _b.GetPowerMode();
        bool batterySaver = _b.IsBatterySaver;

        using (Quiet())
        {
            if (boost >= 0) BoostIndex = Math.Min(boost, BoostModes.Length - 1);

            PowerModeEnabled = !batterySaver;
            SelectedPowerMode = batterySaver
                ? PowerModes.FirstOrDefault()
                : PowerModes.FirstOrDefault(p => p.Key == powerMode);
        }
    }

    // ================= fan curves =================

    public bool ClampFanDots
    {
        get => _clampFanDots;
        set
        {
            if (!Set(ref _clampFanDots, value) || _quiet > 0) return;
            _b.SetConfig("fan_clamp", value ? 1 : 0);
        }
    }

    public int ChartCount => 2 + (Mid.IsVisible ? 1 : 0) + (Xgm.IsVisible ? 1 : 0);

    public bool FanRpm
    {
        get => _fanRpm;
        private set
        {
            if (_fanRpm == value) return;
            _fanRpm = value;
            Raise();
            AxisChanged?.Invoke();
        }
    }

    /// <summary>Axis labels changed (units toggled / calibration finished): charts repaint.</summary>
    public event Action? AxisChanged;

    /// <summary>Clicking the Y axis switches between RPM and percent (legacy ChartCPU_MouseClick).</summary>
    public void ToggleFanUnits() => FanRpm = !FanRpm;

    /// <summary>Legacy ChartYLabel.</summary>
    public string FormatSpeed(int percentage, AsusFan device, string unit = "")
    {
        if (percentage == 0) return "OFF";

        int min = _b.FanMin(device);
        int max = _b.FanMax(device);

        if (_fanRpm)
            return (200 * Math.Floor((float)(min * 100 + (max - min) * percentage) / 200)).ToString() + unit;
        return percentage + "%";
    }

    /// <summary>Y axis label of a chart (percent value 0..100).</summary>
    public string AxisYLabel(AsusFan device, double value) => FormatSpeed((int)Math.Round(value), device);

    /// <summary>X axis label: whole degrees, Fahrenheit when configured.</summary>
    public string AxisXLabel(double value) => _b.IsFahrenheit
        ? Math.Round(value * 9.0 / 5.0 + 32.0).ToString()
        : ((int)Math.Round(value)).ToString();

    /// <summary>Floating tip next to the active point (legacy UpdateChartTip / ChartCPU_MouseMove).</summary>
    public string TipText(AsusFan device, double x, double y) =>
        _b.FormatTemp(x) + ", " + FormatSpeed((int)y, device, " " + Strings.RPM);

    private string ChartTitle(AsusFan device)
    {
        string scale = _b.IsFahrenheit ? ", RPM/°F" : ", RPM/°C";
        return device switch
        {
            AsusFan.CPU => Strings.FanProfileCPU + scale,
            AsusFan.GPU => Strings.FanProfileGPU + scale,
            AsusFan.Mid => Strings.FanProfileMid + scale,
            AsusFan.XGM => "XG Mobile" + scale,
            _ => "",
        };
    }

    public void InitFans()
    {
        // Middle / system fan check
        if (!AsusACPI.IsEmptyCurve(_b.GetFanCurve(AsusFan.Mid)) || _b.IsMidFanSupported)
        {
            _b.SetConfig("mid_fan", 1);
            Mid.IsVisible = true;
            Mid.Title = ChartTitle(AsusFan.Mid);
            LoadProfile(Mid);
        }
        else
        {
            _b.SetConfig("mid_fan", 0);
            Mid.IsVisible = false;
        }

        // XG Mobile Fan check
        if (_b.IsXgConnected)
        {
            _b.SetConfig("xgm_fan", 1);
            Xgm.IsVisible = true;
            Xgm.Title = ChartTitle(AsusFan.XGM);
            LoadProfile(Xgm);
        }
        else
        {
            _b.SetConfig("xgm_fan", 0);
            Xgm.IsVisible = false;
        }

        Cpu.Title = ChartTitle(AsusFan.CPU);
        Gpu.Title = ChartTitle(AsusFan.GPU);
        LoadProfile(Cpu);
        LoadProfile(Gpu);

        bool autoFans = _b.IsApplyPower && _b.IsFanRequired;
        bool applyFans = _b.IsApplyFans;

        using (Quiet()) ApplyFans = applyFans;

        SetCurvesActive(autoFans || applyFans);
        InitHysteresis();
        Raise(nameof(ChartCount));
    }

    private void SetCurvesActive(bool active)
    {
        foreach (var c in Curves) c.IsActive = active;
    }

    /// <summary>Legacy LoadProfile: stored curve, else the BIOS / default curve; always saved back.</summary>
    private void LoadProfile(FanCurveModel curveModel, bool reset = false)
    {
        var device = curveModel.Device;
        byte[] curve = _b.GetFanConfig(device);

        if (reset || AsusACPI.IsInvalidCurve(curve))
        {
            curve = _b.GetFanCurve(device, _b.CurrentBaseMode);
            if (AsusACPI.IsInvalidCurve(curve))
                curve = _b.GetDefaultCurve(device);

            curve = _b.FixFanCurve(curve);
        }

        var points = new (double x, double y)[8];
        byte old = 0;
        for (int i = 0; i < 8; i++)
        {
            if (curve[i] == old) curve[i]++; // preventing 2 points in same spot from default asus profiles
            points[i] = (curve[i], curve[i + 8]);
            old = curve[i];
        }

        curveModel.Points = points;
        SaveProfile(curveModel);
        curveModel.RaisePointsReloaded();
    }

    private void SaveProfile(FanCurveModel curveModel)
    {
        byte[] curve = new byte[16];
        for (int i = 0; i < 8 && i < curveModel.Points.Length; i++)
        {
            curve[i] = (byte)Math.Round(curveModel.Points[i].x);
            curve[i + 8] = (byte)curveModel.Points[i].y;
        }

        _b.SetFanConfig(curveModel.Device, curve);
    }

    /// <summary>A chart finished an edit (mouse up / key): store it and let Core apply the curves (legacy Chart_Save).</summary>
    public void CommitCurve(AsusFan device, IEnumerable<(double x, double y)> points)
    {
        var model = Curves.First(c => c.Device == device);
        model.Points = points.ToArray();
        SaveProfile(model);
        _b.AutoFans();
    }

    /// <summary>Hover / drag tip below the charts (legacy FanDragHint).</summary>
    public void FanDragHint(bool show)
    {
        if (_hint == show) return;
        _hint = show;
        Raise(nameof(StatusText));
        Raise(nameof(StatusVisible));
        Raise(nameof(StatusIsHint));
    }

    public string StatusText => _hint ? Strings.FanDragAll : _resultText;
    public bool StatusVisible => _hint || _resultText.Length > 0;
    public bool StatusIsHint => _hint;

    public void InitResult()
    {
        string text = _b.FansResult ?? "";
        if (text == _resultText) return;
        _resultText = text;
        Raise(nameof(StatusText));
        Raise(nameof(StatusVisible));
    }

    public bool CalibrateVisible { get => _calibrateVisible; private set => Set(ref _calibrateVisible, value); }
    public bool CalibrateEnabled { get => _calibrateEnabled; private set => Set(ref _calibrateEnabled, value); }

    private void Calibrate()
    {
        CalibrateEnabled = false;
        _b.StartCalibration();
    }

    /// <summary>After a calibration: new fan maximums change the RPM labels (legacy InitAxis).</summary>
    public void InitAxis()
    {
        CalibrateEnabled = true;
        AxisChanged?.Invoke();
    }

    // ---- hysteresis ----

    private static readonly Func<string[]> HysteresisLabels = () => new[]
    {
        Strings.VeryLow, Strings.Low, Strings.Medium, Strings.High, Strings.VeryHigh,
    };

    public bool HysteresisVisible { get => _hysteresisVisible; private set => Set(ref _hysteresisVisible, value); }

    public int HysteresisUp
    {
        get => _hysteresisUp;
        set
        {
            if (!Set(ref _hysteresisUp, value)) return;
            Raise(nameof(HysteresisUpText));
            if (_quiet == 0) OnHysteresisScroll();
        }
    }

    public int HysteresisDown
    {
        get => _hysteresisDown;
        set
        {
            if (!Set(ref _hysteresisDown, value)) return;
            Raise(nameof(HysteresisDownText));
            if (_quiet == 0) OnHysteresisScroll();
        }
    }

    public string HysteresisUpText => HysteresisLabels()[Math.Clamp(_hysteresisUp, 1, 5) - 1];
    public string HysteresisDownText => HysteresisLabels()[Math.Clamp(_hysteresisDown, 1, 5) - 1];

    private void OnHysteresisScroll()
    {
        _b.SetMode("hysteresis_up", _hysteresisUp);
        _b.SetMode("hysteresis_down", _hysteresisDown);
    }

    public void CommitHysteresis() => _b.SetFanHysteresis(_hysteresisUp, _hysteresisDown);

    private void InitHysteresis()
    {
        var defaults = _b.GetFanHysteresis();
        if (defaults.up < 0 || defaults.down < 0)
        {
            HysteresisVisible = false;
            return;
        }

        HysteresisVisible = true;

        int up = _b.GetMode("hysteresis_up");
        int down = _b.GetMode("hysteresis_down");

        if (up < 0) up = defaults.up > 0 ? defaults.up : 3;
        if (down < 0) down = defaults.down > 0 ? defaults.down : 3;

        using (Quiet())
        {
            HysteresisUp = Math.Clamp(up, 1, 5);
            HysteresisDown = Math.Clamp(down, 1, 5);
        }
    }

    // ================= advanced: undervolting / temperature =================

    public bool PawnVisible { get => _pawnVisible; private set => Set(ref _pawnVisible, value); }
    public bool DownloadVisible { get => _downloadVisible; private set => Set(ref _downloadVisible, value); }
    public bool UvTitleVisible { get => _uvTitleVisible; private set => Set(ref _uvTitleVisible, value); }
    public bool UvVisible { get => _uvVisible; private set => Set(ref _uvVisible, value); }
    public bool IGpuUvVisible { get => _iGpuUvVisible; private set => Set(ref _iGpuUvVisible, value); }
    public bool RiskyVisible { get => _riskyVisible; private set => Set(ref _riskyVisible, value); }
    public string RiskyText { get => _riskyText; private set => Set(ref _riskyText, value); }

    public int Uv { get => _uv; set { if (Set(ref _uv, value)) { Raise(nameof(UvText)); if (_quiet == 0) AdvancedScroll(); } } }
    public int IGpuUv { get => _iGpuUv; set { if (Set(ref _iGpuUv, value)) { Raise(nameof(IGpuUvText)); if (_quiet == 0) AdvancedScroll(); } } }
    public int Temp { get => _temp; set { if (Set(ref _temp, value)) { Raise(nameof(TempText)); if (_quiet == 0) AdvancedScroll(); } } }

    public string UvText => _uv.ToString();
    public string IGpuUvText => _iGpuUv.ToString();
    public string TempText => _temp < _b.DefaultCpuTemp ? _b.FormatTemp(_temp) : "Default";

    public bool ApplyUV
    {
        get => _applyUV;
        set
        {
            if (!Set(ref _applyUV, value) || _quiet > 0) return;
            _b.SetMode("auto_uv", value ? 1 : 0);
            _b.AutoRyzen();
        }
    }

    public bool ApplyUVEnabled { get => _applyUVEnabled; private set => Set(ref _applyUVEnabled, value); }

    public void InitUV()
    {
        int cpuUv = Math.Max(UvRange.min, Math.Min(UvRange.max, _b.GetMode("cpu_uv", 0)));
        int iGpuUv = Math.Max(IGpuUvRange.min, Math.Min(IGpuUvRange.max, _b.GetMode("igpu_uv", 0)));

        int temp = _b.GetMode("cpu_temp");
        if (temp < _b.MinCpuTemp || temp > _b.DefaultCpuTemp) temp = _b.DefaultCpuTemp;

        using (Quiet())
        {
            bool apply = _b.IsApplyUV;
            ApplyUVEnabled = apply;
            ApplyUV = apply;

            Uv = cpuUv;
            IGpuUv = iGpuUv;
            Temp = temp;
        }

        VisualiseAdvanced();
        AdvancedButtonVisible = _b.IsAmd;
    }

    /// <summary>Legacy VisualiseAdvanced; also run when the window is activated (PawnIO may have been installed).</summary>
    public void VisualiseAdvanced()
    {
        bool available = _b.IsPawnAvailable;
        bool installed = available || _b.IsPawnInstalled;

        PawnVisible = installed;
        DownloadVisible = !installed;

        if (installed)
        {
            bool uv = _b.IsSupportedUV;
            UvTitleVisible = uv;
            RiskyVisible = uv;
            UvVisible = uv;
            IGpuUvVisible = _b.IsSupportedUViGPU;
        }

        Raise(nameof(UvText));
        Raise(nameof(IGpuUvText));
        Raise(nameof(TempText));
    }

    private void AdvancedScroll()
    {
        _b.SetMode("auto_uv", 0);
        using (Quiet())
        {
            ApplyUVEnabled = false;
            ApplyUV = false;
        }

        VisualiseAdvanced();

        _b.SetMode("cpu_temp", _temp);
        _b.SetMode("cpu_uv", _uv);
        _b.SetMode("igpu_uv", _iGpuUv);
    }

    private void ApplyAdvanced()
    {
        string result = _b.ApplyRyzen();
        ApplyUVEnabled = true;
        ShowRisky(result);
    }

    private void ReadLimits() => ShowRisky(_b.ReadRyzenLimits());

    private void ShowRisky(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        RiskyText = text;
        RiskyVisible = true;
    }

    public void InitCpuName()
    {
        string name = _b.CpuName;
        if (!string.IsNullOrEmpty(name)) WindowTitle = Strings.FansAndPower + " - " + name;
    }

    // ================= GPU =================

    public string GpuTitle { get => _gpuTitle; private set => Set(ref _gpuTitle, value); }
    public bool GpuBoostVisible { get => _gpuBoostVisible; private set => Set(ref _gpuBoostVisible, value); }
    public bool GpuTempVisible { get => _gpuTempVisible; private set => Set(ref _gpuTempVisible, value); }
    public bool GpuPowerVisible { get => _gpuPowerVisible; private set => Set(ref _gpuPowerVisible, value); }

    /// <summary>Maximum of the GPU power slider; raised from nvidia-smi after the window opened.</summary>
    public int GpuPowerMax { get => _gpuPowerMax; private set => Set(ref _gpuPowerMax, value); }

    public int GpuCore { get => _gpuCore; set { if (Set(ref _gpuCore, value)) { Raise(nameof(GpuCoreText)); if (_quiet == 0) OnGpuOffsetScroll(ref _gpuCore, nameof(GpuCore)); } } }
    public int GpuMemory { get => _gpuMemory; set { if (Set(ref _gpuMemory, value)) { Raise(nameof(GpuMemoryText)); if (_quiet == 0) OnGpuOffsetScroll(ref _gpuMemory, nameof(GpuMemory)); } } }
    public int GpuClockLimit { get => _gpuClockLimit; set { if (Set(ref _gpuClockLimit, value)) { Raise(nameof(GpuClockLimitText)); if (_quiet == 0) OnGpuClockScroll(); } } }
    public int GpuBoost { get => _gpuBoost; set { if (Set(ref _gpuBoost, value)) { Raise(nameof(GpuBoostText)); if (_quiet == 0) OnGpuPowerScroll(); } } }
    public int GpuTemp { get => _gpuTemp; set { if (Set(ref _gpuTemp, value)) { Raise(nameof(GpuTempText)); if (_quiet == 0) OnGpuPowerScroll(); } } }
    public int GpuPower { get => _gpuPower; set { if (Set(ref _gpuPower, value)) { Raise(nameof(GpuPowerText)); if (_quiet == 0) OnGpuPowerScroll(); } } }

    public string GpuCoreText => $"{_gpuCore} MHz";
    public string GpuMemoryText => $"{_gpuMemory} MHz";
    public string GpuBoostText => $"{_gpuBoost}W";
    public string GpuTempText => _b.FormatTemp(_gpuTemp);
    public string GpuClockLimitText => _gpuClockLimit >= GpuClockRange.max ? "Default" : $"{_gpuClockLimit} MHz";
    public string GpuPowerText => (_gpuPowerBase + _gpuPower) + "W";

    /// <summary>Core / memory offset released (legacy TrackGPUClocks_MouseUp).</summary>
    public void CommitGpuClocks() => _b.SetGpuClocks();

    /// <summary>Boost / temperature / power released (legacy TrackGPU_MouseUp).</summary>
    public void CommitGpuPower() => _b.SetGpuPower();

    private static int RoundTo5(int value) => (int)Math.Round((float)value / 5) * 5;

    private void OnGpuOffsetScroll(ref int field, string name)
    {
        int rounded = RoundTo5(field);
        if (rounded != field)
        {
            using (Quiet())
            {
                if (name == nameof(GpuCore)) GpuCore = rounded;
                else GpuMemory = rounded;
            }
        }

        _b.SetMode("gpu_core", _gpuCore);
        _b.SetMode("gpu_memory", _gpuMemory);
    }

    private void OnGpuClockScroll()
    {
        int max = RoundTo5(_gpuClockLimit);
        if (max != _gpuClockLimit)
        {
            using (Quiet()) GpuClockLimit = max;
        }

        _b.SetMode("gpu_clock_limit", _gpuClockLimit);
    }

    private void OnGpuPowerScroll()
    {
        _b.SetMode("gpu_boost", _gpuBoost);
        _b.SetMode("gpu_temp", _gpuTemp);
        if (_gpuPowerBase > 0) _b.SetMode("gpu_power", _gpuPower);
    }

    /// <summary>Legacy InitGPU: probe the NVIDIA GPU off the UI thread, then fill the GPU tab (or hide it).</summary>
    public async Task InitGpuAsync()
    {
        _gpuPowerBase = await Task.Run(() => _b.ReadGpuPowerBase());
        GpuPowerVisible = _gpuPowerBase > 0;
        Raise(nameof(GpuPowerText));

        GpuSnapshot? gpu;
        try
        {
            gpu = await Task.Run(() => _b.ReadGpu());
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.ToString());
            gpu = null;
        }

        if (gpu is null)
        {
            _gpuVisible = false;
            GpuButtonVisible = false;
            if (Tab == 1) SelectTab(0);
            return;
        }

        using (Quiet())
        {
            _gpuVisible = true;
            GpuButtonVisible = true;
            if (gpu.Name is not null) GpuTitle = gpu.Name;

            GpuClockLimit = Math.Max(Math.Min(gpu.ClockLimit, GpuClockRange.max), GpuClockRange.min);
            GpuCore = Math.Max(Math.Min(gpu.Core, GpuCoreRange.max), GpuCoreRange.min);
            GpuMemory = Math.Max(Math.Min(gpu.Memory, GpuMemoryRange.max), GpuMemoryRange.min);
            GpuBoost = Math.Max(Math.Min(gpu.Boost, GpuBoostRange.max), GpuBoostRange.min);
            GpuTemp = Math.Max(Math.Min(gpu.Temp, GpuTempRange.max), GpuTempRange.min);

            GpuBoostVisible = gpu.BoostVisible;
            GpuTempVisible = gpu.TempVisible;
        }

        await InitGpuPowerAsync();
    }

    private async Task InitGpuPowerAsync()
    {
        if (_gpuPowerBase <= 0) return;

        int max = await Task.Run(() => _b.DetectGpuPowerMax(_gpuPowerBase));
        if (max > 0)
        {
            GpuPowerRange = (AsusACPI.MinGPUPower, max);
            GpuPowerMax = max; // the view raises the slider maximum before the value arrives
        }

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        int variable = await Task.Run(() => _b.ReadGpuPowerVariable());

        int power = _b.GetMode("gpu_power");
        if (power < 0) power = variable >= 0 ? variable : GpuPowerMax;

        using (Quiet())
            GpuPower = Math.Max(Math.Min(power, GpuPowerMax), GpuPowerRange.min);
    }

    // ================= reset =================

    /// <summary>Legacy ButtonReset_Click ("Factory Defaults").</summary>
    public void ResetToDefaults()
    {
        LoadProfile(Cpu, true);
        LoadProfile(Gpu, true);

        if (_b.IsConfigOn("mid_fan")) LoadProfile(Mid, true);
        if (_b.IsConfigOn("xgm_fan")) LoadProfile(Xgm, true);

        using (Quiet())
        {
            ApplyFans = false;
            ApplyPower = false;
        }

        SetCurvesActive(false);

        _b.SetMode("auto_apply", 0);
        _b.SetMode("auto_apply_power", 0);

        using (Quiet())
        {
            Uv = _b.MaxCpuUV;
            IGpuUv = _b.MaxIGpuUV;
            Temp = _b.DefaultCpuTemp;

            CrossLoad = AsusACPI.MaxCrossLoad;
            GpuToCpu = AsusACPI.MaxGPUtoCPU;
            CpuTempLimit = AsusACPI.MaxCPUTemp;
        }

        SavePower();

        AdvancedScroll();
        _b.RemoveMode("cpu_temp");

        _b.ResetPerformanceMode();

        InitPowerPlan();

        _b.XgmReset();

        if (_gpuVisible)
        {
            using (Quiet())
            {
                GpuClockLimit = GpuClockRange.max;
                GpuCore = 0;
                GpuMemory = 0;
                GpuBoost = AsusACPI.MaxGPUBoost;
                GpuTemp = AsusACPI.MaxGPUTemp;
            }

            _b.RemoveMode("gpu_boost");
            _b.RemoveMode("gpu_temp");
            _b.RemoveMode("gpu_power");
            _b.RemoveMode("gpu_clock_limit");
            _b.RemoveMode("gpu_core");
            _b.RemoveMode("gpu_memory");

            _ = InitGpuPowerAsync();

            _b.SetGpuClocks(true);
            _b.SetGpuPower();
        }

        if (HysteresisVisible)
        {
            _b.RemoveMode("hysteresis_up");
            _b.RemoveMode("hysteresis_down");
            InitHysteresis();
        }
    }

    // ================= loading =================

    /// <summary>Legacy InitAll: re-read everything (also the target of Refresh("fans")).</summary>
    public void InitAll()
    {
        InitMode();
        InitFans();
        InitPower();
        InitPowerPlan();
        InitUV();
    }

    /// <summary>Handles IUiHost.Refresh(area) requests that concern this window.</summary>
    public void OnAreaRefreshed(string area)
    {
        switch (area)
        {
            case "fans":
                InitAll();
                break;
            case "gpu":
                _ = InitGpuAsync();
                break;
            case "fansresult":
                InitResult();
                break;
            case "fansaxis":
                InitAxis();
                break;
        }
    }

    private QuietScope Quiet() => new(this);

    private readonly struct QuietScope : IDisposable
    {
        private readonly FansViewModel _owner;

        public QuietScope(FansViewModel owner)
        {
            _owner = owner;
            owner._quiet++;
        }

        public void Dispose() => _owner._quiet--;
    }
}
