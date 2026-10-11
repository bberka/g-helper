// Stubs for types the core-peripherals task needs but that are owned by other parallel tasks.
// The integrator deletes this file after the owning branches are merged. No real logic here.

using GHelper.Input;

// STUB(core-input-usb): GHelper.AnimeMatrix.Communication.Packet (app/AnimeMatrix/Communication/Packet.cs)
namespace GHelper.AnimeMatrix.Communication
{
    public abstract class Packet
    {
        public byte[] Data { get; } = Array.Empty<byte>();
    }

    // STUB(core-input-usb): GHelper.AnimeMatrix.Communication.Device (app/AnimeMatrix/Communication/Device.cs)
    public abstract class Device : IDisposable
    {
        protected Platform.UsbProvider? _usbProvider;

        protected ushort _vendorId;
        protected ushort _productId;
        protected int _maxFeatureReportLength;

        protected Device(ushort vendorId, ushort productId)
        {
            _vendorId = vendorId;
            _productId = productId;
        }

        protected Device(ushort vendorId, ushort productId, int maxFeatureReportLength)
        {
            _vendorId = vendorId;
            _productId = productId;
            _maxFeatureReportLength = maxFeatureReportLength;
        }

        public ushort VendorID() => _vendorId;
        public ushort ProductID() => _productId;

        public virtual void SetProvider() { }

        public void Set(Packet packet) => _usbProvider?.Set(packet.Data);
        public byte[] Get(Packet packet) => _usbProvider?.Get(packet.Data)!;
        public void Read(byte[] data) => _usbProvider?.Read(data);
        public void Write(byte[] data) => _usbProvider?.Write(data);
        public void Drain(int packetSize) => _usbProvider?.Drain(packetSize);

        public virtual void Dispose() => _usbProvider?.Dispose();
    }
}

namespace GHelper.AnimeMatrix.Communication.Platform
{
    // STUB(core-input-usb): UsbProvider (app/AnimeMatrix/Communication/Platform/UsbProvider.cs)
    public abstract class UsbProvider : IDisposable
    {
        public abstract void Set(byte[] data);
        public abstract byte[] Get(byte[] data);
        public abstract void Read(byte[] data);
        public abstract void Write(byte[] data);
        public virtual void Drain(int packetSize) { }
        public abstract void Dispose();
    }

    // STUB(core-input-usb): HidSharpUsbProvider, the cross-platform replacement of legacy WindowsUsbProvider.
    // Real one: locates the HID node by (vid, pid, device path contains <path>) or by max feature report length.
    internal class HidSharpUsbProvider : UsbProvider
    {
        public HidSharpUsbProvider(ushort vendorId, ushort productId, string path, int timeout = 500) { }
        public HidSharpUsbProvider(ushort vendorId, ushort productId, int maxFeatureReportLength, string name = "Matrix") { }

        public override void Set(byte[] data) => throw new NotImplementedException();
        public override byte[] Get(byte[] data) => throw new NotImplementedException();
        public override void Read(byte[] data) => throw new NotImplementedException();
        public override void Write(byte[] data) => throw new NotImplementedException();
        public override void Dispose() { }
    }
}

namespace GHelper.USB
{
    // STUB(core-input-usb): enum lives in app/USB/Aura.cs
    public enum AuraSpeed : int
    {
        Slow = 0,
        Normal = 1,
        Fast = 2,
    }

    // STUB(core-input-usb): app/USB/XGM.cs
    public static class XGM
    {
        public static void Init() => throw new NotImplementedException();
    }
}

namespace GHelper.Input
{
    // STUB(core-input-usb): app/Input/InputDispatcher.cs
    public class InputDispatcher
    {
        public void Init() => throw new NotImplementedException();
        public void RegisterKeys() => throw new NotImplementedException();
    }
}

namespace GHelper
{
    // STUB(core-engine): the static field lives in app/Program.cs (Program partial in Core)
    public static partial class Program
    {
        public static InputDispatcher? inputDispatcher;
    }
}
