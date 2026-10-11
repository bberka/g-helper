using GHelper.Gpu;

namespace GHelper.Platform;

/// <summary>
/// Additive contract (platform-windows): NVIDIA overclock / clock-lock operations that legacy code called on the concrete
/// NvidiaGpuControl (NVAPI PerformanceStates20 / ClockBoostLock). The <see cref="IGpuControl"/> returned by
/// <see cref="IGpuProvider.CreateDiscrete"/> implements this when the platform can do it (Windows x64 + NVAPI).
/// </summary>
public interface IGpuOverclock
{
    // allowed ranges (depend on the GPU model, overridable with config keys min/max_gpu_core, min/max_gpu_memory, min_gpu_clock)
    int MinCoreOffset { get; }
    int MaxCoreOffset { get; }
    int MinMemoryOffset { get; }
    int MaxMemoryOffset { get; }
    int MinClockLimit { get; }
    int MaxClockLimit { get; }

    /// <summary>Core / memory clock offset in MHz for the P0 state.</summary>
    bool GetClocks(out int core, out int memory);

    /// <returns>1 = applied, 0 = nothing to do / out of range, -1 = error.</returns>
    int SetClocks(int core, int memory);

    /// <summary>Current max GPU clock lock in MHz, -1 on failure.</summary>
    int GetMaxGpuClock();

    /// <returns>1 = changed, 0 = unchanged.</returns>
    int SetMaxGpuClock(int clock);

    /// <summary>Restart / stop NVIDIA container services (needs elevation); used around GPU mode switches.</summary>
    void RestartNvidiaServices(bool displayContainerToo);
    void StopNvidiaServices();
}

/// <summary>
/// Additive contract (platform-windows): AMD ADL based extras of the legacy AmdGpuControl that are used on the
/// APU / iGPU (ROG Ally FPS limit + auto TDP, OLED power optimisation, iGPU sensors).
/// Returned by <see cref="IGpuProvider.CreateAmdApu"/> (and by CreateDiscrete for AMD dGPUs).
/// </summary>
public interface IAmdApuControl
{
    int GetiGpuPower();
    int? GetiGpuUse();
    (int? temp, int? use, int? gfxPower, int? cpuPower, int? asicPower) GetiGpuSensors();

    bool GetOledPowerOptimization();
    bool DisableOledPowerOptimization();

    bool SetVariBright(int enabled);
    bool GetVariBright(out int supported, out int enabled);

    void StartFps();
    void StopFps();
    float GetFps();
    int GetFpsLimit();
    int SetFpsLimit(int limit);
}
