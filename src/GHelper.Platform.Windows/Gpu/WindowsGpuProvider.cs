using GHelper.Gpu;
using GHelper.Gpu.AMD;
using GHelper.Gpu.NVidia;

namespace GHelper.Platform.Windows.Gpu;

/// <summary>
/// The OS specific half of the GPU stack. The control objects themselves are Core's: <c>NvidiaGpuControl</c> (NVML)
/// and <c>AmdGpuControl</c> over the <see cref="IAmdGpuDriver"/> this provider hands out (ADL2). What only Windows can add
/// is supplied through the contract members:
/// <list type="bullet">
/// <item><see cref="GetDiscretePowerState"/> via NVAPI (NVAPI_GPU_NOT_POWERED), so polling never wakes the dGPU;</item>
/// <item><see cref="IGpuOverclock"/> via NVAPI (P0 clock offsets and clock boost lock), preferred over NVML;</item>
/// <item><see cref="CreateAmdDriver"/> via ADL2 (sensors, VRAM, VariBright, OLED SCE, FPS limit, GPU applications);</item>
/// <item>Intel iGPU VRAM size override from the registry.</item>
/// </list>
/// NVAPI is x64 only, ADL x64 and arm64: elsewhere those parts report "unsupported" and no native library is touched.
/// </summary>
public sealed class WindowsGpuProvider : IGpuProvider, IGpuOverclock
{
    private IntelVram? intelVram;
    private NvapiGpu? nvapi;
    private readonly object nvapiLock = new();

    /// <summary>NVAPI ships x64 binaries only (legacy app was x64 only as well).</summary>
    public static bool IsNvapiSupported => NvapiGpu.IsSupported;

    /// <summary>ADL is available for x64 and arm64 Windows.</summary>
    public static bool IsAdlSupported => AdlAmdDriver.IsSupported;

    private NvapiGpu? Nvapi
    {
        get
        {
            if (!IsNvapiSupported) return null;
            lock (nvapiLock) return nvapi ??= new NvapiGpu();
        }
    }

    // --- control objects (Core's) ---------------------------------------------------------------

    public IGpuControl? CreateDiscrete()
    {
        if (!OperatingSystem.IsWindows()) return null;

        // NVIDIA through NVML first, then AMD through ADL (the driver is only opened when NVIDIA was not found)
        var control = GpuControlFactory.CreateDiscrete(CreateAmdDriver);
        if (control is not null) Logger.WriteLine(control.FullName);
        else Logger.WriteLine("dGPU not found");
        return control;
    }

    public IGpuControl? CreateAmdApu()
    {
        if (!IsAdlSupported) return null;

        try
        {
            var driver = AdlAmdDriver.TryCreate();
            if (driver is null) return null;

            if (!driver.HasIntegrated && !driver.HasDiscrete)
            {
                driver.Dispose();
                return null;
            }

            return new AmdGpuControl(driver);
        }
        catch (Exception ex)
        {
            Logger.WriteLine("AMD APU control: " + ex.Message);
            return null;
        }
    }

    public IAmdGpuDriver? CreateAmdDriver()
    {
        try { return AdlAmdDriver.TryCreate(); }
        catch (Exception ex)
        {
            Logger.WriteLine("ADL: " + ex.Message);
            return null;
        }
    }

    // --- NVAPI ----------------------------------------------------------------------------------

    public GpuPowerState GetDiscretePowerState()
    {
        try { return Nvapi?.GetPowerState() ?? GpuPowerState.Unknown; }
        catch { return GpuPowerState.Unknown; }
    }

    bool IGpuOverclock.GetClockOffsets(out int core, out int memory)
    {
        core = memory = 0;
        return Nvapi?.GetClockOffsets(out core, out memory) ?? false;
    }

    int IGpuOverclock.SetClockOffsets(int core, int memory) => Nvapi?.SetClockOffsets(core, memory) ?? -1;

    int IGpuOverclock.GetClockLock() => Nvapi?.GetClockLock() ?? -1;

    // --- Intel VRAM -----------------------------------------------------------------------------

    public int[] IntelVramSizes => intelVram?.Sizes ?? [];

    public int IntelVramIndex => intelVram?.Index ?? 0;

    public bool InitIntelVram()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            intelVram ??= new IntelVram();
            return intelVram.Init();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Intel VRAM: " + ex.Message);
            return false;
        }
    }

    public void SetIntelVramIndex(int index)
    {
        if (!OperatingSystem.IsWindows()) return;
        intelVram?.SetIndex(index);
    }

    // --- lifetime -------------------------------------------------------------------------------

    /// <summary>NVML shutdown + NvAPI_Unload (legacy HardwareControl.DisposeGpuControl).</summary>
    public void UnloadNative()
    {
        if (!IsNvapiSupported) return;

        try
        {
            NvmlHelper.Shutdown();
            NvapiGpu.Unload();
            lock (nvapiLock) nvapi = null; // handles are invalid after the unload
        }
        catch (Exception ex)
        {
            Logger.WriteLine("GPU unload: " + ex.Message);
        }
    }
}
