using GHelper;
using GHelper.App.Services;
using GHelper.Display;
using GHelper.Gpu;
using GHelper.Helpers;
using GHelper.Input;
using GHelper.Mode;
using GHelper.Platform;
using GHelper.Properties;
using GHelper.USB;

namespace GHelper.App.Views.Extra;

/// <summary>The real thing: forwards to GHelper.Core (legacy Extra.cs called these statics directly).</summary>
internal sealed class CoreExtraBackend : IExtraBackend
{
    private readonly ClamshellModeControl _clamshell = new();

    private static AsusACPI Acpi => Program.acpi;

    public ExtraCaps ReadCaps() => new()
    {
        IsAlly = AppConfig.IsAlly(),
        IsDuo = AppConfig.IsDUO(),
        IsArcnm = AppConfig.IsARCNM(),
        NoMKeys = AppConfig.NoMKeys(),
        IsM4Button = AppConfig.IsM4Button(),
        IsVivoZenPro = AppConfig.IsVivoZenPro(),
        MediaKeys = AppConfig.MediaKeys(),
        IsTuf = AppConfig.IsTUF(),
        IsNoFnV = AppConfig.IsNoFNV(),
        IsStrix = AppConfig.IsStrix(),
        IsBacklightZones = AppConfig.IsBacklightZones(),
        HasLightbar = Aura.HasLightbar,
        HasLogo = Aura.HasLogo,
        HasRearglow = Aura.HasRearglow,
        GpuModeSupported = Acpi.IsSupported(AsusACPI.GPUEco),
        OverdriveSupported = Acpi.IsOverdriveSupported(),
        XgmConnected = XGM.IsConnected(),
        NvidiaGpu = Acpi.IsNVidiaGPU(),
        Keystone = AppConfig.IsKeystone(),
        Debug = AppConfig.Is("debug"),
    };

    // ---- config ----
    public string? GetString(string key) => AppConfig.GetString(key);
    public void SetString(string key, string value) => AppConfig.Set(key, value);
    public int GetInt(string key, int fallback = 0) => AppConfig.Get(key, fallback);
    public void SetInt(string key, int value) => AppConfig.Set(key, value);
    public bool Is(string key) => AppConfig.Is(key);
    public bool IsNotFalse(string key) => AppConfig.IsNotFalse(key);
    public bool IsOnBattery(string zone) => AppConfig.IsOnBattery(zone);

    // ---- hotkeys ----
    public void ApplyMKeys()
    {
        MKeyControl.ApplyAll();
        Program.inputDispatcher.RegisterKeys();
    }

    public void ResetMKeys()
    {
        MKeyControl.Reset();
        Program.inputDispatcher.RegisterKeys();
    }

    // ---- keyboard backlight ----
    public IReadOnlyList<(int key, string text)> AuraSpeeds =>
        Aura.GetSpeeds().Select(kv => ((int)kv.Key, kv.Value)).ToList();

    public int AuraSpeed => (int)Aura.Speed;
    public void ApplyAura() => Aura.ApplyAura();
    public void ApplyAuraPower() => Aura.ApplyPower();
    public bool IsOldStrix => Aura.IsOldStrix;
    public int GetBacklight() => InputDispatcher.GetBacklight();
    public bool IsOnBatteryPower => PlatformServices.Current.Power.GetPowerSource() != PowerSource.AC;
    public void ApplyBrightness(int value) => Aura.ApplyBrightness(value, "Slider");
    public void InitBacklightTimer() => Program.inputDispatcher.InitBacklightTimer();

    // ---- other settings ----
    public void SetClamshell(bool enabled)
    {
        if (enabled) _clamshell.ToggleLidAction();
        else ClamshellModeControl.DisableClamshellMode();
    }

    public void SetTopmost(bool enabled)
    {
        UiSettings.Topmost = enabled;
        if (App.Instance?.MainWindow is { } main) main.Topmost = enabled;
    }

    public void SetNoOverdrive(bool enabled) => ScreenControl.AutoScreen(true);
    public int GetElmb() => ScreenELMB.Get();
    public void SetElmb(bool enabled) => ScreenELMB.Set(enabled ? 1 : 0);
    public void SetXgmLight(bool enabled) => XGM.Light(enabled);
    public int GetBootSound() => Acpi.DeviceGet(AsusACPI.BootSound);
    public void SetBootSound(bool enabled) => Acpi.DeviceSet(AsusACPI.BootSound, enabled ? 1 : 0, "BootSound");
    public int GetStatusLed() => Acpi.DeviceGet(AsusACPI.StatusLed);
    public void SetStatusLed(bool enabled) => InputDispatcher.SetStatusLED(enabled);
    public int GetNumberPad() => AppConfig.IsNumberPad() ? NumberPad.Get() : -1;
    public void SetNumberPad(bool enabled) => NumberPad.Set(enabled);
    public int GetOptimalBrightness() => ScreenControl.GetOptimalBrightness();
    public void SetOptimalBrightness(int index) => ScreenControl.SetOptimalBrightness(index);
    public bool IsKeystoneEnabled() => Keystone.IsEnabled();
    public void SetKeystone(bool enabled) => Keystone.SetEnabled(enabled);
    public void SetAspm(bool disabled) => PowerNative.SetBalancedASPM(disabled ? 0 : 2);

