using GHelper.Gpu;
using GHelper.Gpu.AMD;
using GHelper.Gpu.NVidia;

namespace GHelper.Platform.Linux;

/// <summary>
/// IGpuProvider for Linux. The control objects are Core's: <c>NvidiaGpuControl</c> over NVML (libnvidia-ml.so.1, shared
/// with Windows) for the proprietary NVIDIA driver and <c>AmdGpuControl</c> over <see cref="SysfsAmdGpuDriver"/>
/// (amdgpu hwmon / DRM sysfs). Only GPUs without either (nouveau / nova_core, Intel Arc) get the plain sysfs
/// <see cref="SysfsGpuControl"/>. A runtime-suspended GPU is never touched: querying it would wake the dGPU the user
/// switched off, so <see cref="GetDiscretePowerState"/> reports it from PCI runtime_status.
/// </summary>
public sealed class LinuxGpuProvider : IGpuProvider
{
    private readonly LinuxContext ctx;

    public LinuxGpuProvider(LinuxContext ctx)
    {
        this.ctx = ctx;
    }

    public IGpuControl? CreateDiscrete()
    {
        try
        {
            if (AppConfig.NoGpu()) return null;

            List<PciGpu> gpus = PciGpu.Enumerate(ctx.Root);

            // proprietary driver: NVML (the only thing that knows temperature / power / VRAM of a GeForce)
            if (gpus.Any(g => g.Vendor == GpuVendor.Nvidia && g.Driver == "nvidia"))
            {
                IGpuControl nvidia = new NvidiaGpuControl();
                if (nvidia.IsValid) return nvidia;
                nvidia.Dispose();
            }

            // AMD dGPU: not the boot VGA device on a hybrid laptop, or lots of dedicated memory
            if (gpus.Any(g => g.Vendor == GpuVendor.Amd && g.Driver == "amdgpu" && SysfsAmdGpuDriver.IsDiscrete(ctx.Root, g)))
            {
                IGpuControl amd = new AmdGpuControl(new SysfsAmdGpuDriver(ctx));
                if (amd.IsValid) return amd;
                amd.Dispose();
            }

            PciGpu? nouveau = gpus.FirstOrDefault(g => g.Vendor == GpuVendor.Nvidia && g.Driver is "nouveau" or "nova_core");
            if (nouveau is not null) return new SysfsGpuControl(ctx, nouveau, isNvidia: true);

            PciGpu? intel = gpus.FirstOrDefault(g => g.Vendor == GpuVendor.Intel && g.Driver is "i915" or "xe" && HasDedicatedMemory(g));
            if (intel is not null) return new SysfsGpuControl(ctx, intel, isNvidia: false);
        }
        catch (Exception ex)
        {
            LinuxLog.Write("GPU detection: " + ex.Message);
        }
        return null;
    }

    /// <summary>The APU graphics (OLED power savings / telemetry), null when there is no amdgpu iGPU.</summary>
    public IGpuControl? CreateAmdApu()
    {
        try
        {
            var driver = new SysfsAmdGpuDriver(ctx);
            if (!driver.HasIntegrated && !driver.HasDiscrete)
            {
                driver.Dispose();
                return null;
            }
            return new AmdGpuControl(driver);
        }
        catch (Exception ex)
        {
            LinuxLog.Write("AMD APU detection: " + ex.Message);
            return null;
        }
    }

    public IAmdGpuDriver? CreateAmdDriver() => new SysfsAmdGpuDriver(ctx);

    /// <summary>
    /// Runtime PM state of the NVIDIA dGPU (read from sysfs, never touches the device): suspended = Asleep (Eco mode
    /// idle), otherwise Active. Unknown without an NVIDIA GPU so that Core falls back to asking the driver.
    /// </summary>
    public GpuPowerState GetDiscretePowerState()
    {
        try
        {
            PciGpu? nvidia = PciGpu.Enumerate(ctx.Root).FirstOrDefault(g => g.Vendor == GpuVendor.Nvidia);
            if (nvidia is null) return GpuPowerState.Unknown;
            return nvidia.IsSuspended ? GpuPowerState.Asleep : GpuPowerState.Active;
        }
        catch
        {
            return GpuPowerState.Unknown;
        }
    }

    private bool HasDedicatedMemory(PciGpu gpu)
        => ctx.Root.ReadLong(gpu.SysfsDir + "/mem_info_vram_total") is > 0
           || ctx.Root.DirectoryExists(gpu.SysfsDir + "/lmem_total_bytes") || ctx.Root.FileExists(gpu.SysfsDir + "/lmem_total_bytes");

    // Intel iGPU shared memory size is a Windows driver registry value: nothing to configure on Linux
    public bool InitIntelVram() => false;

    public void SetIntelVramIndex(int index)
    {
    }

    /// <summary>Releases the NVML handle (Eco mode: lets the driver power the GPU down).</summary>
    public void UnloadNative()
    {
        try { NvmlHelper.Shutdown(); }
        catch { }
    }
}

// ---------------------------------------------------------------------------------------------- sysfs GPUs

internal sealed class SysfsGpuControl : IGpuControl
{
    private readonly LinuxContext ctx;
    private readonly SysfsRoot root;
    private readonly PciGpu gpu;

    public SysfsGpuControl(LinuxContext ctx, PciGpu gpu, bool isNvidia)
    {
        this.ctx = ctx;
        root = ctx.Root;
        this.gpu = gpu;
        IsNvidia = isNvidia;
    }

    public bool IsNvidia { get; }
    public bool IsValid => true;

    public string FullName => PciIds.Describe(root, gpu.Vendor, gpu.DeviceId)
                              ?? root.ReadText(gpu.SysfsDir + "/product_name")
                              ?? $"{gpu.Vendor} GPU {gpu.DeviceId:X4}";

