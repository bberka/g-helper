using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NvAPIWrapper.Native.Display.Structures;

namespace GHelper.Platform.Windows.Display;

/// <summary>
/// ELMB (extreme low motion blur) panel flag, read/written over DPCD register 0x44C through the Intel Control Library
/// (ControlLib.dll) or the undocumented NVAPI DPCD entry point. Lifted from legacy app/Display/ScreenELMB.cs.
/// Both paths are x64 only; everywhere else every call returns -1.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ElmbControl
{
    private const uint DPCD_STATE = 0x44C;

    private Func<int, int>? aux;
    private bool initialized;
    private readonly object sync = new();

    [StructLayout(LayoutKind.Explicit, Size = 36)]
    private struct InitArgs
    {
        [FieldOffset(0)] public uint Size;
        [FieldOffset(8)] public uint AppVersion;
    }

    [StructLayout(LayoutKind.Explicit, Size = 176)]
    private struct AuxArgs
    {
        [FieldOffset(0)] public uint Size;
        [FieldOffset(8)] public uint OpType;
        [FieldOffset(12)] public uint Flags;
        [FieldOffset(16)] public uint Address;
        [FieldOffset(36)] public uint DataSize;
        [FieldOffset(40)] public byte Data;
    }

    [DllImport("ControlLib.dll")]
    private static extern int ctlInit(ref InitArgs args, out IntPtr api);

    [DllImport("ControlLib.dll")]
    private static extern int ctlEnumerateDevices(IntPtr api, ref uint count, [In, Out] IntPtr[]? devices);

    [DllImport("ControlLib.dll")]
    private static extern int ctlEnumerateDisplayOutputs(IntPtr device, ref uint count, [In, Out] IntPtr[]? outputs);

    [DllImport("ControlLib.dll")]
    private static extern int ctlAUXAccess(IntPtr display, ref AuxArgs args);

    private static int IntelAux(IntPtr output, int writeValue)
    {
        bool read = writeValue < 0;
        var args = new AuxArgs
        {
            Size = 176,
            OpType = read ? 1u : 2u,
            Flags = 1,
            Address = DPCD_STATE,
            DataSize = 1,
            Data = read ? (byte)0 : (byte)writeValue,
        };
        if (ctlAUXAccess(output, ref args) != 0) return -1;
        return args.Data;
    }

    private static Func<int, int>? FindPanelIntel()
    {
        var init = new InitArgs { Size = 36, AppVersion = 0x10001 };
        if (ctlInit(ref init, out IntPtr api) != 0) return null;

        uint deviceCount = 0;
        if (ctlEnumerateDevices(api, ref deviceCount, null) != 0 || deviceCount == 0) return null;

        var devices = new IntPtr[deviceCount];
        if (ctlEnumerateDevices(api, ref deviceCount, devices) != 0) return null;

        foreach (var device in devices)
        {
            uint outputCount = 0;
            if (ctlEnumerateDisplayOutputs(device, ref outputCount, null) != 0 || outputCount == 0) continue;

            var outputs = new IntPtr[outputCount];
            if (ctlEnumerateDisplayOutputs(device, ref outputCount, outputs) != 0) continue;

            foreach (var output in outputs)
            {
                if (IntelAux(output, -1) < 0) continue;

                Logger.WriteLine($"ELMB: Intel output {output:X}");
                return value => IntelAux(output, value);
            }
        }

        return null;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x70)]
    private struct NvAuxArgs
    {
        [FieldOffset(0)] public uint Version;
        [FieldOffset(4)] public uint DisplayId;
        [FieldOffset(8)] public uint Command;
        [FieldOffset(12)] public uint Address;
        [FieldOffset(16)] public byte Data;
        [FieldOffset(32)] public uint DataSize;
    }

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NvQueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvDpcdFn(DisplayHandle display, ref NvAuxArgs args, uint flags);

    private static NvDpcdFn? nvDpcd;

    private static int NvAux(DisplayHandle handle, uint displayId, int writeValue)
    {
        bool read = writeValue < 0;
        var args = new NvAuxArgs
        {
            Version = 0x30070,
            DisplayId = displayId,
            Command = read ? 1u : 0u,
            Address = DPCD_STATE,
            DataSize = 4,
            Data = read ? (byte)0 : (byte)writeValue,
        };
        if (nvDpcd!(handle, ref args, 0) != 0) return -1;
        return args.Data;
    }

    private static Func<int, int>? FindPanelNvidia()
    {
        IntPtr fn = NvQueryInterface(0x8EB56969);
        if (fn == IntPtr.Zero) return null;
        nvDpcd = Marshal.GetDelegateForFunctionPointer<NvDpcdFn>(fn);

        foreach (var display in NvAPIWrapper.Display.Display.GetDisplays())
        {
            uint id = display.DisplayDevice.DisplayId;
            if (NvAux(display.Handle, id, -1) < 0) continue;

            Logger.WriteLine($"ELMB: NVIDIA display {id:X}");
            return value => NvAux(display.Handle, id, value);
        }

        return null;
    }

    /// <summary>
    /// Read (<paramref name="value"/> &lt; 0) or write the ELMB DPCD register. The model gate (AppConfig.IsELMB) is applied by
    /// Core's ScreenELMB; this only does the AUX access. -1 when there is no usable Intel / NVIDIA AUX channel.
    /// </summary>
    public int Access(int value)
    {
        if (!Environment.Is64BitProcess || RuntimeInformation.ProcessArchitecture != Architecture.X64) return -1;

        lock (sync)
        {
            if (!initialized)
            {
                initialized = true;

                try { aux = FindPanelIntel(); }
                catch (Exception ex) { Logger.WriteLine($"ELMB Intel: {ex.Message}"); }

                try { aux ??= FindPanelNvidia(); }
                catch (Exception ex) { Logger.WriteLine($"ELMB NVIDIA: {ex.Message}"); }
            }

            return aux == null ? -1 : aux(value);
        }
    }
}
