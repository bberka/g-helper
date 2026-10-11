using System.Diagnostics;
using System.Drawing;
using System.Management;
using System.Runtime.Versioning;
using GHelper.Platform.Windows.Processes;
using Microsoft.Win32;

namespace GHelper.Platform.Windows.Vendor;

/// <summary>
/// Armoury Crate / ASUS service handling (legacy AsusService), Windows Dynamic Lighting (DynamicLightingHelper), the
/// Keystone proximity sound and device problem detection (DeviceHelper).
/// </summary>
public sealed class WindowsVendorServices : IVendorServices
{
    private const string LightingKey = @"HKEY_CURRENT_USER\Software\Microsoft\Lighting";
    private const string LightingValue = "AmbientLightingEnabled";

    private const string KeystoneRegistryPath = @"AppEvents\Schemes\Apps\.Default\ProximityConnection\.Current";
    private const string KeystoneSoundFile = @"C:\WINDOWS\media\Windows Proximity Connection.wav";

    private static readonly string[] Services =
    [
        "ArmouryCrateControlInterface",
        "ArmouryCrateProArtService",
        "AsHidService",
        "ASUSOptimization",
        "AsusAppService",
        "ASUSLinkNear",
        "ASUSLinkRemote",
        "ASUSSoftwareManager",
        "ASUSLiveUpdateAgent",
        "ASUSSwitch",
        "ASUSSystemAnalysis",
        "ASUSSystemDiagnosis",
        "ASUSXGMobileService",
        "AsusCertService",
    ];

    // stopped but left startable ("Manual") because other things depend on them
    private static readonly string[] ServicesAc =
    [
        "ArmouryCrateSEService",
        "ArmouryCrateService",
        "LightingService",
    ];

    private readonly ISystemInfo system;
    private readonly WindowsProcessService process;

    public WindowsVendorServices(ISystemInfo system, WindowsProcessService process)
    {
        this.system = system;
        this.process = process;
    }

    // --- services -----------------------------------------------------------------------------

    private static bool IsRunning(string name)
    {
        var procs = Process.GetProcessesByName(name);
        try { return procs.Length > 0; }
        finally { foreach (var p in procs) p.Dispose(); }
    }

    public bool IsAsusOptimizationRunning() => IsRunning("AsusOptimization");

    public bool IsArmouryRunning()
    {
        bool acService = IsRunning("ArmouryCrate.Service");
        bool lightingService = IsRunning("LightingService");
        Logger.WriteLine($"AC Service: {acService}, Lighting Service: {lightingService}");
        return acService || lightingService;
    }

    public bool IsOSDRunning() => IsRunning("AsusOSD");

    /// <summary>Names to query: the always-stopped list, plus the Armoury Crate set when "stop_ac" is configured.</summary>
    public static IEnumerable<string> ServiceNames(bool includeArmoury) =>
        includeArmoury ? Services.Concat(ServicesAc) : Services;

    public static bool IsAcService(string name) => ServicesAc.Contains(name);

    private Dictionary<string, string> GetServiceStates()
    {
        var states = new Dictionary<string, string>();
        if (!OperatingSystem.IsWindows()) return states;

        try
        {
            ReadServiceStates(ServiceNames(AppConfig.IsStopAC()), states);
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }

        return states;
    }

    [SupportedOSPlatform("windows")]
    private static void ReadServiceStates(IEnumerable<string> names, Dictionary<string, string> states)
    {
        string filter = string.Join(" OR ", names.Select(name => $"Name='{name}'"));
        using var searcher = new ManagementObjectSearcher($"SELECT Name, State FROM Win32_Service WHERE {filter}");
        foreach (ManagementObject mo in searcher.Get())
        {
            using (mo)
                states[(string)mo["Name"]] = (string)mo["State"];
        }
    }

    public int GetRunningCount() => GetServiceStates().Count(s => s.Value != "Stopped");

    public void StopAsusServices()
    {
        foreach (string service in GetServiceStates().Where(s => s.Value != "Stopped").Select(s => s.Key).ToList())
        {
            process.StopDisableService(service, IsAcService(service) ? "Manual" : "Disabled");
        }

        if (GetRunningCount() == 0) AppConfig.Set("services_disabled", 1);

        // Core re-applies the Ally controller mode afterwards (legacy AsusService did it inline)
    }

    public void StartAsusServices()
    {
        AppConfig.Set("services_disabled", 0);
        foreach (string service in GetServiceStates().Keys)
        {
            process.StartEnableService(service);
        }
    }

    public void StopOnStartup()
    {
        if (AppConfig.Is("services_skip")) return;
        if (!AppConfig.Is("services_disabled") || !system.IsElevated) return;
        if (GetRunningCount() == 0) return;

        Logger.WriteLine("ASUS services revived, re-stopping on startup");
        Task.Run(StopAsusServices);
    }

    public void KillSmartDisplayControl() => process.KillByName("ASUSSmartDisplayControl");

    public void RunArmouryUninstaller() =>
        process.OpenUrl("https://dlcdnets.asus.com/pub/ASUS/mb/14Utilities/Armoury_Crate_Uninstall_Tool.zip");

    // --- Dynamic Lighting ---------------------------------------------------------------------

    public bool IsDynamicLightingSupported =>
        OperatingSystem.IsWindows() && Environment.OSVersion.Version.Build >= 22000 && !system.IsRunningAsSystem;

    public bool IsDynamicLightingEnabled()
    {
        if (!IsDynamicLightingSupported) return false;

        object? value = Registry.GetValue(LightingKey, LightingValue, 0);
        Logger.WriteLine("Dynamic lighting status: " + value);
        return value is int status && status > 0;
    }