    private bool Asleep => PciGpu.Enumerate(root).FirstOrDefault(g => g.Address == gpu.Address)?.IsSuspended ?? false;

    private string? Hwmon => gpu.HwmonDir(root);

    public int? GetCurrentTemperature()
    {
        if (Asleep || Hwmon is not { } dir) return null;
        int? milli = root.ReadInt(dir + "/temp1_input");
        return milli is null ? null : milli.Value / 1000;
    }

    public int? GetGpuUse()
    {
        if (Asleep) return null;
        return root.ReadInt(gpu.SysfsDir + "/gpu_busy_percent");
    }

    public (long usedMb, long totalMb)? GetVramInfo()
    {
        if (Asleep) return null;
        long? used = root.ReadLong(gpu.SysfsDir + "/mem_info_vram_used");
        long? total = root.ReadLong(gpu.SysfsDir + "/mem_info_vram_total");
        if (used is null || total is null || total <= 0) return null;
        return (used.Value / (1024 * 1024), total.Value / (1024 * 1024));
    }

    public float? GetGpuPower()
    {
        if (Asleep) return 0f;
        if (Hwmon is not { } dir) return null;
        long? micro = root.ReadLong(dir + "/power1_average") ?? root.ReadLong(dir + "/power1_input");
        return micro is null ? null : micro.Value / 1_000_000f;
    }

    /// <summary>Applications holding the GPU's render node open, compositors and our own process excluded.</summary>
    public void KillGPUApps()
    {
        var nodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (string drm in root.ListDirectories(gpu.SysfsDir + "/drm"))
        {
            string name = Path.GetFileName(drm);
            if (name.StartsWith("renderD", StringComparison.Ordinal)) nodes.Add("/dev/dri/" + name);
        }
        if (nodes.Count == 0) return;

        GpuProcessKiller.Kill(ctx, target => nodes.Contains(target));
    }

    public void Dispose()
    {
    }
}

/// <summary>Shared "terminate programs using the GPU" logic.</summary>
internal static class GpuProcessKiller
{
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "Xorg", "X", "Xwayland", "gnome-shell", "gnome-session-b", "gnome-session-binary", "kwin_wayland", "kwin_x11",
        "plasmashell", "sway", "Hyprland", "wayfire", "weston", "mutter", "cinnamon", "xfwm4", "xfce4-session",
        "mate-session", "budgie-wm", "labwc", "river", "niri", "cosmic-comp", "cosmic-session", "systemd", "gdm", "sddm",
        "lightdm", "pipewire", "wireplumber", "ghelper", "GHelper.App",
        "nvidia-persiste", "nvidia-persistenced", "nvidia-powerd", "supergfxd",
    };

    /// <summary>Distinct process names (comm) holding a matching device node open, compositors and ourselves excluded.</summary>
    public static List<string> FindNames(LinuxContext ctx, Func<string, bool> deviceMatch)
    {
        var names = new List<string>();
        foreach (int pid in ProcFs.FindFileUsers(ctx.Root, deviceMatch, Environment.ProcessId))
        {
            if (pid <= 1) continue;
            string name = ctx.Root.ReadText($"/proc/{pid}/comm") ?? "";
            if (name.Length == 0 || Protected.Contains(name) || names.Contains(name)) continue;
            names.Add(name);
        }
        return names;
    }

    public static void Kill(LinuxContext ctx, Func<string, bool> deviceMatch)
    {
        foreach (int pid in ProcFs.FindFileUsers(ctx.Root, deviceMatch, Environment.ProcessId))
        {
            string name = ctx.Root.ReadText($"/proc/{pid}/comm") ?? "";
            if (Protected.Contains(name)) continue;
            // never take down a process of another user session or a kernel thread
            if (pid <= 1) continue;
            try
            {
                LinuxLog.Write("Kill:" + name);
                System.Diagnostics.Process.GetProcessById(pid).Kill();
            }
            catch (Exception ex)
            {
                LinuxLog.Write($"Can't kill {name}: {ex.Message}");
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------- pci.ids

internal static class PciIds
{
    private static readonly string[] Files =
        ["/usr/share/hwdata/pci.ids", "/usr/share/misc/pci.ids", "/usr/share/pci.ids", "/var/lib/pciutils/pci.ids"];

    public static string? Describe(SysfsRoot root, GpuVendor vendor, int device)
    {
        string vendorName = vendor switch
        {
            GpuVendor.Nvidia => "NVIDIA",
            GpuVendor.Amd => "AMD",
            GpuVendor.Intel => "Intel",
            _ => "",
        };
        int vendorId = vendor switch { GpuVendor.Nvidia => 0x10DE, GpuVendor.Amd => 0x1002, GpuVendor.Intel => 0x8086, _ => 0 };
        if (vendorId == 0) return null;

        foreach (string file in Files)
        {
            string[]? lines = root.ReadLines(file);
            if (lines is null) continue;

            string vendorKey = vendorId.ToString("x4");
            string deviceKey = "\t" + device.ToString("x4") + "  ";
            bool inVendor = false;
            foreach (string line in lines)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                if (line[0] != '\t')
                {
                    if (inVendor) break;
                    inVendor = line.StartsWith(vendorKey, StringComparison.Ordinal);
                    continue;
                }
                if (!inVendor || !line.StartsWith(deviceKey, StringComparison.Ordinal)) continue;

                string name = line[deviceKey.Length..].Trim();
                int open = name.IndexOf('[');
                int close = name.LastIndexOf(']');
                if (open >= 0 && close > open) name = name[(open + 1)..close];
                return $"{vendorName} {name}".Trim();
            }
        }
        return null;
    }
}
