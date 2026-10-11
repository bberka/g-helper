using System.Runtime.InteropServices;
using GHelper.Gpu;
using static GHelper.Platform.Windows.Gpu.Adl2.NativeMethods;

namespace GHelper.Platform.Windows.Gpu;

/// <summary>
/// AMD ADL2 implementation of Core's <see cref="IAmdGpuDriver"/>: only the native queries live here, the caching,
/// sensor fallback order and kill policy are Core's <c>AmdGpuControl</c> (legacy app/Gpu/AMD/AmdGpuControl.cs).
/// One instance owns one ADL context. x64 and arm64 only (no ADL for 32 bit).
/// Reference: https://github.com/GPUOpen-LibrariesAndSDKs/display-library/blob/master/Sample-Managed/Program.cs
/// </summary>
internal sealed class AdlAmdDriver : IAmdGpuDriver
{
    private nint _adlContextHandle;
    private readonly ADLAdapterInfo? _discrete;
    private readonly ADLAdapterInfo? _iGPU;

    private AdlAmdDriver(nint context, ADLAdapterInfo? discrete, ADLAdapterInfo? integrated)
    {
        _adlContextHandle = context;
        _discrete = discrete;
        _iGPU = integrated;
    }

    /// <summary>ADL is available for x64 and arm64 Windows.</summary>
    public static bool IsSupported =>
        OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;

    /// <summary>Null when ADL is not present / initialisation failed. An iGPU only system still returns a driver.</summary>
    public static AdlAmdDriver? TryCreate()
    {
        if (!IsSupported || !Adl2.Load()) return null;

        nint context;
        try
        {
            if (Adl2.ADL2_Main_Control_Create(1, out context) != Adl2.ADL_SUCCESS) return null;
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
            return null;
        }

        try
        {
            var discrete = FindByType(context, ADLAsicFamilyType.Discrete);
            var integrated = FindByType(context, ADLAsicFamilyType.Integrated);
            return new AdlAmdDriver(context, discrete, integrated);
        }
        catch (Exception ex)
        {
            Logger.WriteLine("ADL adapters: " + ex.Message);
            ADL2_Main_Control_Destroy(context);
            return null;
        }
    }

    private static ADLAdapterInfo? FindByType(nint context, ADLAsicFamilyType type)
    {
        ADL2_Adapter_NumberOfAdapters_Get(context, out int numberOfAdapters);
        if (numberOfAdapters <= 0)
            return null;

        ADLAdapterInfoArray osAdapterInfoData = new();
        int osAdapterInfoDataSize = Marshal.SizeOf(osAdapterInfoData);
        nint adapterBuffer = Marshal.AllocCoTaskMem(osAdapterInfoDataSize);
        try
        {
            Marshal.StructureToPtr(osAdapterInfoData, adapterBuffer, false);
            if (ADL2_Adapter_AdapterInfo_Get(context, adapterBuffer, osAdapterInfoDataSize) != Adl2.ADL_SUCCESS)
                return null;

            osAdapterInfoData = (ADLAdapterInfoArray)Marshal.PtrToStructure(adapterBuffer, osAdapterInfoData.GetType())!;
        }
        finally
        {
            Marshal.FreeCoTaskMem(adapterBuffer);
        }

        const int amdVendorId = 1002;

        ADLAdapterInfo found =
            osAdapterInfoData.ADLAdapterInfo
                .FirstOrDefault(adapter =>
                {
                    if (adapter.Exist == 0 || adapter.Present == 0)
                        return false;

                    if (adapter.VendorID != amdVendorId)
                        return false;

                    if (ADL2_Adapter_ASICFamilyType_Get(context, adapter.AdapterIndex, out ADLAsicFamilyType asicFamilyType, out int asicFamilyTypeValids) != Adl2.ADL_SUCCESS)
                        return false;

                    asicFamilyType = (ADLAsicFamilyType)((int)asicFamilyType & asicFamilyTypeValids);

                    return (asicFamilyType & type) != 0;
                });

        if (found.Exist == 0)
            return null;

        return found;
    }

    public bool HasDiscrete => _adlContextHandle != nint.Zero && _discrete is not null;

    public bool HasIntegrated => _adlContextHandle != nint.Zero && _iGPU is not null;

    public string DiscreteName => _discrete?.AdapterName ?? "";

    private ADLAdapterInfo? Adapter(bool integrated) => integrated ? _iGPU : _discrete;

    private static int? Sensor(ADLPMLogDataOutput log, ADLSensorType type)
    {
        ADLSingleSensorData sensor = log.Sensors[(int)type];
        return sensor.Supported != 0 ? sensor.Value : null;
    }

