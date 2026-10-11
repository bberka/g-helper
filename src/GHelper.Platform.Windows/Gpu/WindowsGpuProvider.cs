using System.Runtime.InteropServices;
using GHelper.Gpu;

namespace GHelper.Platform.Windows.Gpu;

/// <summary>
/// GPU telemetry / tuning backends: NVAPI + NVML for NVIDIA dGPUs, ADL2 for AMD dGPUs and APUs, registry for the Intel
/// iGPU VRAM size. NVAPI is x64 only: on other architectures <see cref="CreateDiscrete"/> never returns an NVIDIA control
/// and no native NVIDIA library is touched.
/// </summary>
public sealed class WindowsGpuProvider : IGpuProvider
{
    private IntelVram? intelVram;

    /// <summary>NVAPI / NVML ship x64 binaries only (legacy app was x64 only as well).</summary>
    public static bool IsNvapiSupported =>
        OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64;

    /// <summary>ADL is available for x64 and arm64 Windows.</summary>
    public static bool IsAdlSupported =>
        OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;

    /// <summary>Optional CPU temperature source for the NVIDIA polling heuristic (set by Core / the app head).</summary>
    public Func<float?>? CpuTempProvider { get; set; }

    // ASUS ACPI temperature first (what legacy HardwareControl.GetCPUTemp reads), OS thermal zone as the fallback
    private static float? DefaultCpuTemp()
    {
        int acpi = Program.acpi?.DeviceGet(AsusACPI.Temp_CPU) ?? -1;
        if (acpi > 0) return acpi;
        return PlatformServices.Current.Sensors.GetCpuTemp();
    }

    public IGpuControl? CreateDiscrete()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (AppConfig.NoGpu()) return null;

        try
        {
            if (IsNvapiSupported)
            {
                var nvidia = new NvidiaGpuControl { CpuTempProvider = CpuTempProvider ?? DefaultCpuTemp };
                if (nvidia.IsValid)
                {
                    Logger.WriteLine(nvidia.FullName);
                    return nvidia;
                }

                nvidia.Dispose();
            }

            if (IsAdlSupported)
            {
                var amd = new AmdGpuControl();
                if (amd.IsValid)
                {
                    if (amd.FullName.Contains("6850M")) AppConfig.Set("xgm_special", 1);
                    Logger.WriteLine(amd.FullName);
                    return amd;
                }

                amd.Dispose();
            }

            Logger.WriteLine("dGPU not found");
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Can't connect to GPU " + ex.Message);
        }

        return null;
    }

    public IGpuControl? CreateAmdApu()
    {
        if (!IsAdlSupported) return null;

        try
        {
            var amd = new AmdGpuControl();
            if (amd.HasAdlContext) return amd;
            amd.Dispose();
        }
        catch (Exception ex)
        {
            Logger.WriteLine("AMD APU control: " + ex.Message);
        }

        return null;
    }

    // --- Intel VRAM ---------------------------------------------------------------------------

    public bool IntelVramSupported => intelVram?.Supported ?? false;

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

    // --- lifetime -----------------------------------------------------------------------------

    public void UnloadNative()
    {
        if (!IsNvapiSupported) return;

        try { NvidiaGpuControl.UnloadNative(); }
        catch (Exception ex) { Logger.WriteLine("GPU unload: " + ex.Message); }
    }
}
