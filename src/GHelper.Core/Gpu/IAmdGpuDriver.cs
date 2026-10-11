namespace GHelper.Gpu;

/// <summary>Power state of the discrete GPU as far as it can be told without waking it up.</summary>
public enum GpuPowerState
{
    Unknown,
    Active,
    /// <summary>Powered down / runtime suspended (Eco mode idle). Reading sensors would wake it.</summary>
    Asleep,
    /// <summary>Not present or driver not loaded.</summary>
    Off,
}

/// <summary>
/// One PM-log style sample of an AMD adapter, null members are not supported by the adapter.
/// (legacy: ADLPMLogDataOutput sensors PMLOG_*)
/// </summary>
public sealed class AmdSensorSample
{
    public int? TemperatureEdge { get; init; }
    public int? TemperatureGfx { get; init; }
    public int? TemperatureSoc { get; init; }
    public int? GfxActivity { get; init; }
    public int? AsicPower { get; init; }
    public int? GfxPower { get; init; }
    public int? BoardPower { get; init; }
    public int? CpuPower { get; init; }
}

/// <summary>Result of the pre-PMLog "OverdriveN" fallback path used by old Radeon GPUs.</summary>
public readonly record struct AmdLegacySample(int? Temperature, int? Usage, float? Power);

/// <summary>
/// The native half of AmdGpuControl: everything that needs ADL (Windows, amdgpu driver P/Invoke) or amdgpu
/// sysfs/hwmon (Linux). The caching, fallback ordering and policy live in Core's <see cref="AMD.AmdGpuControl"/>.
/// Implementations return null / false / -1 for unsupported features and must not throw for "not supported".
/// </summary>
public interface IAmdGpuDriver : IDisposable
{
    /// <summary>A discrete, present AMD adapter was found.</summary>
    bool HasDiscrete { get; }
    /// <summary>An integrated AMD adapter (APU graphics) was found.</summary>
    bool HasIntegrated { get; }
    /// <summary>Marketing name of the discrete adapter ("AMD Radeon RX 6850M XT").</summary>
    string DiscreteName { get; }

    /// <summary>PM log of the discrete (integrated = false) or integrated adapter.</summary>
    AmdSensorSample? QuerySensors(bool integrated);

    /// <summary>True for GPUs that only support the old Overdrive interface (version &lt; 8).</summary>
    bool IsLegacyOverdrive();
    AmdLegacySample QueryLegacy();

    long? GetTotalVramMb(bool integrated);
    int? GetUsedVramMb(bool integrated);

    // --- iGPU display features ----------------------------------------------------------------
    bool SetVariBright(int enabled);
    bool GetVariBright(out int supported, out int enabled);

    /// <summary>
    /// Display index that supports the OLED power optimisation (SCE) state: -1 when there is none, null when the query
    /// failed (retry later). May throw when the entry point is missing.
    /// </summary>
    int? FindSceDisplay();
    /// <summary>SCE state of the display (&gt; 1 means the optimisation is active). Null on failure.</summary>
    int? GetSceState(int displayIndex);
    bool SetSceState(int displayIndex, int state);

    // --- frame metrics / FPS limit (Ally) -----------------------------------------------------
    void StartFrameMetrics();
    void StopFrameMetrics();
    float GetFrameMetrics();
    /// <summary>Global FPS limit of the iGPU driver, -1 when unavailable.</summary>
    int GetFpsLimit();
    bool SetFpsLimit(int limit);

    /// <summary>Names (without extension) of processes currently using the discrete GPU.</summary>
    IReadOnlyList<string> GetGpuApplications();
}
