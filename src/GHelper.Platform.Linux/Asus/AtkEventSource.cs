using System.Collections.Concurrent;

namespace GHelper.Platform.Linux;

/// <summary>Reads the ASUS evdev nodes and raises legacy ATK event ids (see <see cref="AtkEventTranslator"/>).</summary>
internal sealed class AtkEventSource : IDisposable
{
    public const string WmiHotkeysName = "Asus WMI hotkeys";
    public const int AsusUsbVendor = 0x0B05;

    private readonly EvdevMonitor monitor;
    private readonly ConcurrentDictionary<string, int> lastScan = new();
    private readonly bool forwardDesktopHandled;
    private readonly Action<int> raise;

    public AtkEventSource(SysfsRoot root, Action<int> raise, bool forwardDesktopHandled = false, int rescanMs = 10000)
    {
        this.raise = raise;
        this.forwardDesktopHandled = forwardDesktopHandled;
        monitor = new EvdevMonitor(root, IsAsusDevice, OnEvent, rescanMs);
    }

    public static bool IsAsusDevice(InputDeviceInfo info)
    {
        if (info.Name.Equals(WmiHotkeysName, StringComparison.Ordinal)) return true;
        if (info.Name.StartsWith("Asus Keyboard", StringComparison.OrdinalIgnoreCase)) return true;
        return info.Vendor == AsusUsbVendor && info.IsKeyboard;
    }

    public int OpenDeviceCount => monitor.OpenDeviceCount;

    public void Start() => monitor.Start();

    private void OnEvent(InputDeviceInfo device, InputEvent e)
    {
        string node = device.EventNode ?? device.Name;

        if (e.Type == EvCodes.EV_MSC && e.Code == EvCodes.MSC_SCAN)
        {
            lastScan[node] = e.Value;
            return;
        }

        if (e.Type != EvCodes.EV_KEY || e.Value != 1) return;

        int? scan = lastScan.TryRemove(node, out int s) ? s : null;
        bool wmi = device.Name.Equals(WmiHotkeysName, StringComparison.Ordinal);
        int id = AtkEventTranslator.Translate(wmi, scan, e.Code);
        if (id < 0) return;
        if (!forwardDesktopHandled && AtkEventTranslator.IsDesktopHandled(id)) return;

        raise(id);
    }

    public void Dispose() => monitor.Dispose();
}
