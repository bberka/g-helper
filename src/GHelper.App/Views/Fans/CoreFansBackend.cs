using GHelper;
using GHelper.App.ViewModels;
using GHelper.Fan;
using GHelper.Gpu.NVidia;
using GHelper.Helpers;
using GHelper.Mode;
using GHelper.Platform;
using GHelper.USB;
using PawnIO;

namespace GHelper.App.Views.Fans;

/// <summary>The real thing: forwards to GHelper.Core (legacy Fans.cs called these statics directly).</summary>
internal sealed class CoreFansBackend : IFansBackend
{
    private readonly FanSensorControl _sensors = new();

    private static ModeControl Mode => Program.modeControl;
    private static AsusACPI Acpi => Program.acpi;

    // ---- configuration ----
    public int GetMode(string key, int fallback = -1) => AppConfig.GetMode(key, fallback);
    public void SetMode(string key, int value) => AppConfig.SetMode(key, value);
    public void SetMode(string key, string value) => AppConfig.SetMode(key, value);
    public void RemoveMode(string key) => AppConfig.RemoveMode(key);
    public void SetConfig(string key, int value) => AppConfig.Set(key, value);
    public bool IsConfigOn(string key) => AppConfig.Is(key);

    public byte[] GetFanConfig(AsusFan device) => AppConfig.GetFanConfig(device);
    public void SetFanConfig(AsusFan device, byte[] curve) => AppConfig.SetFanConfig(device, curve);
    public byte[] GetDefaultCurve(AsusFan device) => AppConfig.GetDefaultCurve(device);
    public byte[] FixFanCurve(byte[] curve) => AsusACPI.FixFanCurve(curve);

    public bool IsApplyPower => AppConfig.IsApplyPower();
    public bool IsApplyFans => AppConfig.IsApplyFans();
    public bool IsApplyUV => AppConfig.IsApplyUV();
    public bool IsFanRequired => AppConfig.IsFanRequired();
    public bool IsClampFanDots => AppConfig.IsClampFanDots();

    // ---- ACPI ----
    public bool IsSupported(uint device) => Acpi.IsSupported(device);
    public int DeviceGet(uint device) => Acpi.DeviceGet(device);
    public bool IsAllAmdPPT => Acpi.IsAllAmdPPT();
    public byte[] GetFanCurve(AsusFan device, int mode = 0) => Acpi.GetFanCurve(device, mode);
    public bool IsMidFanSupported => Acpi.IsMidFanSupported();
    public bool IsXgConnected => Acpi.IsXGConnected() || XGM.IsConnected();
    public (int up, int down) GetFanHysteresis() => Acpi.GetFanHysteresis();
    public void SetFanHysteresis(int up, int down) => Acpi.SetFanHysteresis(up, down);

    // ---- modes ----
    public int CurrentMode => Modes.GetCurrent();
    public int CurrentBaseMode => Modes.GetCurrentBase();
    public bool IsCurrentCustom => Modes.IsCurrentCustom();

    public IReadOnlyList<ChoiceItem> GetModes() =>
        Modes.GetDictonary().Select(kv => new ChoiceItem(kv.Key, kv.Value)).ToList();

    public void RemoveCustomMode(int mode) => Modes.Remove(mode);
    public int AddCustomMode() => Modes.Add();
    public void SetCurrentModeName(string name) => AppConfig.SetMode("mode_name", name);

    // ---- actions ----
    public void AutoPower() => Mode.AutoPower(true);
    public void AutoFans() => Mode.AutoFans();
    public void SetPerformanceMode(int mode = -1) => Mode.SetPerformanceMode(mode);
    public void ResetPerformanceMode() => Mode.ResetPerformanceMode();
    public void SetGpuPower() => Mode.SetGPUPower();
    public void SetGpuClocks(bool reset = false) => Mode.SetGPUClocks(true, reset);
    public string ApplyRyzen() => Mode.SetRyzen(true);
    public string ReadRyzenLimits() => Mode.ReadRyzenLimits();
    public void AutoRyzen() => Mode.AutoRyzen();
    public bool IsPawnAvailable => ModeControl.IsPawnAvailable();
    public bool IsPawnInstalled => ModeControl.IsPawnInstalled();
    public void XgmReset() => XGM.Reset();
    public void StartCalibration() => _sensors.StartCalibration();
    public void RefreshContextMenu() => Program.Ui.Refresh("contextmenu");

    public void OpenUrl(string url)
    {
        try { PlatformServices.Current.Process.OpenUrl(url); }
        catch (Exception ex) { Logger.WriteLine("OpenUrl: " + ex.Message); }
    }