    public AmdSensorSample? QuerySensors(bool integrated)
    {
        if (_adlContextHandle == nint.Zero || Adapter(integrated) is not { } adapter) return null;
        if (ADL2_New_QueryPMLogData_Get(_adlContextHandle, adapter.AdapterIndex, out ADLPMLogDataOutput log) != Adl2.ADL_SUCCESS) return null;

        return new AmdSensorSample
        {
            TemperatureEdge = Sensor(log, ADLSensorType.PMLOG_TEMPERATURE_EDGE),
            TemperatureGfx = Sensor(log, ADLSensorType.PMLOG_TEMPERATURE_GFX),
            TemperatureSoc = Sensor(log, ADLSensorType.PMLOG_TEMPERATURE_SOC),
            GfxActivity = Sensor(log, ADLSensorType.PMLOG_INFO_ACTIVITY_GFX),
            AsicPower = Sensor(log, ADLSensorType.PMLOG_ASIC_POWER),
            GfxPower = Sensor(log, ADLSensorType.PMLOG_GFX_POWER),
            BoardPower = Sensor(log, ADLSensorType.PMLOG_BOARD_POWER),
            CpuPower = Sensor(log, ADLSensorType.PMLOG_CPU_POWER),
        };
    }

    // ---- pre-PMLog GPUs ------------------------------------------------------------------------

    public bool IsLegacyOverdrive()
    {
        if (_adlContextHandle == nint.Zero || _discrete is null) return false;
        return ADL2_Overdrive_Caps(_adlContextHandle, _discrete.Value.AdapterIndex, out _, out _, out int version) == Adl2.ADL_SUCCESS && version < 8;
    }

    public AmdLegacySample QueryLegacy()
    {
        if (_adlContextHandle == nint.Zero || _discrete is null) return default;

        int idx = _discrete.Value.AdapterIndex;
        int? temp = null, use = null;
        float? power = null;

        if (ADL2_OverdriveN_Temperature_Get(_adlContextHandle, idx, 1, out int t) == Adl2.ADL_SUCCESS)
        {
            if (t > 1000) t /= 1000;
            if (t > 0 && t < 125) temp = t;
        }

        if (ADL2_OverdriveN_PerformanceStatus_Get(_adlContextHandle, idx, out ADLODNPerformanceStatus st) == Adl2.ADL_SUCCESS
            && st.iGPUActivityPercent >= 0 && st.iGPUActivityPercent <= 100)
            use = st.iGPUActivityPercent;

        if (ADL2_Overdrive6_CurrentPower_Get(_adlContextHandle, idx, 0, out int p) == Adl2.ADL_SUCCESS)
        {
            float watts = p / 256f;
            if (watts > 0 && watts < 1000) power = watts;
        }

        return new AmdLegacySample(temp, use, power);
    }

    // ---- memory --------------------------------------------------------------------------------

    public long? GetTotalVramMb(bool integrated)
    {
        if (_adlContextHandle == nint.Zero || Adapter(integrated) is not { } adapter) return null;
        if (ADL2_Adapter_MemoryInfo2_Get(_adlContextHandle, adapter.AdapterIndex, out ADLMemoryInfo2 mem) != Adl2.ADL_SUCCESS) return null;
        return mem.iMemorySize / (1024 * 1024);
    }

    public int? GetUsedVramMb(bool integrated)
    {
        if (_adlContextHandle == nint.Zero || Adapter(integrated) is not { } adapter) return null;
        if (ADL2_Adapter_DedicatedVRAMUsage_Get(_adlContextHandle, adapter.AdapterIndex, out int usedMb) != Adl2.ADL_SUCCESS) return null;
        return usedMb;
    }

    // ---- iGPU display features -----------------------------------------------------------------

    public bool SetVariBright(int enabled)
    {
        if (_adlContextHandle == nint.Zero || _iGPU is null) return false;
        return ADL2_Adapter_VariBrightEnable_Set(_adlContextHandle, _iGPU.Value.AdapterIndex, enabled) == Adl2.ADL_SUCCESS;
    }

    public bool GetVariBright(out int supported, out int enabled)
    {
        supported = enabled = -1;
        if (_adlContextHandle == nint.Zero || _iGPU is null) return false;

        if (ADL2_Adapter_VariBright_Caps(_adlContextHandle, _iGPU.Value.AdapterIndex, out int supportedOut, out int enabledOut, out _) != Adl2.ADL_SUCCESS)
            return false;

        supported = supportedOut;
        enabled = enabledOut;
        return true;
    }

