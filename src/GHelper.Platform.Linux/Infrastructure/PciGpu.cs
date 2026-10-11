namespace GHelper.Platform.Linux;

internal enum GpuVendor { Unknown, Nvidia, Amd, Intel }

/// <summary>A display class (0x03xxxx) PCI device from /sys/bus/pci/devices.</summary>
internal sealed record PciGpu(string Address, string SysfsDir, GpuVendor Vendor, int DeviceId, string? Driver, bool IsBootVga)
{
    /// <summary>drm card directory (/sys/bus/pci/devices/.../drm/cardN) name, null when no DRM driver is bound.</summary>
    public string? DrmCard { get; init; }

    public string RuntimeStatus { get; init; } = "";

    /// <summary>The device is runtime-suspended (powered down): polling it from user space would wake it.</summary>
    public bool IsSuspended => RuntimeStatus.Equals("suspended", StringComparison.OrdinalIgnoreCase);

    public static List<PciGpu> Enumerate(SysfsRoot root)
    {
        var list = new List<PciGpu>();
        foreach (string dir in root.ListDirectories("/sys/bus/pci/devices"))
        {
            string? cls = root.ReadText(dir + "/class");
            if (cls is null || !cls.StartsWith("0x03", StringComparison.Ordinal)) continue;
            // 0x0302 = 3D controller, 0x0300 = VGA, 0x0380 = other display; 0x0380 includes some GPUs, keep all

            int vendorId = root.ReadInt(dir + "/vendor") ?? 0;
            GpuVendor vendor = vendorId switch
            {
                0x10DE => GpuVendor.Nvidia,
                0x1002 => GpuVendor.Amd,
                0x8086 => GpuVendor.Intel,
                _ => GpuVendor.Unknown,
            };

            string? link = root.ReadLink(dir + "/driver");
            string? driver = link is null ? null : Path.GetFileName(link.TrimEnd('/'));

            string? card = root.ListDirectories(dir + "/drm")
                .Select(Path.GetFileName)
                .FirstOrDefault(n => n is not null && n.StartsWith("card", StringComparison.Ordinal) && !n.Contains('-'));

            list.Add(new PciGpu(Path.GetFileName(dir), dir, vendor, root.ReadInt(dir + "/device") ?? 0, driver,
                root.ReadInt(dir + "/boot_vga") == 1)
            {
                DrmCard = card,
                RuntimeStatus = root.ReadText(dir + "/power/runtime_status") ?? "",
            });
        }
        return list;
    }

    /// <summary>hwmon directory of the device (temp1_input, power1_average, ...).</summary>
    public string? HwmonDir(SysfsRoot root)
        => root.ListDirectories(SysfsDir + "/hwmon").FirstOrDefault();
}
