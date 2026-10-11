using System.Globalization;

namespace GHelper.Platform.Linux;

/// <summary>hwmon (k10temp / coretemp / asus), /proc/stat, /proc/meminfo, power_supply and RAPL based sensors.</summary>
public sealed class LinuxSystemSensors : ISystemSensors
{
    private const string Hwmon = "/sys/class/hwmon";

    private readonly SysfsRoot root;
    private readonly AsusWmiLayout layout;

    private (ulong total, ulong idle)? lastCpu;
    private double lastEnergyUj = -1;
    private long lastEnergyTick;
    private string? raplEnergyPath;

    public LinuxSystemSensors(LinuxContext ctx, AsusWmiLayout? layout = null)
    {
        root = ctx.Root;
        this.layout = layout ?? new AsusWmiLayout(root);
    }

    // ------------------------------------------------------------------ CPU temperature

    public float? GetCpuTemp()
    {
        foreach (string dir in root.ListDirectories(Hwmon))
        {
            string? name = root.ReadText(dir + "/name");
            if (name is not ("k10temp" or "zenpower" or "coretemp")) continue;

            float? value = name == "coretemp" ? ReadCoretemp(dir) : ReadK10Temp(dir);
            if (value is not null) return value;
        }

        // generic fallbacks: ACPI thermal zone, x86 package thermal
        foreach (string dir in root.ListDirectories("/sys/class/thermal"))
        {
            string? type = root.ReadText(dir + "/type");
            if (type is "x86_pkg_temp" or "acpitz")
            {
                int? milli = root.ReadInt(dir + "/temp");
                if (milli is > 0) return milli.Value / 1000f;
            }
        }
        return null;
    }

    private float? ReadK10Temp(string dir)
    {
        // temp1 = Tctl (what Ryzen fan control uses), temp2 = Tdie/Tccd on some boards
        foreach (string input in root.ListFiles(dir).Where(f => f.EndsWith("_input", StringComparison.Ordinal) && Path.GetFileName(f).StartsWith("temp")))
        {
            string label = root.ReadText(input.Replace("_input", "_label")) ?? "";
            if (label is "Tctl" or "Tdie")
            {
                int? milli = root.ReadInt(input);
                if (milli is not null) return milli.Value / 1000f;
            }
        }
        int? fallback = root.ReadInt(dir + "/temp1_input");
        return fallback is null ? null : fallback.Value / 1000f;
    }

    private float? ReadCoretemp(string dir)
    {
        float? best = null;
        foreach (string input in root.ListFiles(dir).Where(f => f.EndsWith("_input", StringComparison.Ordinal) && Path.GetFileName(f).StartsWith("temp")))
        {
            string label = root.ReadText(input.Replace("_input", "_label")) ?? "";
            int? milli = root.ReadInt(input);
            if (milli is null) continue;
            float value = milli.Value / 1000f;
            if (label.StartsWith("Package", StringComparison.Ordinal)) return value;
            if (best is null || value > best) best = value;
        }
        return best;
    }

    // ------------------------------------------------------------------ CPU usage

    internal static (ulong total, ulong idle)? ParseCpuStat(string? line)
    {
        if (line is null || !line.StartsWith("cpu ", StringComparison.Ordinal)) return null;
        string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 5) return null;

        ulong total = 0;
        ulong idle = 0;
        for (int i = 1; i < f.Length && i <= 8; i++) // user nice system idle iowait irq softirq steal
        {
            if (!ulong.TryParse(f[i], NumberStyles.None, CultureInfo.InvariantCulture, out ulong v)) return null;
            total += v;
            if (i == 4 || i == 5) idle += v;
        }
        return (total, idle);
    }

    public float? GetCpuUsage()
    {
        string? first = root.ReadLines("/proc/stat")?.FirstOrDefault();
        var current = ParseCpuStat(first);
        if (current is null) return null;

        var previous = lastCpu ?? (0UL, 0UL); // first call: average since boot
        lastCpu = current;

        ulong totalDelta = current.Value.total - previous.total;
        ulong idleDelta = current.Value.idle - previous.idle;
        if (totalDelta == 0) return 0f;
        return (float)(100.0 * (totalDelta - idleDelta) / totalDelta);
    }

    // ------------------------------------------------------------------ CPU power

    private string? FindEnergyPath()
    {
        if (raplEnergyPath is not null) return raplEnergyPath;
        foreach (string candidate in new[] { "/sys/class/powercap/intel-rapl:0/energy_uj", "/sys/class/powercap/intel-rapl/intel-rapl:0/energy_uj" })
        {
            if (root.FileExists(candidate) && root.ReadLong(candidate) is not null)
                return raplEnergyPath = candidate;
        }
        return null;
    }

    public float? GetCpuPower()
    {
        string? path = FindEnergyPath();
        if (path is null) return null;

        long? energy = root.ReadLong(path);
        if (energy is null) return null;

        long tick = Environment.TickCount64;
        double previous = lastEnergyUj;
        long previousTick = lastEnergyTick;
        lastEnergyUj = energy.Value;
        lastEnergyTick = tick;

        if (previous < 0) return null;
        double seconds = (tick - previousTick) / 1000.0;
        if (seconds < 0.05) return null;

        double delta = energy.Value - previous;
        if (delta < 0)
        {
            long range = root.ReadLong(path.Replace("energy_uj", "max_energy_range_uj")) ?? 0;
            if (range <= 0) return null;
            delta += range;
        }
        return (float)(delta / 1_000_000.0 / seconds);
    }

    public void ResetCpuPowerCounter()
    {
        lastEnergyUj = -1;
        lastEnergyTick = 0;
    }

    // ------------------------------------------------------------------ memory / battery / fans

    internal static (int percent, int usedMb)? ParseMeminfo(IEnumerable<string>? lines)
    {
        if (lines is null) return null;
        long total = -1, available = -1, free = 0, buffers = 0, cached = 0;
        foreach (string line in lines)
        {
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            string key = line[..colon];
            string[] parts = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !long.TryParse(parts[0], out long kb)) continue;
            switch (key)
            {
                case "MemTotal": total = kb; break;
                case "MemAvailable": available = kb; break;
                case "MemFree": free = kb; break;
                case "Buffers": buffers = kb; break;
                case "Cached": cached = kb; break;
            }
        }
        if (total <= 0) return null;
        if (available < 0) available = free + buffers + cached; // kernels before 3.14

        long used = Math.Max(0, total - available);
        return ((int)Math.Round(used * 100.0 / total), (int)(used / 1024));
    }

    public (int percent, int usedMb)? GetRamInfo() => ParseMeminfo(root.ReadLines("/proc/meminfo"));

    public BatteryState? GetBatteryState() => PowerSupplyInfo.ReadBattery(root);

    public int? GetFanRpm(int fanIndex)
    {
        string? dir = layout.FanHwmonDir;
        if (dir is null) return null;
        // hwmon fanN_input is already RPM
        return root.ReadInt($"{dir}/fan{fanIndex + 1}_input");
    }
}
