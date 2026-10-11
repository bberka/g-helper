using GHelper.Gpu;

namespace GHelper.Platform;

/// <summary>
/// Optional platform backend for the NVIDIA dGPU clock offsets and clock boost lock (Windows: NVAPI
/// PerformanceStates20 / ClockBoostLock, which older drivers support and NVML does not). Implemented on the platform's
/// <see cref="IGpuProvider"/> object; Core's NvidiaGpuControl prefers it and falls back to NVML offsets / locked clocks
/// when it is missing or reports a failure. The allowed ranges, the "nothing to change" checks and the nvidia-smi
/// fallback for the lock stay in Core.
/// </summary>
public interface IGpuOverclock
{
    /// <summary>Core / memory clock offset in MHz of the P0 state. False when the backend cannot read it.</summary>
    bool GetClockOffsets(out int core, out int memory);

    /// <returns>1 = applied, anything else = failed (Core then tries NVML).</returns>
    int SetClockOffsets(int core, int memory);

    /// <summary>Current max GPU clock lock in MHz, 0 when no lock is set, -1 when the backend cannot tell.</summary>
    int GetClockLock();
}