    // ---- power plan ----
    public IReadOnlyList<PowerModeItem> PowerModes =>
        PowerNative.powerModes.Select(kv => new PowerModeItem(kv.Key, kv.Value)).ToList();

    public int GetCpuBoost() => PowerNative.GetCPUBoost();
    public void SetCpuBoost(int boost) => PowerNative.SetCPUBoost(boost);
    public string GetPowerMode() => PowerNative.GetPowerMode();
    public void SetPowerMode(string key) => PowerNative.SetPowerMode(key);
    public bool IsBatterySaver => PowerNative.GetBatterySaverStatus();
    public string GetDefaultPowerMode(int baseMode) => PowerNative.GetDefaultPowerMode(baseMode);

    // ---- CPU ----
    public bool IsAmd => CpuInfo.IsAMD;
    public string CpuName => CpuInfo.Name;
    public bool IsSupportedUV => CpuInfo.IsSupportedUV();
    public bool IsSupportedUViGPU => CpuInfo.IsSupportedUViGPU();
    public int MinCpuUV => CpuInfo.MinCPUUV;
    public int MaxCpuUV => CpuInfo.MaxCPUUV;
    public int MinIGpuUV => CpuInfo.MinIGPUUV;
    public int MaxIGpuUV => CpuInfo.MaxIGPUUV;
    public int MinCpuTemp => CpuInfo.MinTemp;
    public int DefaultCpuTemp => CpuInfo.DefaultTemp;

    // ---- GPU ----
    public GpuSnapshot? ReadGpu()
    {
        if (Acpi.DeviceGet(AsusACPI.GPUEco) == 1) return null;

        if (HardwareControl.GpuControl is null || !HardwareControl.GpuControl.IsValid) HardwareControl.RecreateGpuControl();
        if (HardwareControl.GpuControl is not NvidiaGpuControl nv) return null;

        int boost = AppConfig.GetMode("gpu_boost");
        int temp = AppConfig.GetMode("gpu_temp");
        int core = AppConfig.GetMode("gpu_core");
        int memory = AppConfig.GetMode("gpu_memory");
        int clockLimit = AppConfig.GetMode("gpu_clock_limit");

        if (boost < 0) boost = AsusACPI.MaxGPUBoost;
        if (temp < 0) temp = AsusACPI.MaxGPUTemp;
        if (core == -1) core = 0;
        if (memory == -1) memory = 0;
        if (clockLimit == -1) clockLimit = NvidiaGpuControl.MaxClockLimit;

        string? name = null;

        if (nv.GetClocks(out int currentCore, out int currentMemory))
        {
            core = currentCore;
            memory = currentMemory;
        }

        int limit = nv.GetMaxGPUCLock();
        if (limit == 0) clockLimit = NvidiaGpuControl.MaxClockLimit;
        else if (limit > 0) clockLimit = limit;

        try { name = nv.FullName; } catch { }

        return new GpuSnapshot(name, core, memory, clockLimit, boost, temp,
            Acpi.IsSupported(AsusACPI.PPT_GPUC0), Acpi.IsSupported(AsusACPI.PPT_GPUC2));
    }

    public int GpuClockMin => NvidiaGpuControl.MinClockLimit;
    public int GpuClockMax => NvidiaGpuControl.MaxClockLimit;
    public int GpuCoreMin => NvidiaGpuControl.MinCoreOffset;
    public int GpuCoreMax => NvidiaGpuControl.MaxCoreOffset;
    public int GpuMemoryMin => NvidiaGpuControl.MinMemoryOffset;
    public int GpuMemoryMax => NvidiaGpuControl.MaxMemoryOffset;

    public int ReadGpuPowerBase() => Acpi.DeviceGet(AsusACPI.GPU_BASE);

    public int ReadGpuPowerVariable()
    {
        int value = Acpi.DeviceGet(AsusACPI.GPU_POWER);
        Logger.WriteLine($"ReadGPUPower ({Modes.GetCurrentBase()}): {Acpi.DeviceGet(AsusACPI.GPU_BASE)} + {value}");
        return value;
    }

    public int DetectGpuPowerMax(int gpuPowerBase)
    {
        int max = NvidiaSmi.GetMaxGPUPower();
        if (max > 0) AsusACPI.MaxGPUPower = max - gpuPowerBase - AsusACPI.MaxGPUBoost;
        return AsusACPI.MaxGPUPower;
    }

    // ---- fans / temperature ----
    public int FanMin(AsusFan device) => FanSensorControl.GetFanMin(device);
    public int FanMax(AsusFan device) => FanSensorControl.GetFanMax(device);
    public bool IsFahrenheit => TempHelper.IsFahrenheit;
    public string FormatTemp(double celsius) => TempHelper.FormatTemp(celsius);
    public string FansResult => ModeControl.FansResult;
}
