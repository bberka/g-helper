namespace GHelper.App.Views.Extra;

/// <summary>In-memory backend for the design time preview and the headless tests; records the calls Core would get.</summary>
internal class SampleExtraBackend : IExtraBackend
{
    public Dictionary<string, string> Strs { get; } = new();
    public Dictionary<string, int> Ints { get; } = new();
    public List<string> Calls { get; } = new();

    public ExtraCaps Caps { get; set; } = new()
    {
        IsStrix = true,
        IsTuf = true,
        IsBacklightZones = true,
        HasLightbar = true,
        HasLogo = true,
        HasRearglow = true,
        GpuModeSupported = true,
        OverdriveSupported = true,
        XgmConnected = true,
        NvidiaGpu = true,
        Keystone = true,
    };

    public int Elmb { get; set; } = 0;
    public int BootSound { get; set; } = 1;
    public int StatusLed { get; set; } = 1;
    public int NumberPad { get; set; } = 0;
    public int Optimal { get; set; } = 1;
    public int Hibernate { get; set; } = 30;
    public int Backlight { get; set; } = 2;
    public bool OnBattery { get; set; }
    public int ServicesCount { get; set; } = 3;
    public bool Admin { get; set; } = true;
    public bool ConfirmAnswer { get; set; } = true;
    public CoresInfo? Cores { get; set; } = new(
        new[] { "4 Pcores", "5 Pcores", "6 Pcores" }, new[] { "0 Ecores", "4 Ecores", "8 Ecores" }, 2, 2,
        (e, p) => { });

    public ApuMemory? Apu { get; set; } = new(new[] { "Auto", "1G", "2G", "4G", "8G" }, 0, _ => true);

    public static SampleExtraBackend Create() => new();

    public ExtraCaps ReadCaps() => Caps;

    public string? GetString(string key) => Strs.TryGetValue(key, out var v) ? v : null;
    public void SetString(string key, string value) { Strs[key] = value; Calls.Add($"SetString({key},{value})"); }
    public int GetInt(string key, int fallback = 0) => Ints.TryGetValue(key, out var v) ? v : fallback;
    public void SetInt(string key, int value) { Ints[key] = value; Calls.Add($"SetInt({key},{value})"); }
    public bool Is(string key) => GetInt(key, 0) == 1;
    public bool IsNotFalse(string key) => GetInt(key, 1) != 0;
    public bool IsOnBattery(string zone) => GetInt(zone + "_bat", 0) == 1;

    public void ApplyMKeys() => Calls.Add("ApplyMKeys");
    public void ResetMKeys() => Calls.Add("ResetMKeys");

    public IReadOnlyList<(int key, string text)> AuraSpeeds { get; } = new[] { (0, "Slow"), (1, "Normal"), (2, "Fast") };
    public int AuraSpeed { get; set; } = 1;
    public void ApplyAura() => Calls.Add("ApplyAura");
    public void ApplyAuraPower() => Calls.Add("ApplyAuraPower");
    public bool IsOldStrix { get; set; }
    public int GetBacklight() => Backlight;
    public bool IsOnBatteryPower => OnBattery;
    public void ApplyBrightness(int value) => Calls.Add($"ApplyBrightness({value})");
    public void InitBacklightTimer() => Calls.Add("InitBacklightTimer");

    public void SetClamshell(bool enabled) => Calls.Add($"SetClamshell({enabled})");
    public void SetTopmost(bool enabled) => Calls.Add($"SetTopmost({enabled})");
    public void SetNoOverdrive(bool enabled) => Calls.Add($"SetNoOverdrive({enabled})");
    public int GetElmb() => Elmb;
    public void SetElmb(bool enabled) => Calls.Add($"SetElmb({enabled})");
    public void SetXgmLight(bool enabled) => Calls.Add($"SetXgmLight({enabled})");
    public int GetBootSound() => BootSound;
    public void SetBootSound(bool enabled) => Calls.Add($"SetBootSound({enabled})");
    public int GetStatusLed() => StatusLed;
    public void SetStatusLed(bool enabled) => Calls.Add($"SetStatusLed({enabled})");
    public int GetNumberPad() => NumberPad;
    public void SetNumberPad(bool enabled) => Calls.Add($"SetNumberPad({enabled})");
    public int GetOptimalBrightness() => Optimal;
    public void SetOptimalBrightness(int index) => Calls.Add($"SetOptimalBrightness({index})");
    public bool IsKeystoneEnabled() => true;
    public void SetKeystone(bool enabled) => Calls.Add($"SetKeystone({enabled})");
    public void SetAspm(bool disabled) => Calls.Add($"SetAspm({disabled})");
    public void SetStandbyNetworking(bool disabled) => Calls.Add($"SetStandbyNetworking({disabled})");

    public int GetHibernateAfter() => Hibernate;
    public void SetHibernateAfter(int minutes) => Calls.Add($"SetHibernateAfter({minutes})");
    public CoresInfo? ReadCores() => Cores;
    public ApuMemory? ReadApuMemory() => Apu;

    public int GetRunningServicesCount() => ServicesCount;
    public void StopServices() { ServicesCount = 0; Calls.Add("StopServices"); }
    public void StartServices() { ServicesCount = 4; Calls.Add("StartServices"); }
    public void ReinitInput() => Calls.Add("ReinitInput");
    public bool IsAdministrator => Admin;
    public void RunAsAdmin(string param) => Calls.Add($"RunAsAdmin({param})");

    public bool Confirm(string title, string text) { Calls.Add("Confirm"); return ConfirmAnswer; }
    public void Restart() => Calls.Add("Restart");
    public void OpenUrl(string url) => Calls.Add($"OpenUrl({url})");
    public string LogFile => "log.txt";
    public string ScanRange() => "scan.txt";
    public string AcpiTest(string commandHex, string parameter)
    {
        Calls.Add($"AcpiTest({commandHex},{parameter})");
        return "ACPI DEVS Test : 1";
    }
}
