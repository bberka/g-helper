using System.Management;
using System.Runtime.Versioning;

namespace GHelper.Platform.Windows.Vendor;

/// <summary>
/// Folder of the ASUS ATKWMIACPIIO system driver (Win32_SystemDriver PathName). The ASUS System Control Interface tools
/// next to it are AsusSplendid.exe (display colour modes) and AsusHotkey.exe (camera toggle).
/// Legacy: VisualControl.GetSplendidPath / InputDispatcher.GetAsusPath.
/// </summary>
internal static class AsusDriverPath
{
    private static string? cached;

    public static string? Find()
    {
        if (cached is not null || !OperatingSystem.IsWindows()) return cached;

        try
        {
            cached = Query();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("ATKWMIACPIIO path: " + ex.Message);
        }

        return cached;
    }

    [SupportedOSPlatform("windows")]
    private static string? Query()
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
}
