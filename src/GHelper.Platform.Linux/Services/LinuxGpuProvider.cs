using System.Runtime.InteropServices;
using System.Text;
using GHelper.Gpu;

namespace GHelper.Platform.Linux;

/// <summary>
/// IGpuProvider for Linux: NVIDIA through NVML (libnvidia-ml.so.1, loaded with NativeLibrary so a missing driver is
/// not an error), AMD / Intel through the DRM sysfs files (hwmon temp/power, gpu_busy_percent, mem_info_vram_*).
/// A runtime-suspended GPU is never touched: querying it would wake the dGPU the user switched off.
/// </summary>
public sealed class LinuxGpuProvider : IGpuProvider
{
    private readonly LinuxContext ctx;
    private NvmlLibrary? nvml;

    public LinuxGpuProvider(LinuxContext ctx)
    {
        this.ctx = ctx;
    }

    public IGpuControl? CreateDiscrete()
    {
        try
        {
            List<PciGpu> gpus = PciGpu.Enumerate(ctx.Root);

            PciGpu? nvidia = gpus.FirstOrDefault(g => g.Vendor == GpuVendor.Nvidia && g.Driver is "nvidia" or "nouveau" or "nova_core");
            if (nvidia is not null)
            {
                if (nvidia.Driver == "nvidia")
                {
                    nvml ??= NvmlLibrary.TryLoad();
                    if (nvml is not null) return new NvmlGpuControl(ctx, nvidia, nvml);
                }
                return new SysfsGpuControl(ctx, nvidia, isNvidia: true);
            }

            // AMD dGPU: not the boot VGA device on a hybrid laptop, or lots of dedicated memory
            PciGpu? amd = gpus.FirstOrDefault(g => g.Vendor == GpuVendor.Amd && g.Driver == "amdgpu" && IsDiscreteAmd(g));
            if (amd is not null) return new SysfsGpuControl(ctx, amd, isNvidia: false);

            PciGpu? intel = gpus.FirstOrDefault(g => g.Vendor == GpuVendor.Intel && g.Driver is "i915" or "xe" && HasDedicatedMemory(g));
            if (intel is not null) return new SysfsGpuControl(ctx, intel, isNvidia: false);
        }
        catch (Exception ex)
        {
            LinuxLog.Write("GPU detection: " + ex.Message);
        }
        return null;
    }

    public IGpuControl? CreateAmdApu()
    {
        try
        {
            PciGpu? apu = PciGpu.Enumerate(ctx.Root)
                .FirstOrDefault(g => g.Vendor == GpuVendor.Amd && g.Driver == "amdgpu" && !IsDiscreteAmd(g));
            return apu is null ? null : new SysfsGpuControl(ctx, apu, isNvidia: false);
        }
        catch (Exception ex)
        {
            LinuxLog.Write("AMD APU detection: " + ex.Message);
            return null;
        }
    }

    private bool IsDiscreteAmd(PciGpu gpu)
    {
        long? vram = ctx.Root.ReadLong(gpu.SysfsDir + "/mem_info_vram_total");
        if (vram is > 3L * 1024 * 1024 * 1024) return true;   // APUs carve out at most a few GB
        if (vram is not null && gpu.IsBootVga) return false;
        return !gpu.IsBootVga && vram is > 1L * 1024 * 1024 * 1024;
    }

    private bool HasDedicatedMemory(PciGpu gpu)
        => ctx.Root.ReadLong(gpu.SysfsDir + "/mem_info_vram_total") is > 0
           || ctx.Root.DirectoryExists(gpu.SysfsDir + "/lmem_total_bytes") || ctx.Root.FileExists(gpu.SysfsDir + "/lmem_total_bytes");

    public bool InitIntelVram() => false;

    public void SetIntelVramIndex(int index)
    {
        // Windows only: adapter index of the dedicated Intel GPU for the legacy IntelVram helper
    }

