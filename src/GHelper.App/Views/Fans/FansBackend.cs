using GHelper.App.ViewModels;

namespace GHelper.App.Views.Fans;

/// <summary>GPU values read from the NVIDIA driver for the GPU tab (legacy Fans.InitGPU, run off the UI thread).</summary>
internal sealed record GpuSnapshot(
    string? Name,
    int Core,
    int Memory,
    int ClockLimit,
    int Boost,
    int Temp,
    bool BoostVisible,
    bool TempVisible);

internal sealed record PowerModeItem(string Key, string Text)
{
    public override string ToString() => Text;
}

/// <summary>
/// Everything the fans / power window needs from Core (Program.acpi, ModeControl, AppConfig, PowerNative, CpuInfo,
/// NvidiaGpuControl ...). One seam so <see cref="FansViewModel"/> holds the legacy Fans.cs rules without touching
/// static Core state: the headless tests and the design time sample run against <see cref="SampleFansBackend"/>.
/// </summary>
internal interface IFansBackend
{
    // ---- per mode configuration (AppConfig.GetMode / SetMode / RemoveMode) ----
    int GetMode(string key, int fallback = -1);
    void SetMode(string key, int value);
    void SetMode(string key, string value);
    void RemoveMode(string key);

    /// <summary>Global (not per mode) config value: mid_fan, xgm_fan, fan_clamp.</summary>
    void SetConfig(string key, int value);
    bool IsConfigOn(string key);

    byte[] GetFanConfig(AsusFan device);
    void SetFanConfig(AsusFan device, byte[] curve);
    byte[] GetDefaultCurve(AsusFan device);

    /// <summary>AsusACPI.FixFanCurve (reads the fan_clamp setting).</summary>
    byte[] FixFanCurve(byte[] curve);

    bool IsApplyPower { get; }
    bool IsApplyFans { get; }
    bool IsApplyUV { get; }
    bool IsFanRequired { get; }
    bool IsClampFanDots { get; }

    // ---- ACPI ----
    bool IsSupported(uint device);
    int DeviceGet(uint device);
    bool IsAllAmdPPT { get; }
    byte[] GetFanCurve(AsusFan device, int mode = 0);
    bool IsMidFanSupported { get; }
    bool IsXgConnected { get; }
    (int up, int down) GetFanHysteresis();
    void SetFanHysteresis(int up, int down);

    // ---- performance modes ----
    int CurrentMode { get; }
    int CurrentBaseMode { get; }
    bool IsCurrentCustom { get; }
    IReadOnlyList<ChoiceItem> GetModes();
    void RemoveCustomMode(int mode);
    int AddCustomMode();
    void SetCurrentModeName(string name);

    // ---- actions (ModeControl) ----
    void AutoPower();
    void AutoFans();
    void SetPerformanceMode(int mode = -1);
    void ResetPerformanceMode();
    void SetGpuPower();
    void SetGpuClocks(bool reset = false);
    string ApplyRyzen();
    string ReadRyzenLimits();
    void AutoRyzen();
    bool IsPawnAvailable { get; }
    bool IsPawnInstalled { get; }
    void XgmReset();
    void StartCalibration();
    void RefreshContextMenu();
    void OpenUrl(string url);

    // ---- Windows power plan ----
    IReadOnlyList<PowerModeItem> PowerModes { get; }
    int GetCpuBoost();
    void SetCpuBoost(int boost);
    string GetPowerMode();
    void SetPowerMode(string key);
    bool IsBatterySaver { get; }
    string GetDefaultPowerMode(int baseMode);

    // ---- CPU (PawnIO) ----
    bool IsAmd { get; }
    string CpuName { get; }
    bool IsSupportedUV { get; }
    bool IsSupportedUViGPU { get; }
    int MinCpuUV { get; }
    int MaxCpuUV { get; }
    int MinIGpuUV { get; }
    int MaxIGpuUV { get; }
    int MinCpuTemp { get; }
    int DefaultCpuTemp { get; }

    // ---- GPU ----
    /// <summary>null when the dGPU is off / not NVIDIA (the GPU tab stays hidden). Runs on a worker thread.</summary>
    GpuSnapshot? ReadGpu();
    int GpuClockMin { get; }
    int GpuClockMax { get; }
    int GpuCoreMin { get; }
    int GpuCoreMax { get; }
    int GpuMemoryMin { get; }
    int GpuMemoryMax { get; }
    /// <summary>Base part of the GPU TGP (AsusACPI.GPU_BASE), 0 = no GPU power slider. Read once per window.</summary>
    int ReadGpuPowerBase();
    /// <summary>Additional TGP the firmware reports now, -1 unknown. Runs on a worker thread.</summary>
    int ReadGpuPowerVariable();
    /// <summary>Raise AsusACPI.MaxGPUPower from nvidia-smi; returns the (possibly new) maximum.</summary>
    int DetectGpuPowerMax(int gpuPowerBase);

    // ---- fans / temperature ----
    int FanMin(AsusFan device);
    int FanMax(AsusFan device);
    bool IsFahrenheit { get; }
    string FormatTemp(double celsius);
    string FansResult { get; }
}
