using GHelper.Platform;

namespace GHelper.Gpu.AMD;

/// <summary>
/// AMD GPU telemetry / control. The policy (cache windows, sensor fallbacks, OLED power optimisation bookkeeping) is
/// the legacy implementation; the native calls go through <see cref="IAmdGpuDriver"/> (ADL on Windows, amdgpu on Linux).
/// Reference: https://github.com/GPUOpen-LibrariesAndSDKs/display-library/blob/master/Sample-Managed/Program.cs
/// </summary>
public class AmdGpuControl : IGpuControl
{
    private const int SceUnsupported = -1;
    private const int PMLogCacheMs = 500;

    private IAmdGpuDriver? _driver;
    private int? _sceDisplayIndex;

    public bool IsNvidia => false;

    public string FullName => _driver?.DiscreteName ?? "";

    /// <summary>Uses the driver of the registered platform (IGpuProvider.CreateAmdDriver), none = never valid.</summary>
    public AmdGpuControl() : this(null)
    {
    }

    public AmdGpuControl(IAmdGpuDriver? driver)
    {
        if (AppConfig.NoGpu() && !AppConfig.IsAMDiGPU()) return;

        try
        {
            _driver = driver ?? (PlatformServices.IsRegistered ? PlatformServices.Current.Gpu.CreateAmdDriver() : null);
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }
    }

    public bool IsValid => _driver is not null && _driver.HasDiscrete;

    private bool HasIntegrated => _driver is not null && _driver.HasIntegrated;

    // ---- discrete GPU PM log (cached) ------------------------------------------------------------

    private AmdSensorSample? _pmLog;
    private long _pmLogTime = -PMLogCacheMs;

    private AmdSensorSample? GetPMLog()
    {
        if (Environment.TickCount64 - _pmLogTime >= PMLogCacheMs)
        {
            _pmLog = _driver!.QuerySensors(false);
            _pmLogTime = Environment.TickCount64;
        }
        return _pmLog;
    }

    public int? GetCurrentTemperature()
    {
        if (!IsValid) return null;
        return GetPMLog()?.TemperatureEdge ?? GetLegacy().temp;
    }

    public int? GetGpuUse()
    {
        if (!IsValid) return null;
        return GetPMLog()?.GfxActivity ?? GetLegacy().use;
    }

    private long _totalVramMB; // total VRAM is static, cached on first successful query (0 = not yet)

    public (long usedMb, long totalMb)? GetVramInfo()
    {
        if (_driver is null) return null;

        bool integrated = !IsValid;
        if (integrated && !_driver.HasIntegrated) return null;

        if (_totalVramMB <= 0)
        {
            long? total = _driver.GetTotalVramMb(integrated);
            if (total is null || total <= 0) return null;
            _totalVramMB = total.Value;
        }

        int? used = _driver.GetUsedVramMb(integrated);
        if (used is null) return null;

        return (used.Value, _totalVramMB);
    }

    // ---- integrated GPU --------------------------------------------------------------------------

    public int? GetiGpuUse()
    {
        if (!HasIntegrated) return null;
        return _driver!.QuerySensors(true)?.GfxActivity;
    }

    private AmdSensorSample? _pmLogiGpu;
    private long _pmLogiGpuTime = -PMLogCacheMs;

    private AmdSensorSample? GetPMLogiGpu()
    {
        if (!HasIntegrated) return null;
        if (Environment.TickCount64 - _pmLogiGpuTime >= PMLogCacheMs)
        {
            _pmLogiGpu = _driver!.QuerySensors(true);
            _pmLogiGpuTime = Environment.TickCount64;
        }
        return _pmLogiGpu;
    }

    public (int? temp, int? use, int? gfxPower, int? cpuPower, int? asicPower) GetiGpuSensors()
    {
        var log = GetPMLogiGpu();
        if (log is null) return default;

        return (log.TemperatureEdge ?? log.TemperatureGfx ?? log.TemperatureSoc,
                log.GfxActivity,
                log.GfxPower,
                log.CpuPower,
                log.AsicPower);
    }

    // Used by ROG Ally (iGPU-only) for auto-TDP logic - queries the integrated GPU adapter
    public int GetiGpuPower()
    {
        if (!HasIntegrated) return 0;
        return _driver!.QuerySensors(true)?.AsicPower ?? 0;
    }

