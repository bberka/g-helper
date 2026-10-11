using GHelper.Gpu.AMD;
using GHelper.Gpu.NVidia;

namespace GHelper.Gpu;

/// <summary>
/// The legacy HardwareControl.RecreateGpuControl probing order: NVIDIA (NVML) first, then AMD (through the platform's
/// <see cref="IAmdGpuDriver"/>). Platform IGpuProvider.CreateDiscrete() implementations can simply call this.
/// </summary>
public static class GpuControlFactory
{
    /// <summary>A valid discrete GPU control, or null (the caller logs "dGPU not found").</summary>
    public static IGpuControl? CreateDiscrete(IAmdGpuDriver? amdDriver = null)
    {
        if (AppConfig.NoGpu()) return null;

        try
        {
            IGpuControl nvidia = new NvidiaGpuControl();
            if (nvidia.IsValid) return nvidia;
            nvidia.Dispose();

            IGpuControl amd = new AmdGpuControl(amdDriver);
            if (amd.IsValid)
            {
                if (amd.FullName.Contains("6850M")) AppConfig.Set("xgm_special", 1);
                return amd;
            }
            amd.Dispose();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Can't connect to GPU " + ex.Message);
        }

        return null;
    }
}
