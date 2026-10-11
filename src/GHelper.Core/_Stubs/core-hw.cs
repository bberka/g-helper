// Stubs for types owned by other parallel tasks, only what core-hw (Gpu/Ally/Pawn/Display/Overlay) compiles against.
// The integrator deletes src/GHelper.Core/_Stubs after merging the real types. No logic in here.
using GHelper.Gpu;
using HidSharp;

// STUB(core-engine): app/HardwareControl.cs (src/GHelper.Core/HardwareControl.cs)
public static class HardwareControl
{
    public static IGpuControl? GpuControl;

    public static float? cpuTemp = -1;
    public static float? gpuTemp = -1;
    public static float? cpuPower;
    public static float? gpuPower;
    public static decimal? batteryRate = 0;
    public static int? cpuFanRPM;
    public static int? gpuFanRPM;
    public static int? cpuUsage;
    public static int? gpuUsage;
    public static int? vramUsage;
    public static int? ramUsage;
    public static int? vramUsedMb;
    public static int? ramUsedMb;

    public static bool readFans;
    public static bool readUsage;
    public static bool readMemory;
    public static bool readPower;
    public static bool readBattery;

    public static float? GetCPUTemp() => null;
    public static void ResetCPUPowerCounter() { }
    public static void ReadSensorsOverlay() { }
    public static double GetBatteryChargePercentage() => 0;
    public static bool IsUsedGPU(int threshold = 10) => false;
    public static GHelper.Gpu.AMD.AmdGpuControl AmdApu() => default!;
    public static void DisposeGpuControl() { }
    public static void KillGPUApps() { }
    public static void RecreateGpuControl() { }
    public static Task RecreateGpuControlWithRetry(int retries, int delay) => Task.CompletedTask;
}

namespace GHelper.Mode
{
    // STUB(core-engine): app/Mode/ModeControl.cs
    public class ModeControl
    {
        public void WaitForApply() { }
        public void AutoPerformance(bool powerChanged = false) { }
        public void SetPerformanceMode(int mode = -1, bool notify = false) { }
        public void SetGPUClocks(bool launchAsAdmin = true, bool reset = false) { }
    }
}

namespace GHelper
{
    // STUB(core-engine): Program.modeControl (legacy app/Program.cs)
    public static partial class Program
    {
        public static GHelper.Mode.ModeControl modeControl = default!;
    }
}

namespace GHelper.USB
{
    // STUB(core-input-usb): app/USB/XGM.cs
    public static class XGM
    {
        public static void Init() { }
        public static void Reset() { }
        public static void SetFan(byte[] curve) { }
    }

    // STUB(core-input-usb): app/USB/Aura.cs
    public static class Aura
    {
        public static class CustomRGB
        {
            public static void ApplyGPUColor(int gpuMode = -1) { }
        }
    }

    // STUB(core-input-usb): app/USB/AsusHid.cs
    public static class AsusHid
    {
        public const byte INPUT_ID = 0x5a;
        public static HidStream? FindHidStream(byte reportId) => null;
        public static void WriteInput(byte[] data, string? log = "USB") { }
        public static void InitInput(string? log = "Input Init") { }
    }
}

namespace GHelper.Input
{
    // STUB(core-input-usb): app/Input/InputDispatcher.cs
    public static class InputDispatcher
    {
        public static int GetBacklight() => 0;
        public static void SetBacklightAuto() { }
        public static void SetBacklight(int delta, bool force = false) { }
    }
}
