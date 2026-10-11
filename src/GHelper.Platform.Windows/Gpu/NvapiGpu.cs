using System.Diagnostics;
using GHelper.Gpu;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;
using NvAPIWrapper.Native.Interfaces.GPU;
using static NvAPIWrapper.Native.GPU.Structures.PerformanceStates20InfoV1;

namespace GHelper.Platform.Windows.Gpu;

/// <summary>
/// The NVAPI half of the NVIDIA dGPU support (x64 Windows only): "is the GPU powered" without waking it
/// (<see cref="IGpuProvider.GetDiscretePowerState"/>), and the P0 clock offsets / clock boost lock that older drivers only
/// expose through NVAPI (<see cref="IGpuOverclock"/>). Everything else (temperature, power, VRAM, usage, process list,
/// NVML clock offsets as the fallback) is Core's NvidiaGpuControl over NVML.
/// Lifted from legacy app/Gpu/NVidia/NvidiaGpuControl.cs.
/// </summary>
internal sealed class NvapiGpu : IGpuOverclock
{
    private readonly object sync = new();
    private PhysicalGPU? gpu;
    private bool searched;

    /// <summary>NVAPI ships x64 binaries only (legacy app was x64 only as well).</summary>
    public static bool IsSupported =>
        OperatingSystem.IsWindows() && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.X64;

    private PhysicalGPU? InternalGpu
    {
        get
        {
            lock (sync)
            {
                if (!searched)
                {
                    searched = true;
                    gpu = IsSupported ? FindInternalDiscreteGpu() : null;
                }

                return gpu;
            }
        }
    }

    private static PhysicalGPU? FindInternalDiscreteGpu()
    {
        try
        {
            return PhysicalGPU
                .GetPhysicalGPUs()
                .FirstOrDefault(g => g.SystemType == SystemType.Laptop);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            return null;
        }
    }

    /// <summary>
    /// NVAPI_GPU_NOT_POWERED = runtime suspended (Eco mode idle), any other failure = driver gone. Unknown when NVAPI has no
    /// internal GPU so that Core falls back to asking NVML.
    /// </summary>
    public GpuPowerState GetPowerState()
    {
        var internalGpu = InternalGpu;
        if (internalGpu is null) return GpuPowerState.Unknown;

        try
        {
            GPUApi.GetCurrentPerformanceState(internalGpu.Handle);
            return GpuPowerState.Active;
        }
        catch (Exception ex)
        {
            return ex.Message == "NVAPI_GPU_NOT_POWERED" ? GpuPowerState.Asleep : GpuPowerState.Off;
        }
    }

    // --- IGpuOverclock --------------------------------------------------------------------------

    public bool GetClockOffsets(out int core, out int memory)
    {
        core = memory = 0;
        var internalGpu = InternalGpu;
        if (internalGpu is null) return false;

        try
        {
            IPerformanceStates20Info states = GPUApi.GetPerformanceStates20(internalGpu.Handle);
            core = states.Clocks[PerformanceStateId.P0_3DPerformance][0].FrequencyDeltaInkHz.DeltaValue / 1000;
            memory = states.Clocks[PerformanceStateId.P0_3DPerformance][1].FrequencyDeltaInkHz.DeltaValue / 1000;
            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteLine("GET GPU CLOCKS (NVAPI): " + ex.Message);
            core = memory = 0;
            return false;
        }
    }

    public int SetClockOffsets(int core, int memory)
    {
        var internalGpu = InternalGpu;
        if (internalGpu is null) return -1;

        var coreClock = new PerformanceStates20ClockEntryV1(PublicClockDomain.Graphics, new PerformanceStates20ParameterDelta(core * 1000));
        var memoryClock = new PerformanceStates20ClockEntryV1(PublicClockDomain.Memory, new PerformanceStates20ParameterDelta(memory * 1000));

        PerformanceStates20ClockEntryV1[] clocks = { coreClock, memoryClock };
        PerformanceStates20BaseVoltageEntryV1[] voltages = { };
        PerformanceState20[] performanceStates = { new PerformanceState20(PerformanceStateId.P0_3DPerformance, clocks, voltages) };
        var overclock = new PerformanceStates20InfoV1(performanceStates, 2, 0);

        try
        {
            GPUApi.SetPerformanceStates20(internalGpu.Handle, overclock);
            return 1;
        }
        catch (Exception ex)
        {
            Logger.WriteLine("SET GPU CLOCKS (NVAPI): " + ex.Message);
            return -1;
        }
    }

    public int GetClockLock()
    {
        var internalGpu = InternalGpu;
        if (internalGpu is null) return -1;

        try
        {
            PrivateClockBoostLockV2 data = GPUApi.GetClockBoostLock(internalGpu.Handle);
            return (int)data.ClockBoostLocks[0].VoltageInMicroV / 1000;
        }
        catch (Exception ex)
        {
            Logger.WriteLine("GET CLOCK LIMIT (NVAPI): " + ex.Message);
            return -1;
        }
    }

    /// <summary>NvAPI_Unload (legacy HardwareControl.DisposeGpuControl): lets the driver power the GPU down for Eco mode.</summary>
    public static void Unload()
    {
        if (!IsSupported) return;

        int count = 0;
        for (int i = 0; i < 32; i++)
        {
            try { NvAPIWrapper.Native.GeneralApi.Unload(); count++; }
            catch { break; }
        }

        if (count > 0) Logger.WriteLine($"NvAPI Unload: {count}");
    }
}
