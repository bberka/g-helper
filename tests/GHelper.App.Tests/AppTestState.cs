using System.Reflection;
using GHelper.AnimeMatrix;
using GHelper.Gpu;
using GHelper.Tests.Engine;
using Xunit;
using Xunit.v3;

[assembly: GHelper.App.Tests.ResetGlobalState]

namespace GHelper.App.Tests;

/// <summary>
/// Core keeps process wide singletons (AppConfig model cache, AniMatrixControl.Instance, HardwareControl sensor cache,
/// GPUModeControl UI state ...). Before this existed the result of a test depended on which earlier test of the
/// assembly had touched them: the first <c>new AniMatrixControl()</c> of a side window test became
/// <c>AniMatrixControl.Instance</c> for the rest of the run (matrix section visible without a device), the 2 second
/// wall clock throttle of <c>HardwareControl.GetCPUTemp</c> served the previous test's temperature, and
/// <c>GPUModeControl.ModesHidden</c> stayed latched. <see cref="Reset"/> puts all of it back to the start-up state.
/// </summary>
internal static class AppTestState
{
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Reset()
    {
        // model / BIOS are read from the platform once; tests swap platforms
        AppConfig.ResetModelCache();

        // one device handle singleton: a test's controller must not outlive the test
        TestStatics.Set(typeof(AniMatrixControl), "_instance", null);
        Program.matrixControl = null;

        ResetHardwareControl();
        ResetGpuModeControl();
    }

    private static void ResetHardwareControl()
    {
        var t = typeof(HardwareControl);

        foreach (var name in new[]
                 {
                     "cpuPower", "gpuPower", "batteryRate", "designCapacity", "fullCapacity", "chargeCapacity",
                     "batteryCharge", "cpuFan", "gpuFan", "midFan", "cpuFanRPM", "gpuFanRPM", "gpuUse", "cpuUsage",
                     "gpuUsage", "vramUsage", "ramUsage", "vramUsedMb", "ramUsedMb", "readFans", "readUsage",
                     "readMemory", "readPower", "readBattery", "lastUpdate", "_lastBatteryRead", "_cpuPowerNullTicks",
                 })
        {
            Default(t, name);
        }

        TestStatics.Set(t, "cpuTemp", (float?)-1);
        TestStatics.Set(t, "gpuTemp", (float?)-1);
        TestStatics.Set(t, "batteryHealth", -1m);
        TestStatics.Set(t, "batteryCapacity", -1m);
        TestStatics.Set(t, "batteryRate", (decimal?)0);
        HardwareControl.GpuControl = null;

        // initialised from the model once per process
        TestStatics.Set(t, "isPZ13", AppConfig.IsPZ13());
        TestStatics.Set(t, "isAlly", AppConfig.IsAlly());
        TestStatics.Set(t, "isAMDiGPU", AppConfig.IsAMDiGPU());
    }

    private static void ResetGpuModeControl()
    {
        var t = typeof(GPUModeControl);

        GPUModeControl.gpuMode = 0;
        GPUModeControl.gpuExists = null;
        GPUModeControl.gpuError = null;
        GPUModeControl.suspended = false;
        TestStatics.Set(t, "nvRestartPending", false);

        SetProperty(t, "EcoSupported", true);
        SetProperty(t, "MuxSupported", true);
        SetProperty(t, "ModesHidden", false);
        SetProperty(t, "GpuPanelVisible", true);
        SetProperty(t, "IsLocked", false);
        SetProperty(t, "LockText", null);
        SetProperty(t, "RequestedVisualMode", -1);
    }

    private static void Default(Type type, string field)
    {
        var f = type.GetField(field, Any);
        if (f is null) return;
        f.SetValue(null, f.FieldType.IsValueType ? Activator.CreateInstance(f.FieldType) : null);
    }

    private static void SetProperty(Type type, string name, object? value) =>
        (type.GetProperty(name, Any) ?? throw new MissingMemberException(type.FullName, name)).SetValue(null, value);
}

/// <summary>Leaves the process wide Core state clean after every test of the assembly.</summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
internal sealed class ResetGlobalStateAttribute : BeforeAfterTestAttribute
{
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        try { AppTestState.Reset(); }
        catch (Exception ex) { Console.Error.WriteLine("AppTestState.Reset: " + ex.Message); }
    }
}
