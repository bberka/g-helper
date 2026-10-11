// Minimal placeholders for types owned by other W1 tasks that the input / USB / matrix code compiles against.
// Signatures mirror the legacy app/ types. No logic. The integrator deletes src/GHelper.Core/_Stubs after merging.
#pragma warning disable CS0067, CS8618, CS0649

using System.Drawing;

namespace GHelper
{
    public static partial class Program
    {
        // STUB(core-engine): Program.cs orchestration members
        public static GHelper.Mode.ModeControl modeControl = default!;
        public static GHelper.Input.InputDispatcher? inputDispatcher;
        public static void OnChargerEvent() => throw new NotImplementedException();

        // STUB(core-hw)
        public static GHelper.Gpu.GPUModeControl gpuControl = default!;
        public static GHelper.Ally.AllyControl allyControl = default!;
        public static GHelper.Overlay.HardwareOverlay? hardwareOverlay;
    }
}

namespace GHelper.Mode
{
    // STUB(core-engine)
    public class ModeControl
    {
        public void SetPerformanceMode(int mode = -1, bool notify = false) => throw new NotImplementedException();
        public void CyclePerformanceMode(bool back = false) => throw new NotImplementedException();
    }
}

namespace GHelper.Gpu
{
    // STUB(core-hw)
    public class GPUModeControl
    {
        public static int gpuMode;
        public void ToggleXGM(bool silent = false) => throw new NotImplementedException();
        public void SetGPUMode(int gpuMode, int auto = 0) => throw new NotImplementedException();
    }
}

namespace GHelper.Ally
{
    // STUB(core-hw)
    public class AllyControl
    {
        public void ToggleModeHotkey() => throw new NotImplementedException();
        public void ToggleFPSLimit(bool toast = false) => throw new NotImplementedException();
    }
}

namespace GHelper.Overlay
{
    // STUB(core-hw)
    public class HardwareOverlay
    {
        public void SetDragKey(bool down) => throw new NotImplementedException();
    }
}

namespace GHelper.Display
{
    // STUB(core-hw)
    public static class ScreenBrightness
    {
        public static int Get() => throw new NotImplementedException();
        public static void Set(int brightness) => throw new NotImplementedException();
        public static int Adjust(int delta) => throw new NotImplementedException();
    }

    // STUB(core-hw)
    public static class ScreenCCD
    {
        public static bool GetHDRStatus(bool log = true) => throw new NotImplementedException();
    }

    // STUB(core-hw)
    public static class ScreenControl
    {
        public static void ToggleScreenRate() => throw new NotImplementedException();
        public static string ToogleMiniled() => throw new NotImplementedException();
    }

    // STUB(core-hw)
    public static class VisualControl
    {
        public static int SetBrightness(int brightness = -1, int delta = 0) => throw new NotImplementedException();
    }
}

namespace GHelper.Helpers
{
    // STUB(core-engine): legacy ToastForm.cs enum (the toast window itself is UI)
    public enum ToastIcon
    {
        BrightnessUp,
        BrightnessDown,
        BacklightUp,
        BacklightDown,
        Touchpad,
        Microphone,
        MicrophoneMute,
        FnLock,
        Battery,
        Charger,
        Controller
    }

    // STUB(core-engine): facade over IVendorServices
    public static class AsusService
    {
        public static bool IsAsusOptimizationRunning() => throw new NotImplementedException();
        public static bool IsOSDRunning() => throw new NotImplementedException();
    }

    // STUB(core-engine): facade over IAudioService
    public static class Audio
    {
        public static void SubscribeMute(Action<bool> onMuteChange) => throw new NotImplementedException();
        public static bool ToggleMicMute() => throw new NotImplementedException();
        public static bool IsMicMuted() => throw new NotImplementedException();
        public static bool IsMuted() => throw new NotImplementedException();
    }