    public void SetStandbyNetworking(bool disabled)
    {
        if (disabled) PowerNative.SetConnectivityInStandby(0, 0);
        else PowerNative.SetConnectivityInStandby(1, 2);
    }

    // ---- hibernate / cores / memory ----
    public int GetHibernateAfter() => PowerNative.GetHibernateAfter();
    public void SetHibernateAfter(int minutes) => PowerNative.SetHibernateAfter(minutes);

    public CoresInfo? ReadCores()
    {
        (int eCores, int pCores) = Acpi.GetCores();
        (int eCoresMax, int pCoresMax) = Acpi.GetCores(AsusACPI.CORES_MAX);

        if (eCores < 0 || pCores < 0 || eCoresMax < 0 || pCoresMax < 0) return null;

        if (eCoresMax == 0) eCoresMax = 8;
        if (pCoresMax == 0) pCoresMax = 6;
        if (AppConfig.Is8Ecores()) eCoresMax = Math.Max(8, eCoresMax);
        eCoresMax = Math.Max(4, eCoresMax);

        int minP = AsusACPI.PCoreMin;
        int minE = AsusACPI.ECoreMin;

        (int eMin, int pMin) = Acpi.GetCores(AsusACPI.CORES_MIN);
        if (pMin >= 1)
        {
            minP = Math.Min(pMin, pCoresMax);
            minE = Math.Min(eMin, eCoresMax);
        }

        var pItems = new List<string>();
        var eItems = new List<string>();
        for (int i = minP; i <= pCoresMax; i++) pItems.Add(i + " Pcores");
        for (int i = minE; i <= eCoresMax; i++) eItems.Add(i + " Ecores");

        int pIndex = Math.Max(Math.Min(pCores - minP, pItems.Count - 1), 0);
        int eIndex = Math.Max(Math.Min(eCores - minE, eItems.Count - 1), 0);

        return new CoresInfo(pItems, eItems, pIndex, eIndex, (e, p) => Acpi.SetCores(minE + e, minP + p));
    }

    public ApuMemory? ReadApuMemory()
    {
        int unitMb = 0;
        int[] vramOptions = [];
        if (PawnIO.CpuInfo.IsAMD) vramOptions = Acpi.GetVramOptions(out unitMb);

        var items = new List<string>();
        int selected;

        if (vramOptions.Length > 0)
        {
            foreach (int option in vramOptions)
                items.Add(option == 0 ? Strings.AutoMode : ((double)option * unitMb / 1024).ToString("0.#") + "G");

            int current = Acpi.GetVramMem();
            if (current == 0) current = AppConfig.Get("vram_mem", 0);
            selected = Math.Max(0, Array.IndexOf(vramOptions, current));
        }
        else if (IntelVram.Init())
        {
            items.Add(Strings.AutoMode);
            foreach (int size in IntelVram.Sizes) items.Add(size + "G");
            selected = IntelVram.Index;
        }
        else
        {
            if (!AppConfig.IsAlly()) return null;

            int apuMem = Acpi.GetAPUMem();
            if (apuMem < 0) return null;

            // legacy keeps the designer list "Auto, 1G .. 8G" for the Ally
            items.Add(Strings.AutoMode);
            for (int i = 1; i <= 8; i++) items.Add(i + "G");
            selected = apuMem;
        }

        return new ApuMemory(items, selected, mem =>
        {
            if (IntelVram.Supported && !ProcessHelper.IsUserAdministrator())
            {
                ProcessHelper.RunAsAdmin();
                return false;
            }

            if (IntelVram.Supported) IntelVram.SetIndex(mem);
            else if (vramOptions.Length == 0) Acpi.SetAPUMem(mem);
            else
            {
                Acpi.SetVramMem(vramOptions[mem]);
                AppConfig.Set("vram_mem", vramOptions[mem]);
            }

            return true;
        });
    }

    // ---- services / admin ----
    public int GetRunningServicesCount() => AsusService.GetRunningCount();
    public void StopServices() => AsusService.StopAsusServices();
    public void StartServices() => AsusService.StartAsusServices();
    public void ReinitInput() => Program.inputDispatcher.Init();
    public bool IsAdministrator => ProcessHelper.IsUserAdministrator();
    public void RunAsAdmin(string param) => ProcessHelper.RunAsAdmin(param);

    // ---- misc ----
    public bool Confirm(string title, string text) => Program.Ui.Confirm(title, text);

    public void Restart()
    {
        if (OperatingSystem.IsWindows())
            PlatformServices.Current.Process.RunCommand("shutdown", "/r /t 1");
        else
            PlatformServices.Current.Process.RunCommand("systemctl", "reboot");
    }

    public void OpenUrl(string url)
    {
        try { PlatformServices.Current.Process.OpenUrl(url); }
        catch (Exception ex) { Logger.WriteLine("OpenUrl: " + ex.Message); }
    }

    public string LogFile => Logger.logFile;
    public string ScanRange() => Acpi.ScanRange();

    public string AcpiTest(string commandHex, string parameter)
    {
        try
        {
            int deviceId = Convert.ToInt32(commandHex, 16);
            int status = Convert.ToInt32(parameter, parameter.Contains('x') ? 16 : 10);
            int result = Acpi.DeviceSet((uint)deviceId, status, "TestACPI " + deviceId.ToString("X8") + " " + status.ToString("X4"));
            return "ACPI DEVS Test : " + result;
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
            return "ACPI DEVS Test";
        }
    }
}
