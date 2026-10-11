using GHelper.AnimeMatrix.Communication.Platform;
using HidSharp;

namespace GHelper.Peripherals
{
    /// <summary>
    /// Single place where peripherals open their USB/HID channel. The path argument may be a legacy path token
    /// ("mi_01") or a full device path; it is resolved here with <see cref="HidPath"/> so the shared
    /// provider only ever sees a real device path (works for Windows device paths and Linux hidraw nodes).
    /// </summary>
    internal static class PeripheralUsb
    {
        public static UsbProvider Open(ushort vendorId, ushort productId, string pathOrToken, int timeout = 500)
        {
            HidDevice? device;
            try
            {
                device = DeviceList.Local.GetHidDevices(vendorId, productId)
                    .FirstOrDefault(x => HidPath.Matches(x, pathOrToken));
            }
            catch
            {
                device = null;
            }

            if (device is null) throw new IOException("HID device was not found on your machine.");
            return new HidSharpUsbProvider(vendorId, productId, device.DevicePath, timeout);
        }

        /// <summary>Picks the HID node whose feature report is long enough (devices that talk by feature reports).</summary>
        public static UsbProvider OpenByFeatureLength(ushort vendorId, ushort productId, int maxFeatureReportLength, string name)
        {
            return new HidSharpUsbProvider(vendorId, productId, maxFeatureReportLength, name);
        }
    }
}
