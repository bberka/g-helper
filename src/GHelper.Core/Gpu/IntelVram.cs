using GHelper.Platform;

namespace GHelper.Gpu;

/// <summary>
/// Intel iGPU dedicated-VRAM size option (Windows: registry DedicatedSegmentSize). Facade over
/// <see cref="IGpuProvider"/>; platforms without the feature report Supported == false.
/// </summary>
public static class IntelVram
{
    private static bool _supported;

    public static bool Supported => _supported;

    public static int[] Sizes => _supported ? PlatformServices.Current.Gpu.IntelVramSizes : [];

    public static int Index => _supported ? PlatformServices.Current.Gpu.IntelVramIndex : 0;

    public static bool Init()
    {
        try
        {
            _supported = PlatformServices.IsRegistered && PlatformServices.Current.Gpu.InitIntelVram();
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Intel VRAM init failed: {ex.Message}");
            _supported = false;
        }

        return _supported;
    }

    public static void SetIndex(int index)
    {
        if (!_supported) return;

        try
        {
            PlatformServices.Current.Gpu.SetIntelVramIndex(index);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Failed to set Intel VRAM size: {ex.Message}");
        }
    }
}
