using GHelper.App.ViewModels;

namespace GHelper.App.Views.Fans;

/// <summary>
/// In-memory backend: design time data and the headless tests. Keeps config values in a dictionary and records every
/// action (<see cref="Calls"/>) so tests can assert what the legacy form would have asked Core to do.
/// </summary>
internal class SampleFansBackend : IFansBackend
{
    public Dictionary<string, int> ModeInts { get; } = new();
    public Dictionary<string, string> ModeStrings { get; } = new();
    public Dictionary<string, int> Config { get; } = new();
    public Dictionary<AsusFan, byte[]> StoredCurves { get; } = new();
    public Dictionary<uint, int> Device { get; } = new();
    public HashSet<uint> Supported { get; } = new();
    public List<string> Calls { get; } = new();

    public bool MidFan { get; set; }
    public bool XgmConnected { get; set; }
    public bool Amd { get; set; } = true;
    public bool PawnAvailable { get; set; } = true;
    public bool PawnInstalled { get; set; } = true;
    public bool UvSupported { get; set; } = true;
    public bool IGpuUvSupported { get; set; }
    public bool AllAmd { get; set; }
    public int GpuBase { get; set; }
    public int GpuPowerVar { get; set; } = 10;
    public int GpuPowerMax { get; set; } = 70;
    public GpuSnapshot? Gpu { get; set; } = new("NVIDIA GeForce RTX 4060 Laptop GPU", 100, 200, 3000, 25, 87, true, true);
    public (int up, int down) Hysteresis { get; set; } = (3, 3);
    public int CpuBoost { get; set; } = 2;
    public string PowerMode { get; set; } = "00000000-0000-0000-0000-000000000000";
    public bool BatterySaver { get; set; }
    public string CpuNameValue { get; set; } = "AMD Ryzen 9 7940HS w/ Radeon 780M Graphics";
    public int Current { get; set; } = 0;
    public int BaseMode { get; set; } = 0;
    public List<ChoiceItem> ModeList { get; } = new()
    {
        new ChoiceItem(2, "Silent"), new ChoiceItem(0, "Balanced"), new ChoiceItem(1, "Turbo"),
    };
    public string Result { get; set; } = "";
    public string ApplyRyzenResult { get; set; } = "";
    public string ReadLimitsResult { get; set; } = "";
    public string DefaultPower { get; set; } = "00000000-0000-0000-0000-000000000000";

    public static SampleFansBackend Create(bool midFan = false, bool xgm = false)
    {
        var b = new SampleFansBackend { MidFan = midFan, XgmConnected = xgm };
        foreach (var id in new uint[]
                 {
                     AsusACPI.PPT_APUA0, AsusACPI.PPT_APUC1, AsusACPI.PPT_CROSS9F, AsusACPI.PPT_GPUCPU9C,
                     AsusACPI.PPT_TEMP9E, AsusACPI.PPT_GPUC0, AsusACPI.PPT_GPUC2, AsusACPI.DevsCPUFanCurve,
                 })
            b.Supported.Add(id);
        b.GpuBase = 80;
        b.ModeInts["limit_total"] = 65;
        b.ModeInts["limit_slow"] = 80;
        b.ModeInts["limit_fast"] = 90;
        b.ModeInts["limit_cpu"] = 75;
        b.ModeInts["auto_apply"] = 1;
        b.ModeInts["auto_apply_power"] = 1;
        return b;
    }

    private void Log(string call) => Calls.Add(call);

    // ---- configuration ----
    public int GetMode(string key, int fallback = -1) => ModeInts.TryGetValue(key, out var v) ? v : fallback;
    public void SetMode(string key, int value) { ModeInts[key] = value; Log($"SetMode({key},{value})"); }
    public void SetMode(string key, string value) { ModeStrings[key] = value; Log($"SetMode({key},{value})"); }
    public void RemoveMode(string key) { ModeInts.Remove(key); ModeStrings.Remove(key); Log($"RemoveMode({key})"); }
    public void SetConfig(string key, int value) { Config[key] = value; Log($"Set({key},{value})"); }
    public bool IsConfigOn(string key) => Config.TryGetValue(key, out var v) && v == 1;

    public byte[] GetFanConfig(AsusFan device) => StoredCurves.TryGetValue(device, out var c) ? c : Array.Empty<byte>();
    public void SetFanConfig(AsusFan device, byte[] curve) { StoredCurves[device] = (byte[])curve.Clone(); Log($"SetFanConfig({device})"); }

    public byte[] GetDefaultCurve(AsusFan device) => device == AsusFan.GPU
        ? new byte[] { 0x3A, 0x3D, 0x40, 0x44, 0x48, 0x4D, 0x51, 0x62, 0x0C, 0x16, 0x1D, 0x1F, 0x26, 0x2D, 0x34, 0x4A }
        : new byte[] { 0x3A, 0x3D, 0x40, 0x44, 0x48, 0x4D, 0x51, 0x62, 0x08, 0x11, 0x16, 0x1A, 0x22, 0x29, 0x30, 0x45 };

    public byte[] FixFanCurve(byte[] curve) => curve;

    public bool IsApplyPower => GetMode("auto_apply_power") == 1;
    public bool IsApplyFans => GetMode("auto_apply") == 1;
    public bool IsApplyUV => GetMode("auto_uv") == 1;
    public bool IsFanRequired { get; set; }
    public bool IsClampFanDots { get; set; }

