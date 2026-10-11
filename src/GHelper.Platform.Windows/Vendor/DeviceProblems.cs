using System.Runtime.InteropServices;

namespace GHelper.Platform.Windows.Vendor;

/// <summary>Configuration Manager based "device has a problem" detection for NVIDIA / AMD display adapters (legacy DeviceHelper).</summary>
internal static class DeviceProblems
{
    private const string GUID_DEVCLASS_DISPLAY = "{4D36E968-E325-11CE-BFC1-08002BE10318}";
    private const uint CM_GETIDLIST_FILTER_CLASS = 0x00000200;
    private const uint CM_GETIDLIST_FILTER_PRESENT = 0x00000100;
    private const uint DN_HAS_PROBLEM = 0x00000400;
    private const uint CM_DRP_DEVICEDESC = 0x00000001;

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_List_SizeW(out uint pulLen, string pszFilter, uint ulFlags);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_ListW(string pszFilter, char[] buffer, uint bufferLen, uint ulFlags);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("CfgMgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out uint pulStatus, out uint pulProblemNumber, uint dnDevInst, uint ulFlags);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_Registry_PropertyW(uint dnDevInst, uint ulProperty, out uint pulRegDataType, char[] buffer, ref uint pulLength, uint ulFlags);

    /// <summary>Human readable text of a CM_PROB_* number.</summary>
    public static string ProblemText(uint problem) => problem switch
    {
        1 => "Device is not configured",
        10 => "Device cannot start",
        12 => "Not enough free resources",
        14 => "Restart required",
        18 => "Drivers need to be reinstalled",
        21 => "Device is being removed",
        22 => "Device is disabled",
        28 => "Drivers are not installed",
        31 or 39 => "Driver failed to load",
        32 => "Driver service is disabled",
        37 => "Driver initialization failed",
        38 => "Previous driver still loaded, restart required",
        43 => "Device stopped because it reported problems",
        44 => "Device stopped by an application or service",
        48 => "Driver is blocked from loading",
        52 => "Driver signature cannot be verified",
        54 => "Device is being reset",
        _ => "Unknown problem",
    };

    /// <summary>Only NVIDIA (10DE) and AMD (1002) PCI display adapters are considered.</summary>
    public static bool IsGpuDeviceId(string id) =>
        id.StartsWith("PCI\\VEN_10DE", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("PCI\\VEN_1002", StringComparison.OrdinalIgnoreCase);

    public static string? GetGpuError()
    {
        string? error = null;

        uint flags = CM_GETIDLIST_FILTER_CLASS | CM_GETIDLIST_FILTER_PRESENT;
        if (CM_Get_Device_ID_List_SizeW(out uint len, GUID_DEVCLASS_DISPLAY, flags) == 0 && len > 1)
        {
            var buffer = new char[len];
            if (CM_Get_Device_ID_ListW(GUID_DEVCLASS_DISPLAY, buffer, len, flags) == 0)
            {
                foreach (var id in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!IsGpuDeviceId(id)) continue;
                    if (CM_Locate_DevNodeW(out uint devInst, id, 0) != 0) continue;
                    if (CM_Get_DevNode_Status(out uint status, out uint problem, devInst, 0) != 0) continue;
                    if ((status & DN_HAS_PROBLEM) == 0) continue;

                    var name = new char[256];
                    uint size = 512;
                    string device = CM_Get_DevNode_Registry_PropertyW(devInst, CM_DRP_DEVICEDESC, out _, name, ref size, 0) == 0
                        ? new string(name, 0, (int)size / 2).TrimEnd('\0')
                        : id;
                    error = $"{device}\nError {problem}: {ProblemText(problem)}";
                }
            }
        }

        return error;
    }
}
