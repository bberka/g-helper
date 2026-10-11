using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GHelper.Platform.Windows.Vendor;

/// <summary>
/// What is installed on the machine, for the ASUS driver update list (legacy UpdatesController local driver part):
/// CfgMgr32 present device nodes (driver version, hardware ids, extension INFs), HKLM\SOFTWARE\ASUS DisplayVersion entries,
/// staged oem*.inf versions and MS Store package families. Exposed to Core as <see cref="IDriverInventory"/> by
/// <see cref="WindowsVendorServices"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DriverInventory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    private static readonly DEVPROPKEY DEVPKEY_Device_DeviceDesc = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 2 };
    private static readonly DEVPROPKEY DEVPKEY_Device_HardwareIds = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 3 };
    private static readonly DEVPROPKEY DEVPKEY_Device_DriverVersion = new() { fmtid = new Guid("a8b865dd-2e3d-4094-ad97-e593a70c75d6"), pid = 3 };
    private static readonly DEVPROPKEY DEVPKEY_Device_DriverProvider = new() { fmtid = new Guid("a8b865dd-2e3d-4094-ad97-e593a70c75d6"), pid = 9 };
    private static readonly DEVPROPKEY DEVPKEY_Device_ExtendedConfigurationIds = new() { fmtid = new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), pid = 15 };

    private const uint CM_GETIDLIST_FILTER_PRESENT = 0x00000100;
    private const int CR_BUFFER_SMALL = 0x1A;

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_List_SizeW(out uint pulLen, string? pszFilter, uint ulFlags);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_ListW(string? pszFilter, char[] buffer, uint bufferLen, uint ulFlags);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("CfgMgr32.dll")]
    private static extern int CM_Get_DevNode_PropertyW(uint dnDevInst, in DEVPROPKEY propertyKey, out uint propertyType,
        byte[]? propertyBuffer, ref uint propertyBufferSize, uint ulFlags);

    private static string[] GetPresentDeviceIds()
    {
        if (CM_Get_Device_ID_List_SizeW(out uint len, null, CM_GETIDLIST_FILTER_PRESENT) != 0 || len == 0) return [];
        var buffer = new char[len];
        if (CM_Get_Device_ID_ListW(null, buffer, len, CM_GETIDLIST_FILTER_PRESENT) != 0) return [];
        return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static byte[]? GetDevNodeProperty(uint devInst, DEVPROPKEY key)
    {
        var buffer = new byte[2048];
        uint size = (uint)buffer.Length;
        int cr = CM_Get_DevNode_PropertyW(devInst, key, out _, buffer, ref size, 0);
        if (cr == CR_BUFFER_SMALL)
        {
            buffer = new byte[size];
            cr = CM_Get_DevNode_PropertyW(devInst, key, out _, buffer, ref size, 0);
        }

        if (cr != 0 || size == 0) return null;
        return size == buffer.Length ? buffer : buffer[..(int)size];
    }

    private static string? PropString(byte[]? buffer) => buffer is null ? null : Encoding.Unicode.GetString(buffer).TrimEnd('\0');

    private static string[] PropList(byte[]? buffer) =>
        buffer is null ? [] : Encoding.Unicode.GetString(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Present device driver versions + extension INF entries + HKLM\SOFTWARE\ASUS DisplayVersion entries.</summary>
    public static List<InstalledDriver> Build()
    {
        var list = new List<InstalledDriver>();

        foreach (var deviceId in GetPresentDeviceIds())
        {
            if (CM_Locate_DevNodeW(out uint devInst, deviceId, 0) != 0) continue;

            var version = PropString(GetDevNodeProperty(devInst, DEVPKEY_Device_DriverVersion));
            if (version is not null)
            {
                var desc = PropString(GetDevNodeProperty(devInst, DEVPKEY_Device_DeviceDesc)) ?? "";
                foreach (var hwid in PropList(GetDevNodeProperty(devInst, DEVPKEY_Device_HardwareIds)))
                    list.Add(new InstalledDriver(hwid, version, false, desc));
            }

            foreach (var entry in PropList(GetDevNodeProperty(devInst, DEVPKEY_Device_ExtendedConfigurationIds)))
            {
                // entry: <inf>:<matchId>,<section>,<date>,<version>
                int colon = entry.IndexOf(':');
                if (colon < 0) continue;
                var parts = entry[(colon + 1)..].Split(',');
                if (parts.Length >= 2 && Version.TryParse(parts[^1], out _))
                    list.Add(new InstalledDriver(parts[0], parts[^1], true, entry));
            }
        }

        AddAsusInstalledVersions(list);
        return list;
    }

    private static void AddAsusInstalledVersions(List<InstalledDriver> list)
    {
        try
        {
            using var asus = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\ASUS");
            if (asus is null) return;

            foreach (var name in asus.GetSubKeyNames())
            {
                using var sub = asus.OpenSubKey(name);
                if (sub?.GetValue("DisplayVersion") is string ver && Version.TryParse(ver, out _))
                    list.Add(new InstalledDriver("", ver, true, name));
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.ToString());
        }
    }

    public static string? McuFirmwareVersion()
    {
        const string k = @"HKEY_LOCAL_MACHINE\SOFTWARE\ASUS\FWVersion\ROGMCUFW";
        var raw = (Registry.GetValue(k, "Main FW version", null) ?? Registry.GetValue(k, "FW version", null)) as string;
        return string.IsNullOrEmpty(raw) ? null : raw.Split('.')[^1];
    }

    public static string? FirmwareVersion(string title)
    {
        var key = title.Split('_', ' ')[0];
        if (key.Length < 2) return null;

        foreach (var deviceId in GetPresentDeviceIds())
        {
            if (!deviceId.StartsWith("UEFI\\RES", StringComparison.OrdinalIgnoreCase)) continue;
            if (CM_Locate_DevNodeW(out uint devInst, deviceId, 0) != 0) continue;

            var name = PropString(GetDevNodeProperty(devInst, DEVPKEY_Device_DeviceDesc));
            if (name is null || !name.Contains(key, StringComparison.OrdinalIgnoreCase)) continue;
            if (PropString(GetDevNodeProperty(devInst, DEVPKEY_Device_DriverProvider)) == "Microsoft") continue;

            var version = PropString(GetDevNodeProperty(devInst, DEVPKEY_Device_DriverVersion));
            Logger.WriteLine(name + " " + version + " " + deviceId);
            return version?.Split('.')[^1];
        }

        return null;
    }

    /// <summary>Highest staged (driver store, not necessarily installed) INF version per hardware id.</summary>
    public static Dictionary<string, string> StagedVersions(HashSet<string> hardwares, CancellationToken token)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] files;
        try { files = Directory.GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF"), "oem*.inf"); }
        catch { return map; }

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            string text;
            try
            {
                if (new FileInfo(file).Length > 1024 * 1024) continue;
                text = File.ReadAllText(file);
            }
            catch { continue; }

            if (!hardwares.Any(id => text.Contains(id, StringComparison.OrdinalIgnoreCase))) continue;

            var version = Regex.Match(text, @"DriverVer\s*=[^,\r\n]*,\s*([0-9][0-9.]*)").Groups[1].Value;
            if (!Version.TryParse(version, out var parsed)) continue;

            foreach (var id in hardwares.Where(id => text.Contains(id, StringComparison.OrdinalIgnoreCase)))
                if (!map.TryGetValue(id, out var current) || parsed > Version.Parse(current))
                    map[id] = version;

            Logger.WriteLine("Staged " + Path.GetFileName(file) + " " + version);
        }

        return map;
    }

    public static HashSet<string> StorePackages()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            if (key is not null) foreach (var name in key.GetSubKeyNames()) set.Add(name);
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.ToString());
        }

        return set;
    }
}
