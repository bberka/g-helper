namespace GHelper.Platform;

/// <summary>
/// Raw access to the ASUS ATK ACPI/WMI "device" interface (DSTS = get, DEVS = set, keyed by a 32-bit device id).
/// Windows: \\.\ATKACPI DeviceIoControl (see app/AsusACPI.cs).
/// Linux: asus-wmi sysfs attributes (/sys/devices/platform/asus-nb-wmi, platform_profile,
/// firmware-attributes/asus-armoury, hwmon, power_supply) with debugfs (/sys/kernel/debug/asus-nb-wmi
/// dev_id / ctrl_param / devs / dsts) as the raw fallback for ids that have no sysfs equivalent.
/// The semantic level of each member equals the legacy AsusACPI.DeviceXxx methods so that the AsusACPI facade in
/// Core (fan curves, PPT limits, GPU modes, ...) stays platform independent.
/// </summary>
public interface IAsusBackend : IDisposable
{
    string Name { get; }
    bool IsConnected { get; }

    /// <summary>Legacy DeviceGet: DSTS status minus 0x10000. Negative means "unsupported / error".</summary>
    int DeviceGet(uint deviceId);

    /// <summary>Raw DSTS output buffer (&gt;= 16 bytes). First dword is the status, further bytes are payload (fan curves, ...).</summary>
    byte[] DeviceGetBuffer(uint deviceId, uint status = 0);

    /// <summary>DSTS with a larger output buffer (VRAM options, range scan).</summary>
    byte[] DeviceGetLarge(uint deviceId, int extraIn = 8, int outSize = 64);

    /// <summary>DEVS. Returns 1 on success (legacy convention), anything else is a failure code.</summary>
    int DeviceSet(uint deviceId, int status, string? logName);

    int DeviceSet(uint deviceId, byte[] parameters, string? logName);

    /// <summary>Alternative DEVS path (Windows: WMI AsusAtkWmi_WMNB). Platforms without one forward to DeviceSet.</summary>
    int DeviceSetAlternative(uint deviceId, int status);

    byte[] DeviceInit();
    byte[] DeviceWatchDog();

    /// <summary>
    /// ASUS hotkey / ATK event codes (Fn keys, ROG key, mode key, ...): the values legacy code got from
    /// AsusAtkWmiEvent / the ATK4001 event. Raised on an arbitrary thread.
    /// </summary>
    event Action<int>? AtkEvent;

    /// <summary>Begin delivering <see cref="AtkEvent"/>. Safe to call once.</summary>
    void StartEventListener();
}