    public int? FindSceDisplay()
    {
        if (_adlContextHandle == nint.Zero || _iGPU is null) return -1;
        int adapterIndex = _iGPU.Value.AdapterIndex;

        if (ADL2_Display_DisplayInfo_Get(_adlContextHandle, adapterIndex, out int count, out nint buffer, 0) != Adl2.ADL_SUCCESS) return null;

        try
        {
            int size = Marshal.SizeOf<ADLDisplayInfo>();
            for (int i = 0; i < count; i++)
            {
                var display = Marshal.PtrToStructure<ADLDisplayInfo>(buffer + i * size);
                if ((display.DisplayInfoValue & 1) == 0) continue;
                if (ADL2_Display_SCE_State_Get(_adlContextHandle, adapterIndex, display.DisplayID.DisplayLogicalIndex, out _, out _) == Adl2.ADL_SUCCESS)
                    return display.DisplayID.DisplayLogicalIndex;
            }
        }
        finally
        {
            Adl2.FreeMemory(buffer);
        }

        return -1;
    }

    public int? GetSceState(int displayIndex)
    {
        if (_adlContextHandle == nint.Zero || _iGPU is null) return null;
        if (ADL2_Display_SCE_State_Get(_adlContextHandle, _iGPU.Value.AdapterIndex, displayIndex, out int state, out _) != Adl2.ADL_SUCCESS) return null;
        return state;
    }

    public bool SetSceState(int displayIndex, int state)
    {
        if (_adlContextHandle == nint.Zero || _iGPU is null) return false;
        return ADL2_Display_SCE_State_Set(_adlContextHandle, _iGPU.Value.AdapterIndex, displayIndex, state) == Adl2.ADL_SUCCESS;
    }

    // ---- frame metrics / FPS limit (Ally) ------------------------------------------------------

    public void StartFrameMetrics()
    {
        if (_adlContextHandle == nint.Zero || _iGPU is null) return;
        ADL2_Adapter_FrameMetrics_Start(_adlContextHandle, _iGPU.Value.AdapterIndex, 0);
    }

    public void StopFrameMetrics()
    {
        if (_adlContextHandle == nint.Zero || _iGPU is null) return;
        ADL2_Adapter_FrameMetrics_Stop(_adlContextHandle, _iGPU.Value.AdapterIndex, 0);
    }

    public float GetFrameMetrics()
    {
        if (_adlContextHandle == nint.Zero || _iGPU is null) return 0;
        if (ADL2_Adapter_FrameMetrics_Get(_adlContextHandle, _iGPU.Value.AdapterIndex, 0, out float fps) != Adl2.ADL_SUCCESS) return 0;
        return fps;
    }

    public int GetFpsLimit()
    {
        if (_adlContextHandle == nint.Zero || _iGPU is null) return -1;
        if (ADL2_FPS_Settings_Get(_adlContextHandle, _iGPU.Value.AdapterIndex, out ADLFPSSettingsOutput settings) != Adl2.ADL_SUCCESS) return -1;
        return settings.ulACFPSCurrent;
    }

    public bool SetFpsLimit(int limit)
    {
        if (_adlContextHandle == nint.Zero || _iGPU is null) return false;

        var settings = new ADLFPSSettingsInput
        {
            ulACFPSCurrent = limit,
            ulDCFPSCurrent = limit,
            bGlobalSettings = 1,
        };

        return ADL2_FPS_Settings_Set(_adlContextHandle, _iGPU.Value.AdapterIndex, settings) == Adl2.ADL_SUCCESS;
    }

    // ---- processes running on the dGPU ---------------------------------------------------------

    public IReadOnlyList<string> GetGpuApplications()
    {
        var names = new List<string>();
        if (_adlContextHandle == nint.Zero || _discrete is null) return names;

        nint appInfoPtr = nint.Zero;

        try
        {
            // switchable graphics applications information
            int result = ADL2_SwitchableGraphics_Applications_Get(_adlContextHandle, 2, out int appCount, out appInfoPtr);
            if (result != 0)
            {
                Logger.WriteLine("Failed to get switchable graphics applications. Error code: " + result);
                return names;
            }

            int size = Marshal.SizeOf<ADLSGApplicationInfo>();
            nint currentPtr = appInfoPtr;

            for (int i = 0; i < appCount; i++)
            {
                var app = Marshal.PtrToStructure<ADLSGApplicationInfo>(currentPtr);
                currentPtr = nint.Add(currentPtr, size);

                if (app.iGPUAffinity != 1) continue;

                Logger.WriteLine(app.strFileName + ":" + app.iGPUAffinity + "(" + app.timeStamp + ")");
                names.Add(Path.GetFileNameWithoutExtension(app.strFileName));
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }
        finally
        {
            if (appInfoPtr != nint.Zero) Marshal.FreeCoTaskMem(appInfoPtr);
        }

        return names;
    }

    private void ReleaseUnmanagedResources()
    {
        nint context = Interlocked.Exchange(ref _adlContextHandle, nint.Zero);
        if (context != nint.Zero) ADL2_Main_Control_Destroy(context);
    }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    ~AdlAmdDriver()
    {
        ReleaseUnmanagedResources();
    }
}
