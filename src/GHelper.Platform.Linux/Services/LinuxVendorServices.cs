namespace GHelper.Platform.Linux;

public sealed record VendorConflict(string Name, string Kind, bool Running);

/// <summary>
/// OEM / community software that fights with G-Helper on Linux: asusd (asusctl) and supergfxd (supergfxctl) take over
/// the same asus-wmi attributes, power-profiles-daemon cooperates. Stopping needs root.
/// </summary>
public sealed class LinuxVendorServices : IVendorServices
{
    // (process comm / unit base name, systemd system unit)
    private static readonly (string Process, string Unit)[] Daemons =
    [
        ("asusd", "asusd.service"),
        ("supergfxd", "supergfxd.service"),
    ];

    private static readonly string[] OtherProcesses = ["asusd-user", "rog-control-center"];

    private static readonly string[] UnitDirectories = ["/usr/lib/systemd/system", "/lib/systemd/system", "/etc/systemd/system", "/usr/local/lib/systemd/system"];

    private readonly LinuxContext ctx;
    private readonly AsusWmiLayout? layout;
    private readonly Func<string, bool> configFlag;

    public LinuxVendorServices(LinuxContext ctx, AsusWmiLayout? layout = null, Func<string, bool>? configFlag = null)
    {
        this.ctx = ctx;
        this.layout = layout;
        this.configFlag = configFlag ?? (key => AppConfig.Is(key));
    }

    private bool Running(string process) => ProcFs.IsRunning(ctx.Root, process);

    private bool UnitInstalled(string unit)
        => UnitDirectories.Any(d => ctx.Root.FileExists(d + "/" + unit));

    /// <summary>Everything detected that conflicts with or duplicates G-Helper features (for logs / UI hints).</summary>
    public IReadOnlyList<VendorConflict> DetectConflicts()
    {
        var list = new List<VendorConflict>();
        foreach (var (process, unit) in Daemons)
        {
            if (Running(process) || UnitInstalled(unit))
                list.Add(new VendorConflict(process, "daemon", Running(process)));
        }
        foreach (string process in OtherProcesses)
            if (Running(process)) list.Add(new VendorConflict(process, "ui", true));

        if (Running("tlp") || UnitInstalled("tlp.service"))
            list.Add(new VendorConflict("tlp", "power", Running("tlp")));
        if (Running("tuned"))
            list.Add(new VendorConflict("tuned", "power", true));
        return list;
    }

    public bool IsAsusOptimizationRunning() => Running("asusd");

    public bool IsArmouryRunning() => Running("asusd") || Running("supergfxd") || Running("rog-control-center");

    public bool IsOSDRunning() => false;

    public int GetRunningCount()
        => Daemons.Count(d => Running(d.Process)) + OtherProcesses.Count(Running);

    public void StopAsusServices()
    {
        if (!ctx.IsRoot)
        {
            LinuxLog.Write("Stopping asusd/supergfxd needs root");
            return;
        }

        foreach (var (process, unit) in Daemons)
        {
            if (!UnitInstalled(unit) && !Running(process)) continue;
            ctx.Runner.Run("systemctl", ["disable", "--now", unit], 15000);
        }

        // user session helpers have no system unit
        foreach (string process in OtherProcesses)
            foreach (int pid in ProcFs.FindByName(ctx.Root, process))
                Kill(pid);

        if (GetRunningCount() == 0) LinuxLog.Write("ASUS services stopped");
    }

    public void StartAsusServices()
    {
        if (!ctx.IsRoot) return;
        foreach (var (_, unit) in Daemons)
        {
            if (!UnitInstalled(unit)) continue;
            ctx.Runner.Run("systemctl", ["enable", "--now", unit], 15000);
        }
    }

    public void StopOnStartup()
    {
        if (configFlag("services_skip")) return;
        if (!configFlag("services_disabled") || !ctx.IsRoot) return;
        if (GetRunningCount() == 0) return;

        LinuxLog.Write("ASUS services revived, re-stopping on startup");
        Task.Run(StopAsusServices);
    }

    private static void Kill(int pid)
    {
        try { System.Diagnostics.Process.GetProcessById(pid).Kill(); } catch { }
    }

    public void KillSmartDisplayControl()
    {
        // Windows only
    }

    public void RunArmouryUninstaller()
    {
        // nothing to uninstall on Linux; the conflicting daemons are removed with the distribution package manager
    }

    // ------------------------------------------------------------------ Windows only features

    public bool IsDynamicLightingSupported => false;
    public bool IsDynamicLightingEnabled() => false;
    public void SetDynamicLighting(bool enabled) { }
    public void OpenDynamicLightingSettings() { }

    public bool IsKeystoneEnabled()
    {
        string? path = layout?.Platform("keystone");
        return path is not null && ctx.Root.ReadInt(path) == 1;
    }

    public void SetKeystoneEnabled(bool enabled)
    {
        // read-only in the kernel driver
    }

    /// <summary>A discrete GPU on the PCI bus without a bound driver (typical symptom of a missing nvidia module).</summary>
    public string? GetGpuError()
    {
        foreach (PciGpu gpu in PciGpu.Enumerate(ctx.Root))
        {
            if (gpu.IsBootVga || gpu.Vendor == GpuVendor.Intel) continue;
            if (gpu.Driver is null) return $"No driver bound to GPU {gpu.Address} ({gpu.Vendor})";
        }
        return null;
    }
}