    // ---- ACPI ----
    public bool IsSupported(uint device) => Supported.Contains(device);
    public int DeviceGet(uint device) => Device.TryGetValue(device, out var v) ? v : -1;
    public bool IsAllAmdPPT => AllAmd;
    public byte[] GetFanCurve(AsusFan device, int mode = 0) =>
        device == AsusFan.Mid && !MidFan ? new byte[16] : GetDefaultCurve(device);
    public bool IsMidFanSupported => MidFan;
    public bool IsXgConnected => XgmConnected;
    public (int up, int down) GetFanHysteresis() => Hysteresis;
    public void SetFanHysteresis(int up, int down) => Log($"SetFanHysteresis({up},{down})");

    // ---- modes ----
    public int CurrentMode => Current;
    public int CurrentBaseMode => BaseMode;
    public bool IsCurrentCustom => Current > 2;
    public IReadOnlyList<ChoiceItem> GetModes() => ModeList.ToList();
    public void RemoveCustomMode(int mode)
    {
        ModeList.RemoveAll(m => m.Key == mode);
        Log($"RemoveCustomMode({mode})");
    }

    public int AddCustomMode()
    {
        int id = ModeList.Max(m => m.Key) + 1;
        ModeList.Add(new ChoiceItem(id, "Custom " + (id - 2)));
        Log($"AddCustomMode()={id}");
        return id;
    }

    public void SetCurrentModeName(string name)
    {
        int i = ModeList.FindIndex(m => m.Key == Current);
        if (i >= 0) ModeList[i] = ModeList[i] with { Text = name };
        Log($"SetCurrentModeName({name})");
    }

    // ---- actions ----
    public void AutoPower() => Log("AutoPower");
    public void AutoFans() => Log("AutoFans");
    public void SetPerformanceMode(int mode = -1) { Log($"SetPerformanceMode({mode})"); if (mode >= 0) Current = mode; }
    public void ResetPerformanceMode() => Log("ResetPerformanceMode");
    public void SetGpuPower() => Log("SetGpuPower");
    public void SetGpuClocks(bool reset = false) => Log($"SetGpuClocks({reset})");
    public string ApplyRyzen() { Log("ApplyRyzen"); return ApplyRyzenResult; }
    public string ReadRyzenLimits() { Log("ReadRyzenLimits"); return ReadLimitsResult; }
    public void AutoRyzen() => Log("AutoRyzen");
    public bool IsPawnAvailable => PawnAvailable;
    public bool IsPawnInstalled => PawnInstalled;
    public void XgmReset() => Log("XgmReset");
    public void StartCalibration() => Log("StartCalibration");
    public void RefreshContextMenu() => Log("RefreshContextMenu");
    public void OpenUrl(string url) => Log("OpenUrl(" + url + ")");

    // ---- power plan ----
    public IReadOnlyList<PowerModeItem> PowerModes { get; } = new[]
    {
        new PowerModeItem("961cc777-2547-4f9d-8174-7d86181b8a7a", "Best Power Efficiency"),
        new PowerModeItem("00000000-0000-0000-0000-000000000000", "Balanced"),
        new PowerModeItem("ded574b5-45a0-4f42-8737-46345c09c238", "Best Performance"),
    };

    public int GetCpuBoost() => CpuBoost;
    public void SetCpuBoost(int boost) { CpuBoost = boost; Log($"SetCpuBoost({boost})"); }
    public string GetPowerMode() => PowerMode;
    public void SetPowerMode(string key) { PowerMode = key; Log($"SetPowerMode({key})"); }
    public bool IsBatterySaver => BatterySaver;
    public string GetDefaultPowerMode(int baseMode) => DefaultPower;

    // ---- CPU ----
    public bool IsAmd => Amd;
    public string CpuName => CpuNameValue;
    public bool IsSupportedUV => UvSupported;
    public bool IsSupportedUViGPU => IGpuUvSupported;
    public int MinCpuUV => -40;
    public int MaxCpuUV => 0;
    public int MinIGpuUV => -30;
    public int MaxIGpuUV => 0;
    public int MinCpuTemp => 75;
    public int DefaultCpuTemp => 96;

    // ---- GPU ----
    public GpuSnapshot? ReadGpu() => Gpu;
    public int GpuClockMin => 400;
    public int GpuClockMax => 3000;
    public int GpuCoreMin => -250;
    public int GpuCoreMax => 250;
    public int GpuMemoryMin => -500;
    public int GpuMemoryMax => 500;
    public int ReadGpuPowerBase() => GpuBase;
    public int ReadGpuPowerVariable() => GpuPowerVar;
    public int DetectGpuPowerMax(int gpuPowerBase) => GpuPowerMax;

    // ---- fans / temperature ----
    public int FanMin(AsusFan device) => 18;
    public int FanMax(AsusFan device) => device == AsusFan.XGM ? 72 : 58;
    public bool IsFahrenheit { get; set; }
    public string FormatTemp(double celsius) =>
        IsFahrenheit ? Math.Round(celsius * 9.0 / 5.0 + 32.0) + "°F" : Math.Round(celsius) + "°C";
    public string FansResult => Result;
}
