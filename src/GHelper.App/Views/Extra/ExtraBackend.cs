namespace GHelper.App.Views.Extra;

/// <summary>Model / hardware capabilities that decide which rows of the extra window exist (read once per window).</summary>
internal sealed record ExtraCaps
{
    public bool IsAlly { get; init; }
    public bool IsDuo { get; init; }
    public bool IsArcnm { get; init; }
    public bool NoMKeys { get; init; }
    public bool IsM4Button { get; init; }
    public bool IsVivoZenPro { get; init; }
    public bool MediaKeys { get; init; }
    public bool IsTuf { get; init; }
    public bool IsNoFnV { get; init; }
    public bool IsStrix { get; init; }
    public bool IsBacklightZones { get; init; }
    public bool HasLightbar { get; init; }
    public bool HasLogo { get; init; }
    public bool HasRearglow { get; init; }
    public bool GpuModeSupported { get; init; }
    public bool OverdriveSupported { get; init; }
    public bool XgmConnected { get; init; }
    public bool NvidiaGpu { get; init; }
    public bool Keystone { get; init; }
    public bool Debug { get; init; }
}

internal sealed record CoresInfo(IReadOnlyList<string> PItems, IReadOnlyList<string> EItems, int PIndex, int EIndex,
    Action<int, int> Apply);

/// <summary>APU / iGPU memory options. <see cref="Apply"/> returns true when the "restart now?" question should follow.</summary>
internal sealed record ApuMemory(IReadOnlyList<string> Options, int SelectedIndex, Func<int, bool> Apply);

/// <summary>
/// Everything the extra settings window needs from Core (AppConfig, Aura, InputDispatcher, ScreenControl, PowerNative,
/// AsusService, XGM ...). Same seam idea as <see cref="Fans.IFansBackend"/>: <see cref="ExtraViewModel"/> holds the
/// legacy Extra.cs rules, tests run on <see cref="SampleExtraBackend"/>.
/// </summary>
internal interface IExtraBackend
{
    ExtraCaps ReadCaps();

    // ---- config ----
    string? GetString(string key);
    void SetString(string key, string value);
    int GetInt(string key, int fallback = 0);
    void SetInt(string key, int value);
    bool Is(string key);
    bool IsNotFalse(string key);
    bool IsOnBattery(string zone);

    // ---- hotkeys ----
    void ApplyMKeys();
    void ResetMKeys();

    // ---- keyboard backlight ----
    IReadOnlyList<(int key, string text)> AuraSpeeds { get; }
    int AuraSpeed { get; }
    void ApplyAura();
    void ApplyAuraPower();
    bool IsOldStrix { get; }
    int GetBacklight();
    bool IsOnBatteryPower { get; }
    void ApplyBrightness(int value);
    void InitBacklightTimer();

    // ---- other settings ----
    void SetClamshell(bool enabled);
    void SetTopmost(bool enabled);
    void SetNoOverdrive(bool enabled);
    int GetElmb();
    void SetElmb(bool enabled);
    void SetXgmLight(bool enabled);
    int GetBootSound();
    void SetBootSound(bool enabled);
    int GetStatusLed();
    void SetStatusLed(bool enabled);
    int GetNumberPad();
    void SetNumberPad(bool enabled);
    int GetOptimalBrightness();
    void SetOptimalBrightness(int index);
    bool IsKeystoneEnabled();
    void SetKeystone(bool enabled);
    void SetAspm(bool disabled);
    void SetStandbyNetworking(bool disabled);

    // ---- hibernate / cores / memory ----
    /// <summary>Minutes, -1 or an exception when the power scheme has no such setting.</summary>
    int GetHibernateAfter();
    void SetHibernateAfter(int minutes);
    CoresInfo? ReadCores();
    ApuMemory? ReadApuMemory();

    // ---- services / admin ----
    int GetRunningServicesCount();
    void StopServices();
    void StartServices();
    void ReinitInput();
    bool IsAdministrator { get; }
    void RunAsAdmin(string param);

    // ---- misc ----
    bool Confirm(string title, string text);
    void Restart();
    void OpenUrl(string url);
    string LogFile { get; }
    string ScanRange();
    string AcpiTest(string commandHex, string parameter);
}
