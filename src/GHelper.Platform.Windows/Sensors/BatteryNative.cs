using System.Runtime.InteropServices;
using GHelper.Platform.Windows.Interop;

namespace GHelper.Platform.Windows.Sensors;

/// <summary>Raw battery access: CallNtPowerInformation(SystemBatteryState) and the battery class IOCTLs (legacy HardwareControl).</summary>
internal static class BatteryNative
{
    private const int SystemBatteryStateLevel = 5;
    private const uint DIGCF_PRESENT = 0x02;
    private const uint DIGCF_DEVICEINTERFACE = 0x10;
    private const uint IOCTL_BATTERY_QUERY_TAG = 0x294040;
    private const uint IOCTL_BATTERY_QUERY_STATUS = 0x29404C;

    private static readonly Guid GUID_DEVINTERFACE_BATTERY = new("72631E54-78A4-11D0-BCF7-00AA00B7B32A");

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_BATTERY_STATE
    {
        [MarshalAs(UnmanagedType.U1)] public bool AcOnLine;
        [MarshalAs(UnmanagedType.U1)] public bool BatteryPresent;
        [MarshalAs(UnmanagedType.U1)] public bool Charging;
        [MarshalAs(UnmanagedType.U1)] public bool Discharging;
        public byte Spare1;
        public byte Spare2;
        public byte Spare3;
        public byte Spare4;
        public uint MaxCapacity;
        public uint RemainingCapacity;
        public int Rate;
        public uint EstimatedTime;
        public uint DefaultAlert1;
        public uint DefaultAlert2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BATTERY_WAIT_STATUS
    {
        public uint BatteryTag;
        public uint Timeout;
        public uint PowerState;
        public uint LowCapacity;
        public uint HighCapacity;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BATTERY_STATUS
    {
        public uint PowerState;
        public uint Capacity;
        public int Voltage;
        public int Rate;
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint CallNtPowerInformation(int informationLevel, IntPtr inputBuffer, uint inputBufferLength, IntPtr outputBuffer, uint outputBufferLength);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    private static extern bool DeviceIoControlTag(IntPtr hDevice, uint dwIoControlCode, ref uint lpInBuffer, uint nInBufferSize, ref uint lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    private static extern bool DeviceIoControlStatus(IntPtr hDevice, uint dwIoControlCode, ref BATTERY_WAIT_STATUS lpInBuffer, uint nInBufferSize, ref BATTERY_STATUS lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    private static string? batteryDevicePath;

    public static SYSTEM_BATTERY_STATE? GetSystemBatteryState()
    {
        int size = Marshal.SizeOf<SYSTEM_BATTERY_STATE>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            uint status = CallNtPowerInformation(SystemBatteryStateLevel, IntPtr.Zero, 0, ptr, (uint)size);
            return status == 0 ? Marshal.PtrToStructure<SYSTEM_BATTERY_STATE>(ptr) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static string? GetBatteryDevicePath()
    {
        if (batteryDevicePath != null) return batteryDevicePath;

        Guid batteryGuid = GUID_DEVINTERFACE_BATTERY;
        IntPtr deviceInfoSet = SetupDiGetClassDevs(ref batteryGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (deviceInfoSet == Win32.INVALID_HANDLE_VALUE) return null;

        try
        {
            SP_DEVICE_INTERFACE_DATA did = new() { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };

            if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref batteryGuid, 0, ref did)) return null;

            SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref did, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);
            if (requiredSize == 0) return null;

            IntPtr detailData = Marshal.AllocHGlobal((int)requiredSize);
            try
            {
                Marshal.WriteInt32(detailData, IntPtr.Size == 8 ? 8 : 6);

                if (!SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref did, detailData, requiredSize, out _, IntPtr.Zero))
                    return null;

                batteryDevicePath = Marshal.PtrToStringAuto(detailData + 4);
                return batteryDevicePath;
            }
            finally
            {
                Marshal.FreeHGlobal(detailData);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    public static BATTERY_STATUS? QueryBatteryStatus()
    {
        string? devicePath = GetBatteryDevicePath();
        if (devicePath == null) return null;

        IntPtr handle = Win32.CreateFile(devicePath, Win32.GENERIC_READ | Win32.GENERIC_WRITE,
            Win32.FILE_SHARE_READ | Win32.FILE_SHARE_WRITE, IntPtr.Zero, Win32.OPEN_EXISTING, Win32.FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

        if (handle == Win32.INVALID_HANDLE_VALUE) return null;

        try
        {
            uint timeout = 0;
            uint batteryTag = 0;
            if (!DeviceIoControlTag(handle, IOCTL_BATTERY_QUERY_TAG, ref timeout, 4, ref batteryTag, 4, out _, IntPtr.Zero) || batteryTag == 0)
                return null;

            BATTERY_WAIT_STATUS waitStatus = new() { BatteryTag = batteryTag };
            BATTERY_STATUS status = new();

            if (!DeviceIoControlStatus(handle, IOCTL_BATTERY_QUERY_STATUS,
                ref waitStatus, (uint)Marshal.SizeOf<BATTERY_WAIT_STATUS>(),
                ref status, (uint)Marshal.SizeOf<BATTERY_STATUS>(),
                out _, IntPtr.Zero))
                return null;

            return status;
        }
        finally
        {
            Win32.CloseHandle(handle);
        }
    }
}
