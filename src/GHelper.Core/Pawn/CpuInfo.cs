using GHelper.Platform;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace PawnIO
{
    public static class CpuInfo
    {
        public static readonly bool IsAMD = DetectVendor("AuthenticAMD"u8);
        public static readonly bool IsIntel = DetectVendor("GenuineIntel"u8);

        private static bool DetectVendor(ReadOnlySpan<byte> vendor)
        {
            if (!X86Base.IsSupported) return false;
            var (_, ebx, ecx, edx) = X86Base.CpuId(0, 0);
            return VendorMatches((uint)ebx, (uint)edx, (uint)ecx, vendor);
        }

        /// <summary>The 12 byte CPUID vendor string is EBX, EDX, ECX of leaf 0 ("AuthenticAMD" / "GenuineIntel").</summary>
        public static bool VendorMatches(uint ebx, uint edx, uint ecx, ReadOnlySpan<byte> vendor)
        {
            Span<uint> regs = stackalloc uint[] { ebx, edx, ecx };
            return MemoryMarshal.Cast<uint, byte>(regs).SequenceEqual(vendor);
        }

        private static readonly Lazy<(string Name, string Caption)> _data =
            new Lazy<(string, string)>(Load, LazyThreadSafetyMode.ExecutionAndPublication);

        public static string Name    => _data.Value.Name;
        public static string Caption => _data.Value.Caption;

        // CPU undervolting / temperature limits.
        // Defaults match the original RyzenControl values; can be overridden via AppConfig.
        public static int MinCPUUV   => AppConfig.Get("min_uv",      -40);
        public static int MaxCPUUV   => AppConfig.Get("max_uv",        0);
        public static int MinIGPUUV  => AppConfig.Get("min_igpu_uv", -30);
        public static int MaxIGPUUV  => AppConfig.Get("max_igpu_uv",   0);
        public static int MinTemp    => AppConfig.Get("min_temp",     75);
        public static int DefaultTemp=> AppConfig.Get("max_temp",     96);

        public static bool IsSupportedUV() => IsSupportedUV(Name);

        public static bool IsSupportedUViGPU() => IsSupportedUViGPU(Name);

        public static bool IsSupportedUV(string name)
            => name.Contains("RYZEN AI MAX") ||
               name.Contains("Ryzen AI 9")   ||
               name.Contains("Ryzen 9")      ||
               name.Contains("4900H")        ||
               name.Contains("4800H")        ||
               name.Contains("4600H");

        public static bool IsSupportedUViGPU(string name) => name.Contains("6900H");

        /// <summary>
        /// Decodes the 48 byte processor brand string from CPUID leaves 0x80000002..4
        /// (twelve registers, little endian, NUL padded, often space padded on the left).
        /// </summary>
        public static string DecodeBrandString(ReadOnlySpan<uint> registers)
        {
            var bytes = new byte[registers.Length * 4];
            for (int i = 0; i < registers.Length; i++)
                BitConverter.TryWriteBytes(bytes.AsSpan(i * 4, 4), registers[i]);

            int end = Array.IndexOf(bytes, (byte)0);
            if (end < 0) end = bytes.Length;
            return Encoding.ASCII.GetString(bytes, 0, end).Trim();
        }

        /// <summary>"AMD64 Family 25 Model 116 Stepping 1" like the Windows registry identifier, from CPUID leaf 1.</summary>
        public static string FormatCaption(bool amd, uint eax)
        {
            uint stepping = eax & 0xF;
            uint model = (eax >> 4) & 0xF;
            uint family = (eax >> 8) & 0xF;
            uint extModel = (eax >> 16) & 0xF;
            uint extFamily = (eax >> 20) & 0xFF;

            if (family == 0xF) family += extFamily;
            if (family == 0x6 || family >= 0xF) model |= extModel << 4;

            return $"{(amd ? "AMD64" : "Intel64")} Family {family} Model {model} Stepping {stepping}";
        }

        private static (string Name, string Caption) Load()
        {
            string name = string.Empty;
            string caption = string.Empty;

            try
            {
                if (PlatformServices.IsRegistered)
                    name = PlatformServices.Current.System.GetCpuName()?.Trim() ?? string.Empty;
            }
            catch { }

            try
            {
                if (X86Base.IsSupported)
                {
                    if (name.Length == 0 && X86Base.CpuId(unchecked((int)0x80000000), 0).Eax >= unchecked((int)0x80000004))
                    {
                        var regs = new uint[12];
                        for (int leaf = 0; leaf < 3; leaf++)
                        {
                            var r = X86Base.CpuId(unchecked((int)(0x80000002 + leaf)), 0);
                            regs[leaf * 4] = (uint)r.Eax;
                            regs[leaf * 4 + 1] = (uint)r.Ebx;
                            regs[leaf * 4 + 2] = (uint)r.Ecx;
                            regs[leaf * 4 + 3] = (uint)r.Edx;
                        }
                        name = DecodeBrandString(regs);
                    }

                    caption = FormatCaption(IsAMD, (uint)X86Base.CpuId(1, 0).Eax);
                }
            }
            catch { }

            return (name, caption);
        }
    }
}
