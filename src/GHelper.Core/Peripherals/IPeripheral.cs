
namespace GHelper.Peripherals
{
    public enum PeripheralType
    {
        Mouse,
        Keyboard,
        Headset
    }

    public interface IPeripheral
    {
        public bool IsDeviceReady { get; }
        public int Battery { get; }
        public bool Charging { get; }

        public bool CanExport();
        public byte[] Export();
        public bool Import(byte[] blob);

        public PeripheralType DeviceType();

        public string GetDisplayName();

        public bool HasBattery();

        public void SynchronizeDevice();

        public void ReadBattery();

        /// <summary>
        /// Name of the app image resource used as the device icon (legacy: Properties.Resources.xxx),
        /// e.g. "icons8_maus_48". Resolved to a bitmap by the UI layer.
        /// </summary>
        public string IconKey();
    }
}
