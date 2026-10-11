using GHelper.AnimeMatrix.Communication.Platform;

namespace GHelper.Tests.InputUsb;

/// <summary>Records every feature report a matrix / slash device sends.</summary>
internal sealed class FakeUsbProvider : UsbProvider
{
    public List<byte[]> Sets { get; } = new();

    /// <summary>Response to <see cref="Get"/>; default echoes the request like an idle device.</summary>
    public Func<byte[], byte[]>? OnGet { get; set; }

    public FakeUsbProvider() : base(0x0B05, 0x193B)
    {
    }

    public override void Set(byte[] data) => Sets.Add((byte[])data.Clone());

    public override byte[] Get(byte[] data)
        => OnGet is null ? (byte[])data.Clone() : OnGet((byte[])data.Clone());

    public override void Read(byte[] data) { }

    public override void Write(byte[] data) { }

    public override void Dispose() { }
}
