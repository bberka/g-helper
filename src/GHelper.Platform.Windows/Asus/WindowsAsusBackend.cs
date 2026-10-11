using System.Management;
using System.Runtime.Versioning;
using GHelper.Platform.Windows.Interop;

namespace GHelper.Platform.Windows.Asus;

/// <summary>
/// ASUS ATK ACPI backend: \\.\ATKACPI DeviceIoControl for DSTS/DEVS/INIT/WDOG, WMI AsusAtkWmi_WMNB as the alternative DEVS
/// path, and AsusAtkWmiEvent (or the ATK4001 named event) for hotkey codes. Lifted from legacy app/AsusACPI.cs.
/// The device is opened lazily so that constructing the platform is free of OS calls.
/// </summary>
public sealed class WindowsAsusBackend : IAsusBackend
{
    private readonly object sync = new();
    private IntPtr handle = Win32.INVALID_HANDLE_VALUE;
    private bool opened;
    private bool disposed;

    private ManagementEventWatcher? watcher;
    private IntPtr atkEventHandle;
    private Thread? atkThread;
    private volatile bool atkStop;
    private bool listening;

    public string Name => "ATKACPI";

    public event Action<int>? AtkEvent;

    public bool IsConnected
    {
        get
        {
            Open();
            return handle != Win32.INVALID_HANDLE_VALUE && handle != IntPtr.Zero;
        }
    }

    private void Open()
    {
        if (opened) return;
        lock (sync)
        {
            if (opened) return;
            opened = true;

            if (!OperatingSystem.IsWindows()) return;

            try
            {
                handle = Win32.CreateFile(
                    AtkAcpiProtocol.DevicePath,
                    Win32.GENERIC_READ | Win32.GENERIC_WRITE,
                    Win32.FILE_SHARE_READ | Win32.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    Win32.OPEN_EXISTING,
                    Win32.FILE_ATTRIBUTE_NORMAL,
                    IntPtr.Zero);
            }
            catch (Exception ex)
            {
                handle = Win32.INVALID_HANDLE_VALUE;
                Logger.WriteLine($"Can't connect to ACPI: {ex.Message}");
            }
        }
    }

    /// <summary>Raw IOCTL; output stays zeroed when the device is missing (same as legacy).</summary>
    private void Control(uint ioControlCode, byte[] inBuffer, byte[] outBuffer)
    {
        if (!IsConnected) return;
        uint returned = 0;
        Win32.DeviceIoControl(handle, ioControlCode, inBuffer, (uint)inBuffer.Length, outBuffer, (uint)outBuffer.Length, ref returned, IntPtr.Zero);
    }

    private byte[] Call(byte[] request, int outSize = AtkAcpiProtocol.OutBufferSize)
    {
        byte[] output = new byte[outSize];
        Control(AtkAcpiProtocol.ControlCode, request, output);
        return output;
    }

    public int DeviceGet(uint deviceId) => AtkAcpiProtocol.DeviceGetValue(Call(AtkAcpiProtocol.BuildDsts(deviceId)));

    public byte[] DeviceGetBuffer(uint deviceId, uint status = 0) => Call(AtkAcpiProtocol.BuildDsts(deviceId, status));

    public byte[] DeviceGetLarge(uint deviceId, int extraIn = 8, int outSize = 64) =>
        Call(AtkAcpiProtocol.BuildLargeDsts(deviceId, extraIn), outSize);

    public int DeviceSet(uint deviceId, int status, string? logName)
    {
        int result = AtkAcpiProtocol.DeviceSetResult(Call(AtkAcpiProtocol.BuildDevs(deviceId, status)));

        if (logName is not null)
            Logger.WriteLine(logName + " = " + status + " : " + (result == 1 ? "OK" : result));

        return result;
    }

    public int DeviceSet(uint deviceId, byte[] parameters, string? logName)
    {
        int result = AtkAcpiProtocol.DeviceSetResult(Call(AtkAcpiProtocol.BuildDevs(deviceId, parameters)));

        if (logName is not null)
            Logger.WriteLine(logName + " = " + BitConverter.ToString(parameters) + " : " + (result == 1 ? "OK" : result));

        return result;
    }

    public int DeviceSetAlternative(uint deviceId, int status)
    {
        if (!OperatingSystem.IsWindows()) return DeviceSet(deviceId, status, null);

        try
        {
            return SetViaWmi(deviceId, status);
        }
        catch (Exception ex)
        {
            Logger.WriteLine("WMI DEVS: " + ex.Message);
            return -1;
        }
    }