    public void SetDynamicLighting(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;

        int status = enabled ? 1 : 0;
        try
        {
            Registry.SetValue(LightingKey, LightingValue, status, RegistryValueKind.DWord);
            SetDynamicLightingOnAllDevices(status);
            Logger.WriteLine($"Dynamic lighting: {status}");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error setting value for dynamic lighting: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetDynamicLightingOnAllDevices(int status)
    {
        using var devicesKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Lighting\Devices", true);
        if (devicesKey is null) return;

        foreach (string deviceKeyName in devicesKey.GetSubKeyNames())
        {
            using var deviceKey = devicesKey.OpenSubKey(deviceKeyName, true);
            if (deviceKey?.GetValue(LightingValue) != null)
                deviceKey.SetValue(LightingValue, status, RegistryValueKind.DWord);
        }
    }

    public void InitDynamicLighting()
    {
        if (!AppConfig.IsDynamicLightingInit()) return;
        if (!IsDynamicLightingSupported) return;
        if (IsDynamicLightingEnabled()) return;

        SetDynamicLighting(true);
        Thread.Sleep(1000);
        SetDynamicLighting(false);
    }

    public void OpenDynamicLightingSettings() => process.RunCommand("explorer", "ms-settings:personalization-lighting");

    /// <summary>AABBGGRR: the Dynamic Lighting registry values expect R and B swapped versus ARGB.</summary>
    public static int ToDynamicLightingColor(Color c) => unchecked((c.A << 24) | (c.B << 16) | (c.G << 8) | c.R);

    public void SetDynamicLightingEffect(int effect, Color? color = null, Color? color2 = null, int? brightness = null, int? speed = null)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            // let Windows own the device
            WriteLightingValue("ControlledByForegroundApp", 0);
            WriteLightingValue("AmbientLightingEnabled", 1);

            // core selector
            WriteLightingValue("EffectType", effect);
            WriteLightingValue("EffectMode", 0);

            if (brightness.HasValue) WriteLightingValue("Brightness", Math.Clamp(brightness.Value, 0, 100));
            if (speed.HasValue) WriteLightingValue("Speed", Math.Clamp(speed.Value, 0, 10));
            if (color.HasValue) WriteLightingValue("Color", ToDynamicLightingColor(color.Value));
            if (color2.HasValue) WriteLightingValue("Color2", ToDynamicLightingColor(color2.Value));

            Logger.WriteLine($"Dynamic lighting effect set: {effect}");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Dynamic lighting effect error: {ex}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void WriteLightingValue(string name, int value)
    {
        Registry.SetValue(LightingKey, name, value, RegistryValueKind.DWord);

        using var devicesKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Lighting\Devices", true);
        if (devicesKey == null) return;

        foreach (string dev in devicesKey.GetSubKeyNames())
        {
            using var dk = devicesKey.OpenSubKey(dev, true);
            dk?.SetValue(name, value, RegistryValueKind.DWord);
        }
    }

    // --- Keystone sound -----------------------------------------------------------------------

    public bool IsKeystoneEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeystoneRegistryPath);
            if (key is null) return false;
            return !string.IsNullOrEmpty(key.GetValue("") as string);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Keystone: failed to read sound status: {ex.Message}");
            return false;
        }
    }

    public void SetKeystoneEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeystoneRegistryPath, writable: true);
            if (key is null)
            {
                Logger.WriteLine("Keystone: registry key not found");
                return;
            }

            key.SetValue("", enabled ? KeystoneSoundFile : "", RegistryValueKind.String);
            Logger.WriteLine($"Keystone: sound {(enabled ? "enabled" : "disabled")}");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Keystone: failed to set sound status: {ex.Message}");
        }
    }

    // --- GameVisual / Splendid ----------------------------------------------------------------

    private const string GameVisualKey = @"HKEY_CURRENT_USER\Software\ASUS\ARMOURY CRATE Service\GameVisual";
    private const string GameVisualValue = "ActiveGVStatus";
    private string? splendidPath;

    public string? GetSplendidPath()
    {
        if (splendidPath is not null || !OperatingSystem.IsWindows()) return splendidPath;

        try
        {
            splendidPath = FindSplendidPath();
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }

        return splendidPath;
    }

    [SupportedOSPlatform("windows")]
    private static string? FindSplendidPath()
    {
        using var searcher = new ManagementObjectSearcher("Select * from Win32_SystemDriver WHERE Name='ATKWMIACPIIO'");
        foreach (ManagementBaseObject driver in searcher.Get())
        {
            using (driver)
            {
                string? path = driver["PathName"]?.ToString();
                return path is null ? null : Path.GetDirectoryName(path);
            }
        }

        return null;
    }

    public bool IsGameVisualEnabled()
    {
        if (!OperatingSystem.IsWindows()) return true;

        try { return Registry.GetValue(GameVisualKey, GameVisualValue, 1) is not int status || status > 0; }
        catch { return true; }
    }

    public void SetGameVisualStatus(int status)
    {
        if (!OperatingSystem.IsWindows()) return;

        try { Registry.SetValue(GameVisualKey, GameVisualValue, status, RegistryValueKind.DWord); }
        catch (Exception ex) { Logger.WriteLine("GameVisual status: " + ex.Message); }
    }

    // --- GPU problem detection ----------------------------------------------------------------

    public string? GetGpuError()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try { return DeviceProblems.GetGpuError(); }
        catch (Exception ex)
        {
            Logger.WriteLine("GPU error check: " + ex.Message);
            return null;
        }
    }
}