    // STUB(core-engine): facade over IProcessService
    public static class ProcessHelper
    {
        public static string RunCMD(string name, string args, string? directory = null, int timeoutMs = 0) => throw new NotImplementedException();
    }

    // STUB(core-engine)
    public static class RestrictedProcessHelper
    {
        public static void RunAsRestrictedUser(string command) => throw new NotImplementedException();
    }

    // STUB(core-engine): facade over IInputService (on screen keyboard)
    public static class OnScreenKeyboard
    {
        public static void Show() => throw new NotImplementedException();
    }

    // STUB(core-engine): Helpers/ColorUtilities.cs (GetDominantColor(Bitmap) is not needed by this task)
    public class ColorUtils
    {
        public static Color GetWeightedAverage(Color color1, Color color2, float weight) => throw new NotImplementedException();
        public static Color GetMidColor(Color color1, Color color2) => throw new NotImplementedException();

        public class HSV
        {
            public double Hue { get; set; }
            public double Saturation { get; set; }
            public double Value { get; set; }
            public Color ToRGB() => throw new NotImplementedException();
            public static HSV ToHSV(Color rgb) => throw new NotImplementedException();
            public static Color UpSaturation(Color rgb, float increse = 0.2f) => throw new NotImplementedException();
        }

        public class SmoothColor
        {
            public Color RGB { get; set; }
        }
    }

    // STUB(core-engine): facade (Windows registry / Dynamic Lighting)
    public static class DynamicLightingHelper
    {
        public enum DynamicLightingEffect
        {
            Solid = 0,
            Breathing = 1,
            Rainbow = 2,
            Wave = 4,
            Wheel = 5,
            Gradient = 6
        }

        public static void SetEffect(DynamicLightingEffect effect, Color? color = null, Color? color2 = null, int? brightness = null, int? speed = null)
            => throw new NotImplementedException();
    }
}

namespace GHelper.Peripherals
{
    // STUB(core-peripherals)
    public static class PeripheralsProvider
    {
        public static bool IsAuraSync { get; private set; }
        public static List<GHelper.Peripherals.Mouse.AsusMouse> SnapshotMice() => throw new NotImplementedException();
        public static void StreamMouseColor(Color color) => throw new NotImplementedException();
        public static void StreamKeyboardColor(Color color) => throw new NotImplementedException();
        public static void SyncMiceWithKeyboardAura() => throw new NotImplementedException();
        public static void SyncKeyboardsWithAura() => throw new NotImplementedException();
        public static void SyncHeadsetsWithAura() => throw new NotImplementedException();
    }
}

namespace GHelper.Peripherals.Keyboard
{
    // STUB(core-peripherals)
    public class AsusKeyboard
    {
        public const int LaunchSlots = 4;
        public static string LaunchCommand(int slot) => throw new NotImplementedException();
    }
}

namespace GHelper.Peripherals.Mouse
{
    // STUB(core-peripherals)
    public class AsusMouse
    {
        public class ComboDef
        {
            public string ResolveCommand() => throw new NotImplementedException();
        }

        public static readonly Dictionary<ushort, ComboDef> CombosByCode = new();
        public ushort[] ButtonBindings { get; protected set; } = new ushort[16];
        public virtual bool HasButtonBindings() => throw new NotImplementedException();
    }
}

// STUB(core-engine): HardwareControl.cs (global namespace in the legacy app)
public static class HardwareControl
{
    public static decimal batteryCapacity = -1;
    public static void ReadBatteryState() => throw new NotImplementedException();
    public static float? GetCPUTemp() => throw new NotImplementedException();
    public static double GetBatteryChargePercentage() => throw new NotImplementedException();
}

// STUB(core-engine): Helpers/TouchscreenHelper.cs (global namespace in the legacy app)
public static class TouchscreenHelper
{
    public static bool? GetStatus() => throw new NotImplementedException();
    public static void ToggleTouchscreen(bool status) => throw new NotImplementedException();
}