    [SupportedOSPlatform("windows")]
    private static int SetViaWmi(uint deviceId, int status)
    {
        using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM AsusAtkWmi_WMNB");
        using var results = searcher.Get();
        foreach (ManagementObject wmi in results.Cast<ManagementObject>())
        {
            using (wmi)
            {
                using var inParams = wmi.GetMethodParameters("DEVS");
                inParams["Device_ID"] = deviceId;
                inParams["Control_status"] = (uint)status;
                using var outParams = wmi.InvokeMethod("DEVS", inParams, null);
                int result = Convert.ToInt32(outParams["result"]);
                Logger.WriteLine("WMI DEVS = " + status + " : " + (result == 1 ? "OK" : result));
                return result;
            }
        }

        return -1;
    }

    public byte[] DeviceInit() => Call(AtkAcpiProtocol.BuildInit());

    public byte[] DeviceWatchDog() => Call(AtkAcpiProtocol.BuildWatchDog());

    // --- events -------------------------------------------------------------------------------

    /// <summary>
    /// Starts delivering <see cref="AtkEvent"/>. Default source is the AsusAtkWmiEvent WMI class (legacy
    /// InputDispatcher.WatcherEventArrived). Config "atk4001_listener" switches to the raw ATK4001 named event of the
    /// ATKACPI driver (needs the ASUS optimization service, legacy AsusACPI.RunListener).
    /// </summary>
    public void StartEventListener()
    {
        if (!OperatingSystem.IsWindows()) return;

        lock (sync)
        {
            if (listening || disposed) return;
            listening = true;
        }

        if (AppConfig.Is("atk4001_listener")) StartAtkEventThread();
        else StartWmiWatcher();
    }

    [SupportedOSPlatform("windows")]
    private void StartWmiWatcher()
    {
        try
        {
            var w = new ManagementEventWatcher
            {
                Scope = new ManagementScope(@"root\wmi"),
                Query = new WqlEventQuery("SELECT * FROM AsusAtkWmiEvent"),
            };
            w.EventArrived += OnWmiEvent;
            w.Start();
            watcher = w;
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Can't connect to ASUS WMI events: " + ex.Message);
        }
    }

    [SupportedOSPlatform("windows")]
    private void OnWmiEvent(object sender, EventArrivedEventArgs e)
    {
        try
        {
            if (e.NewEvent is null) return;
            if (!int.TryParse(e.NewEvent["EventID"]?.ToString(), out int eventId)) return;
            Logger.WriteLine("WMI event " + eventId);
            AtkEvent?.Invoke(eventId);
        }
        catch (Exception ex)
        {
            Logger.WriteLine("WMI event error: " + ex.Message);
        }
    }

    private void StartAtkEventThread()
    {
        if (!IsConnected) return;

        atkEventHandle = Win32.CreateEvent(IntPtr.Zero, false, false, "ATK4001");
        if (atkEventHandle == IntPtr.Zero) return;

        byte[] output = new byte[16];
        Control(AtkAcpiProtocol.EventRegisterControlCode, AtkAcpiProtocol.BuildEventRegistration(atkEventHandle.ToInt64()), output);
        Logger.WriteLine("ACPI event registered: " + BitConverter.ToString(output));

        atkThread = new Thread(() =>
        {
            byte[] buffer = new byte[16];
            while (!atkStop)
            {
                Win32.WaitForSingleObject(atkEventHandle, Timeout.Infinite);
                if (atkStop) break;

                Array.Clear(buffer);
                Control(AtkAcpiProtocol.EventReadControlCode, [], buffer);
                int code = BitConverter.ToInt32(buffer, 0);
                Logger.WriteLine("ACPI Code: " + code);
                try { AtkEvent?.Invoke(code); }
                catch (Exception ex) { Logger.WriteLine("ACPI event error: " + ex.Message); }
            }
        })
        { IsBackground = true, Name = "ATK4001 listener" };
        atkThread.Start();
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
        }

        if (OperatingSystem.IsWindows())
        {
            try { StopWatcher(); } catch { }
        }

        atkStop = true;
        if (atkEventHandle != IntPtr.Zero)
        {
            Win32.SetEvent(atkEventHandle);
            atkThread?.Join(500);
            Win32.CloseHandle(atkEventHandle);
            atkEventHandle = IntPtr.Zero;
        }

        if (handle != Win32.INVALID_HANDLE_VALUE && handle != IntPtr.Zero)
        {
            Win32.CloseHandle(handle);
            handle = Win32.INVALID_HANDLE_VALUE;
        }
    }

    [SupportedOSPlatform("windows")]
    private void StopWatcher()
    {
        if (watcher is null) return;
        watcher.EventArrived -= OnWmiEvent;
        watcher.Stop();
        watcher.Dispose();
        watcher = null;
    }
}