    public void UnloadNative()
    {
        nvml?.Dispose();
        nvml = null;
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

// ---------------------------------------------------------------------------------------------- NVML

internal sealed unsafe class NvmlLibrary : IDisposable
{
    private const int Success = 0;

    private nint library;
    private readonly delegate* unmanaged<int> init;
    private readonly delegate* unmanaged<int> shutdown;
    private readonly delegate* unmanaged<byte*, nint*, int> byPciBusId;
    private readonly delegate* unmanaged<uint, nint*, int> byIndex;
    private readonly delegate* unmanaged<nint, byte*, uint, int> getName;
    private readonly delegate* unmanaged<nint, uint, uint*, int> getTemperature;
    private readonly delegate* unmanaged<nint, uint*, int> getUtilization;
    private readonly delegate* unmanaged<nint, ulong*, int> getMemory;
    private readonly delegate* unmanaged<nint, uint*, int> getPower;
    private readonly object gate = new();
    private bool initialised;

    private NvmlLibrary(nint library)
    {
        this.library = library;
        init = (delegate* unmanaged<int>)Export("nvmlInit_v2");
        shutdown = (delegate* unmanaged<int>)Export("nvmlShutdown");
        byPciBusId = (delegate* unmanaged<byte*, nint*, int>)Export("nvmlDeviceGetHandleByPciBusId_v2");
        byIndex = (delegate* unmanaged<uint, nint*, int>)Export("nvmlDeviceGetHandleByIndex_v2");
        getName = (delegate* unmanaged<nint, byte*, uint, int>)Export("nvmlDeviceGetName");
        getTemperature = (delegate* unmanaged<nint, uint, uint*, int>)Export("nvmlDeviceGetTemperature");
        getUtilization = (delegate* unmanaged<nint, uint*, int>)Export("nvmlDeviceGetUtilizationRates");
        getMemory = (delegate* unmanaged<nint, ulong*, int>)Export("nvmlDeviceGetMemoryInfo");
        getPower = (delegate* unmanaged<nint, uint*, int>)Export("nvmlDeviceGetPowerUsage");
    }

    private nint Export(string name)
        => NativeLibrary.TryGetExport(library, name, out nint address) ? address : 0;

    public static NvmlLibrary? TryLoad()
    {
        foreach (string name in new[] { "libnvidia-ml.so.1", "libnvidia-ml.so" })
        {
            try
            {
                if (NativeLibrary.TryLoad(name, out nint handle))
                {
                    var lib = new NvmlLibrary(handle);
                    if (lib.init != null && lib.getTemperature != null) return lib;
                    lib.Dispose();
                }
            }
            catch { }
        }
        return null;
    }

    public bool EnsureInit()
    {
        lock (gate)
        {
            if (initialised) return true;
            try { initialised = init() == Success; }
            catch { initialised = false; }
            return initialised;
        }
    }

    public nint? OpenDevice(string pciAddress)
    {
        if (!EnsureInit()) return null;
        lock (gate)
        {
            nint handle;
            if (byPciBusId != null)
            {
                byte[] id = Encoding.ASCII.GetBytes(pciAddress + "\0");
                fixed (byte* p = id)
                    if (byPciBusId(p, &handle) == Success) return handle;
            }
            if (byIndex != null && byIndex(0, &handle) == Success) return handle;
        }
        return null;
    }

    public string? Name(nint device)
    {
        if (getName == null) return null;
        byte* buffer = stackalloc byte[96];
        lock (gate)
        {
            if (getName(device, buffer, 96) != Success) return null;
        }
        return Marshal.PtrToStringAnsi((nint)buffer);
    }

    public int? Temperature(nint device)
    {
        uint value;
        lock (gate)
        {
            if (getTemperature == null || getTemperature(device, 0, &value) != Success) return null;
        }
        return (int)value;
    }

    public int? Utilization(nint device)
    {
        uint* rates = stackalloc uint[2];
        lock (gate)
        {
            if (getUtilization == null || getUtilization(device, rates) != Success) return null;
        }
        return (int)rates[0];
    }

    public (long usedMb, long totalMb)? Memory(nint device)
    {
        ulong* mem = stackalloc ulong[3]; // total, free, used
        lock (gate)
        {
            if (getMemory == null || getMemory(device, mem) != Success) return null;
        }
        return ((long)(mem[2] / (1024 * 1024)), (long)(mem[0] / (1024 * 1024)));
    }

    public float? Power(nint device)
    {
        uint milliWatt;
        lock (gate)
        {
            if (getPower == null || getPower(device, &milliWatt) != Success) return null;
        }
        if (milliWatt > 1_000_000) return null;
        return milliWatt / 1000f;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (initialised && shutdown != null)
            {
                try { shutdown(); } catch { }
            }
            initialised = false;
            if (library != 0)
            {
                try { NativeLibrary.Free(library); } catch { }
                library = 0;
            }
        }
    }
}

internal sealed class NvmlGpuControl : IGpuControl
{
    private readonly LinuxContext ctx;
    private readonly PciGpu gpu;
    private readonly NvmlLibrary nvml;
    private nint? device;
    private string? name;

    public NvmlGpuControl(LinuxContext ctx, PciGpu gpu, NvmlLibrary nvml)
    {
        this.ctx = ctx;
        this.gpu = gpu;
        this.nvml = nvml;
    }

    public bool IsNvidia => true;
    public bool IsValid => true;

    public string FullName
    {
        get
        {
            if (name is not null) return name;
            if (Active() && Handle() is nint h && nvml.Name(h) is { } n) return name = n;
            return PciIds.Describe(ctx.Root, GpuVendor.Nvidia, gpu.DeviceId) ?? "NVIDIA GPU";
        }
    }

    /// <summary>false while the dGPU is runtime-suspended (do not wake it).</summary>
    private bool Active()
    {
        string? status = ctx.Root.ReadText(gpu.SysfsDir + "/power/runtime_status");
        return !string.Equals(status, "suspended", StringComparison.OrdinalIgnoreCase);
    }

    private nint? Handle() => device ??= nvml.OpenDevice(gpu.Address);

    public int? GetCurrentTemperature() => Active() && Handle() is nint h ? nvml.Temperature(h) : null;

    public int? GetGpuUse() => Active() && Handle() is nint h ? nvml.Utilization(h) : null;

    public (long usedMb, long totalMb)? GetVramInfo() => Active() && Handle() is nint h ? nvml.Memory(h) : null;

    public float? GetGpuPower() => Active() ? (Handle() is nint h ? nvml.Power(h) : null) : 0f;

    public void KillGPUApps()
    {
        GpuProcessKiller.Kill(ctx, target => target.StartsWith("/dev/nvidia", StringComparison.Ordinal)
                                             && !target.StartsWith("/dev/nvidia-modeset", StringComparison.Ordinal)
                                             && !target.StartsWith("/dev/nvidiactl", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        device = null;
    }
}
