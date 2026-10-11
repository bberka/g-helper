using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace GHelper.Platform.Windows;

/// <summary>
/// Windows implementation of <see cref="ISystemInfo"/>: WMI model / BIOS / serial, registry theme and CPU name,
/// token based elevation checks. Everything is lazy and cached; nothing is evaluated at construction.
/// </summary>
public sealed class WindowsSystemInfo : ISystemInfo
{
    private readonly Lazy<string> model;
    private readonly Lazy<(string Bios, string ModelShort)> bios;
    private readonly Lazy<string> serial;
    private readonly Lazy<string?> cpuName;
    private readonly Lazy<bool> elevated;
    private readonly Lazy<bool> isSystem;
    private readonly Lazy<bool> desktop;

    public WindowsSystemInfo()
    {
        model = new(LoadModel, LazyThreadSafetyMode.ExecutionAndPublication);
        bios = new(LoadBios, LazyThreadSafetyMode.ExecutionAndPublication);
        serial = new(LoadSerial, LazyThreadSafetyMode.ExecutionAndPublication);
        cpuName = new(LoadCpuName, LazyThreadSafetyMode.ExecutionAndPublication);
        elevated = new(LoadElevated, LazyThreadSafetyMode.ExecutionAndPublication);
        isSystem = new(LoadIsSystem, LazyThreadSafetyMode.ExecutionAndPublication);
        desktop = new(LoadIsDesktop, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public PlatformKind Kind => PlatformKind.Windows;

    public string OsDescription => RuntimeInformation.OSDescription + " (" + RuntimeInformation.OSArchitecture + ")";

    public string ConfigDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GHelper");

    public string SharedConfigDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GHelper");

    public string GetModel() => model.Value;

    public (string Bios, string ModelShort) GetBiosAndModel() => bios.Value;

    public string GetSerialNumber() => serial.Value;

    public string? GetCpuName() => cpuName.Value;

    public bool IsElevated => elevated.Value;

    public bool IsRunningAsSystem => isSystem.Value;

    public bool IsDesktop => desktop.Value;

    public bool? IsDarkTheme()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is not int value) return null;
            return value <= 0;
        }
        catch
        {
            return null;
        }
    }

    public bool? IsOledPanel()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\ASUS\OLEDCare");
            return key is not null && Convert.ToInt32(key.GetValue("EnablePixelRefresh", 0)) != 0;
        }
        catch
        {
            return null;
        }
    }

    // --- loaders ------------------------------------------------------------------------------

    private static string LoadModel()
    {
        if (!OperatingSystem.IsWindows()) return string.Empty;
        return QueryFirst("SELECT Model FROM Win32_ComputerSystem", "Model") ?? string.Empty;
    }

    private static (string Bios, string ModelShort) LoadBios()
    {
        if (!OperatingSystem.IsWindows()) return (string.Empty, string.Empty);
        return ParseBiosVersion(QueryFirst("SELECT SMBIOSBIOSVersion FROM Win32_BIOS", "SMBIOSBIOSVersion") ?? string.Empty);
    }

    /// <summary>"GA402RJ.320" -> ("320", "GA402RJ"); no dot -> ("", raw).</summary>
    public static (string Bios, string ModelShort) ParseBiosVersion(string raw)
    {
        string[] parts = raw.Split('.');
        return parts.Length > 1 ? (parts[1], parts[0]) : (string.Empty, raw);
    }

    private static string LoadSerial()
    {
        if (!OperatingSystem.IsWindows()) return string.Empty;
        return QueryFirst("SELECT SerialNumber FROM Win32_BIOS", "SerialNumber")?.Trim() ?? string.Empty;
    }

    private static string? LoadCpuName()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString")?.ToString()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static bool LoadElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static bool LoadIsSystem()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.IsSystem;
        }
        catch
        {
            return false;
        }
    }

    private static bool LoadIsDesktop()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            // PCSystemType: 1 = desktop
            string? type = QueryFirst("SELECT PCSystemType FROM Win32_ComputerSystem", "PCSystemType");
            return int.TryParse(type, out int value) && value == 1;
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? QueryFirst(string query, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                using (obj) return obj[property]?.ToString();
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine("WMI " + property + ": " + ex.Message);
        }

        return null;
    }
}
