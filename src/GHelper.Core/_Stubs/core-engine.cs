// STUBS created by W1-A core-engine for types owned by other parallel tasks.
// Signatures mirror the legacy WinForms app (app/). They contain NO logic; the integrator deletes this file
// (and the whole _Stubs folder) after merging the real implementations.

using GHelper.Ally;
using GHelper.USB;

// ------------------------------------------------------------------------------------------------
// STUB(core-hw): Gpu/, Display/, Overlay/, Ally/, Pawn/
// ------------------------------------------------------------------------------------------------
namespace GHelper.Gpu
{
    // STUB(core-hw)
    public class GPUModeControl
    {
        public static bool suspended = false;

        public void InitGPUMode() { }
        public bool AutoGPUMode(bool optimized = false, int delay = 0) => false;
        public void CaptureNvBootState() { }
        public void CheckStandardHalfState() { }
        public void StandardModeFix() { }
    }
}

namespace GHelper.Gpu.NVidia
{
    // STUB(core-hw)
    public class NvidiaGpuControl : IGpuControl
    {
        public bool IsNvidia => false;
        public bool IsValid => false;
        public string FullName => "";
        public int? GetCurrentTemperature() => null;
        public int? GetGpuUse() => null;
        public (long usedMb, long totalMb)? GetVramInfo() => null;
        public float? GetGpuPower() => null;
        public void KillGPUApps() { }
        public void Dispose() { }

        public int SetClocks(int core, int memory) => 0;
        public int SetMaxGPUClock(int clock) => 0;
    }
}

namespace GHelper.Gpu.AMD
{
    // STUB(core-hw)
    public class AmdGpuControl : IGpuControl
    {
        public bool IsNvidia => false;
        public bool IsValid => false;
        public string FullName => "";
        public int? GetCurrentTemperature() => null;
        public int? GetGpuUse() => null;
        public (long usedMb, long totalMb)? GetVramInfo() => null;
        public float? GetGpuPower() => null;
        public void KillGPUApps() { }
        public void Dispose() { }

        public int GetiGpuPower() => 0;
        public (int? temp, int? use, int? gfxPower, int? cpuPower, int? asicPower) GetiGpuSensors() => (null, null, null, null, null);
    }
}

namespace GHelper.Display
{
    // STUB(core-hw)
    public static class ScreenControl
    {
        public static void AutoScreen(bool force = false) { }
        public static void InitMiniled() { }
        public static void InitOptimalBrightness() { }
        public static void InitScreen() { }
    }

    // STUB(core-hw)
    public static class VisualControl
    {
        public static void InitBrightness() { }
    }

    // STUB(core-hw)
    public static class ColorProfileHelper
    {
        public static Task InstallProfile() => Task.CompletedTask;
    }
}

namespace GHelper.Overlay
{
    // STUB(core-hw)
    public class HardwareOverlay
    {
        public void StartOverlay() { }
        public void StopOverlay() { }
        public void SuspendForDisplayOff() { }
        public void ResumeForDisplayOn() { }
    }
}

namespace GHelper.Ally
{
    // STUB(core-hw)
    public enum ControllerMode : int
    {
        Auto = 0,
        Gamepad = 1,
        WASD = 2,
        Mouse = 3,
        Skip = -1,
    }

    // STUB(core-hw)
    public class AllyControl
    {
        public void Init() { }
        public static void ApplyMode(ControllerMode applyMode = ControllerMode.Auto, bool init = false) { }
    }
}

namespace PawnIO
{
    // STUB(core-hw)
    public static class CpuInfo
    {
        public static readonly bool IsAMD = false;

        public static int MinCPUUV => AppConfig.Get("min_uv", -40);
        public static int MaxCPUUV => AppConfig.Get("max_uv", 0);
        public static int MinIGPUUV => AppConfig.Get("min_igpu_uv", -30);
        public static int MaxIGPUUV => AppConfig.Get("max_igpu_uv", 0);
        public static int MinTemp => AppConfig.Get("min_temp", 75);
        public static int DefaultTemp => AppConfig.Get("max_temp", 96);

        public static bool IsSupportedUV() => false;
        public static bool IsSupportedUViGPU() => false;
    }
}

// ------------------------------------------------------------------------------------------------
// STUB(core-input-usb): USB/, Input/, AnimeMatrix/
// ------------------------------------------------------------------------------------------------
namespace GHelper.Input
{
    // STUB(core-input-usb)
    public class InputDispatcher
    {
        public static bool lidClose = false;

        public void Init() { }

        public static void AutoKeyboard() { }
        public static void StartupBacklight() { }
        public static void InitFNLock() { }
        public static int GetBacklight() => 0;
        public static void InitScreenpad() { }
        public static void InitStatusLed() { }
        public static void ShutdownStatusLed() { }
    }

    // STUB(core-input-usb)
    public static class NumberPad
    {
        public static void Init() { }
    }
}

namespace GHelper.USB
{
    // STUB(core-input-usb)
    public enum AuraSpeed : int
    {
        Slow = 0,
        Normal = 1,
        Fast = 2,
    }

    // STUB(core-input-usb)
    public static class XGM
    {
        public static void Init() { }
        public static void Reset() { }
        public static void SetFan(byte[] curve) { }
        public static void NotifyShutdown() { }
    }

    // STUB(core-input-usb)
    public class Aura
    {
        public static bool sessionLock = false;
        public static bool IsBacklightDetected => false;

        public static AuraMode Mode { get; set; }
        public static AuraSpeed Speed { get; set; }

        public static void Init() { }
        public static void SetColor(int colorCode) { }
        public static void SetColor2(int colorCode) { }
        public static void ApplyAura() { }
        public static void ApplyBrightness(int brightness, string log = "Backlight") { }
        public static void SleepBrightness() { }
    }

    // STUB(core-input-usb)
    public static class AsusHid
    {
        public const byte AURA_ID = 0x5d;

        public static void Write(List<byte[]> dataList, string log = "USB", int[]? pids = null) { }
    }
}

namespace GHelper.AnimeMatrix
{
    // STUB(core-input-usb)
    public class AniMatrixControl
    {
        public static bool lidClose = false;

        public bool IsValid => false;
        public bool IsSlash => false;

        public void SetDevice(bool wakeUp = false) { }
        public void SetMatrix(bool wakeUp = false) { }
        public void SetLidMode(bool force = false) { }
    }
}

// ------------------------------------------------------------------------------------------------
// STUB(core-peripherals): Peripherals/
// ------------------------------------------------------------------------------------------------
namespace GHelper.Peripherals
{
    // STUB(core-peripherals)
    public class PeripheralsProvider
    {
        public static void DetectAllAsusMice() { }
        public static void DetectAllAsusKeyboards() { }
        public static void DetectAllAsusHeadsets() { }
        public static void RegisterForDeviceEvents() { }
        public static void UnregisterForDeviceEvents() { }
    }
}
