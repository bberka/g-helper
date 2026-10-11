using GHelper.Gpu;

namespace GHelper.Platform.Linux;

/// <summary>
/// Core's <see cref="IAmdGpuDriver"/> on top of the amdgpu kernel driver's sysfs interface (the counterpart of ADL on
/// Windows): hwmon temp / power, gpu_busy_percent, mem_info_vram_*, the render node users, and the eDP connector's
/// <c>amdgpu/panel_power_savings</c> (adaptive backlight management, kernel 6.8+) for the OLED power optimisation.
/// A runtime-suspended GPU is never read: that would wake the dGPU the user switched off.
/// Everything is resolved against <see cref="LinuxContext.Root"/> so it runs against a fake /sys tree in tests.
/// </summary>
internal sealed class SysfsAmdGpuDriver : IAmdGpuDriver
{
    private const string PanelPowerSavings = "/amdgpu/panel_power_savings";

    private readonly LinuxContext ctx;
    private readonly SysfsRoot root;
    private readonly PciGpu? discrete;
    private readonly PciGpu? integrated;

    public SysfsAmdGpuDriver(LinuxContext ctx)
    {
        this.ctx = ctx;
        root = ctx.Root;

        List<PciGpu> amd = PciGpu.Enumerate(root).Where(g => g.Vendor == GpuVendor.Amd && g.Driver == "amdgpu").ToList();
        discrete = amd.FirstOrDefault(g => IsDiscrete(root, g));
        integrated = amd.FirstOrDefault(g => !IsDiscrete(root, g));
    }

    /// <summary>A dGPU has lots of dedicated memory or is not the boot VGA device; APUs carve out at most a few GB.</summary>
    internal static bool IsDiscrete(SysfsRoot root, PciGpu gpu)
    {
        long? vram = root.ReadLong(gpu.SysfsDir + "/mem_info_vram_total");
        if (vram is > 3L * 1024 * 1024 * 1024) return true;
        if (vram is not null && gpu.IsBootVga) return false;
        return !gpu.IsBootVga && vram is > 1L * 1024 * 1024 * 1024;
    }

    public bool HasDiscrete => discrete is not null;

    public bool HasIntegrated => integrated is not null;

    public string DiscreteName
    {
        get
        {
            if (discrete is null) return "";
            return PciIds.Describe(root, GpuVendor.Amd, discrete.DeviceId)
                   ?? root.ReadText(discrete.SysfsDir + "/product_name")
                   ?? $"AMD GPU {discrete.DeviceId:X4}";
        }
    }

    private PciGpu? Gpu(bool useIntegrated) => useIntegrated ? integrated : discrete;

    private bool IsSuspended(PciGpu gpu)
        => string.Equals(root.ReadText(gpu.SysfsDir + "/power/runtime_status"), "suspended", StringComparison.OrdinalIgnoreCase);

    // ---- sensors -------------------------------------------------------------------------------

    private int? HwmonMilli(PciGpu gpu, string file)
    {
        string? dir = gpu.HwmonDir(root);
        if (dir is null) return null;
        int? milli = root.ReadInt(dir + "/" + file);
        return milli is null ? null : milli.Value / 1000;
    }

    public AmdSensorSample? QuerySensors(bool useIntegrated)
    {
        if (Gpu(useIntegrated) is not { } gpu || IsSuspended(gpu)) return null;

        string? hwmon = gpu.HwmonDir(root);
        int? activity = root.ReadInt(gpu.SysfsDir + "/gpu_busy_percent");
        int? edge = HwmonMilli(gpu, "temp1_input");
        int? power = null;
        if (hwmon is not null)
        {
            // average over the last interval when the ASIC reports it, instantaneous otherwise (micro watt)
            long? micro = root.ReadLong(hwmon + "/power1_average") ?? root.ReadLong(hwmon + "/power1_input");
            if (micro is not null) power = (int)Math.Round(micro.Value / 1_000_000.0);
        }

        if (edge is null && activity is null && power is null) return null;

        return new AmdSensorSample
        {
            TemperatureEdge = edge,
            GfxActivity = activity,
            // the single hwmon power channel is the whole ASIC (APU: SoC) power
            AsicPower = power,
        };
    }

    // amdgpu has no pre-PM-log Overdrive generation: nothing to fall back to
    public bool IsLegacyOverdrive() => false;

    public AmdLegacySample QueryLegacy() => default;

    // ---- memory --------------------------------------------------------------------------------

    public long? GetTotalVramMb(bool useIntegrated)
    {
        if (Gpu(useIntegrated) is not { } gpu) return null;
        long? bytes = root.ReadLong(gpu.SysfsDir + "/mem_info_vram_total");
        return bytes is > 0 ? bytes.Value / (1024 * 1024) : null;
    }

    public int? GetUsedVramMb(bool useIntegrated)
    {
        if (Gpu(useIntegrated) is not { } gpu || IsSuspended(gpu)) return null;
        long? bytes = root.ReadLong(gpu.SysfsDir + "/mem_info_vram_used");
        return bytes is null ? null : (int)(bytes.Value / (1024 * 1024));
    }

    // ---- iGPU display features -----------------------------------------------------------------

    // ADL "VariBright" has no separate sysfs knob (it is the same ABM engine as panel_power_savings below)
    public bool SetVariBright(int enabled) => false;

    public bool GetVariBright(out int supported, out int enabled)
    {
        supported = enabled = -1;
        return false;
    }

    private string? PanelSavingsFile()
    {
        foreach (string connector in root.ListDirectories("/sys/class/drm"))
        {
            string name = Path.GetFileName(connector);
            int dash = name.IndexOf('-');
            if (dash < 0 || !LinuxDisplayService.IsInternalConnector(name[(dash + 1)..])) continue;

            string file = connector + PanelPowerSavings;
            if (root.FileExists(file)) return file;
        }
        return null;
    }

    /// <summary>The ABM / panel power savings knob is the sysfs form of the ADL SCE state: one internal panel, index 0.</summary>
    public int? FindSceDisplay() => PanelSavingsFile() is null ? -1 : 0;

    /// <summary>ADL SCE state 1 = off, &gt; 1 = optimisation level active. panel_power_savings: 0 = off, 1..4 = level.</summary>
    public int? GetSceState(int displayIndex)
    {
        string? file = PanelSavingsFile();
        if (file is null) return null;
        int? level = root.ReadInt(file);
        return level is null ? null : level.Value + 1;
    }

    public bool SetSceState(int displayIndex, int state)
    {
        string? file = PanelSavingsFile();
        if (file is null) return false;
        return root.TryWrite(file, Math.Max(0, state - 1).ToString());
    }

    // ---- frame metrics / FPS limit (Ally: Windows driver features) -----------------------------

    public void StartFrameMetrics()
    {
    }

    public void StopFrameMetrics()
    {
    }

    public float GetFrameMetrics() => 0;

    public int GetFpsLimit() => -1;

    public bool SetFpsLimit(int limit) => false;

    // ---- processes -----------------------------------------------------------------------------

    /// <summary>Applications holding the dGPU's render node open (compositors excluded).</summary>
    public IReadOnlyList<string> GetGpuApplications()
    {
        if (discrete is null) return [];

        var nodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (string drm in root.ListDirectories(discrete.SysfsDir + "/drm"))
        {
            string name = Path.GetFileName(drm);
            if (name.StartsWith("renderD", StringComparison.Ordinal)) nodes.Add("/dev/dri/" + name);
        }
        if (nodes.Count == 0) return [];

        return GpuProcessKiller.FindNames(ctx, target => nodes.Contains(target));
    }

    public void Dispose()
    {
    }
}