    public float? GetGpuPower()
    {
        if (!IsValid) return null;

        var log = GetPMLog();
        if (log is not null)
        {
            foreach (int? power in new[] { log.AsicPower, log.GfxPower, log.BoardPower })
                if (power is > 0) return power.Value;
        }

        return GetLegacy().power;
    }

    // ---- pre-PMLog GPUs ----------------------------------------------------------------------------

    private bool? _oldGpu;
    private (int? temp, int? use, float? power) _legacy;
    private long _legacyTick = -PMLogCacheMs;

    private (int? temp, int? use, float? power) GetLegacy()
    {
        try
        {
            _oldGpu ??= _driver!.IsLegacyOverdrive();
            if (_oldGpu == false) return default;
            if (Environment.TickCount64 - _legacyTick < PMLogCacheMs) return _legacy;

            var sample = _driver!.QueryLegacy();
            _legacyTick = Environment.TickCount64;
            return _legacy = (sample.Temperature, sample.Usage, sample.Power);
        }
        catch (EntryPointNotFoundException)
        {
            _oldGpu = false;
            return default;
        }
    }

    // ---- display features ----------------------------------------------------------------------------

    public bool SetVariBright(int enabled)
    {
        if (!HasIntegrated) return false;
        return _driver!.SetVariBright(enabled);
    }

    public bool GetVariBright(out int supported, out int enabled)
    {
        supported = enabled = -1;
        if (!HasIntegrated) return false;
        return _driver!.GetVariBright(out supported, out enabled);
    }

    public bool GetOledPowerOptimization()
    {
        if (!HasIntegrated) return false;

        try
        {
            _sceDisplayIndex ??= _driver!.FindSceDisplay();
            if (_sceDisplayIndex is null or SceUnsupported) return false;
            int? state = _driver!.GetSceState((int)_sceDisplayIndex);
            return state is > 1;
        }
        catch
        {
            _sceDisplayIndex = SceUnsupported;
            Logger.WriteLine("No AMD OLED Power Optimization support");
            return false;
        }
    }

    public bool DisableOledPowerOptimization()
    {
        if (!HasIntegrated) return false;
        if (_sceDisplayIndex is null or SceUnsupported) return false;

        try
        {
            return _driver!.SetSceState((int)_sceDisplayIndex, 1);
        }
        catch
        {
            Logger.WriteLine("Can't disable AMD OLED Power Optimization");
            return false;
        }
    }

    // ---- FPS (Ally) ----------------------------------------------------------------------------------

    public void StartFPS()
    {
        if (!HasIntegrated) return;
        _driver!.StartFrameMetrics();
    }

    public void StopFPS()
    {
        if (!HasIntegrated) return;
        _driver!.StopFrameMetrics();
    }

    public float GetFPS()
    {
        if (!HasIntegrated) return 0;
        return _driver!.GetFrameMetrics();
    }

    public int GetFPSLimit()
    {
        if (!HasIntegrated) return -1;
        int limit = _driver!.GetFpsLimit();
        if (limit >= 0) Logger.WriteLine($"FPS Limit: {limit}");
        return limit;
    }

    public int SetFPSLimit(int limit)
    {
        if (!HasIntegrated) return -1;
        return _driver!.SetFpsLimit(limit) ? 1 : 0;
    }

    // ---- process killing -----------------------------------------------------------------------------

    private static readonly HashSet<string> Immune = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost", "system", "ntoskrnl", "csrss", "winlogon", "wininit", "smss",
        // Linux session / compositor processes that are listed as GPU clients
        "xorg", "xwayland", "gnome-shell", "kwin_wayland", "kwin_x11", "plasmashell", "sway", "systemd", "mutter",
    };

    public static bool IsImmune(string processName) => Immune.Contains(processName);

    public void KillGPUApps()
    {
        if (!IsValid) return;

        try
        {
            foreach (string name in _driver!.GetGpuApplications())
            {
                if (string.IsNullOrEmpty(name) || IsImmune(name)) continue;
                PlatformServices.Current.Process.KillByName(name);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }
    }

    public void Dispose()
    {
        var driver = _driver;
        _driver = null;
        driver?.Dispose();
        GC.SuppressFinalize(this);
    }
}
