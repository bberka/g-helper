using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace GHelper.Platform.Windows.Pawn;

public enum CpuVendorKind
{
    Unknown,
    Amd,
    Intel,
}

/// <summary>CPUID vendor detection (x86 / x64 only; every other architecture is <see cref="CpuVendorKind.Unknown"/>).</summary>
public static class CpuVendor
{
    private static CpuVendorKind? cached;

    public static CpuVendorKind Current => cached ??= Detect();

    private static CpuVendorKind Detect()
    {
        if (!X86Base.IsSupported) return CpuVendorKind.Unknown;

        try
        {
            var (_, ebx, ecx, edx) = X86Base.CpuId(0, 0);
            return FromRegisters((uint)ebx, (uint)edx, (uint)ecx);
        }
        catch
        {
            return CpuVendorKind.Unknown;
        }
    }

    /// <summary>The 12 byte vendor string is EBX, EDX, ECX of CPUID leaf 0 ("AuthenticAMD" / "GenuineIntel").</summary>
    public static CpuVendorKind FromRegisters(uint ebx, uint edx, uint ecx)
    {
        Span<uint> regs = [ebx, edx, ecx];
        var bytes = MemoryMarshal.Cast<uint, byte>(regs);

        if (bytes.SequenceEqual("AuthenticAMD"u8)) return CpuVendorKind.Amd;
        if (bytes.SequenceEqual("GenuineIntel"u8)) return CpuVendorKind.Intel;
        return CpuVendorKind.Unknown;
    }
}
