namespace GHelper.Platform.Linux;

/// <summary>SMU mailbox status codes (same values as the legacy PawnIO service).</summary>
internal enum LinuxSmuStatus : uint
{
    OK = 0x01,
    Failed = 0xFF,
    UnknownCmd = 0xFE,
    CmdRejectedPrereq = 0xFD,
    CmdRejectedBusy = 0xFC,
}

internal enum RyzenFamily
{
    Unknown,
    Zen1Desktop,
    Raven,
    Renoir,
    Matisse,
    Mobile,
    Raphael,
    StrixPoint,
    StrixHalo,
}

internal sealed record CpuIdentity(string Vendor, int Family, int Model, string ModelName)
{
    public bool IsAmd => Vendor == "AuthenticAMD";
    public bool IsIntel => Vendor == "GenuineIntel";

    public static CpuIdentity? Read(SysfsRoot root)
    {
        string[]? lines = root.ReadLines("/proc/cpuinfo");
        if (lines is null) return null;

        string vendor = "", name = "";
        int family = 0, model = 0;
        foreach (string line in lines)
        {
            if (line.Length == 0 && vendor.Length > 0) break; // first logical CPU only
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "vendor_id": vendor = value; break;
                case "model name": name = value; break;
                case "cpu family": int.TryParse(value, out family); break;
                case "model": int.TryParse(value, out model); break;
            }
        }
        return vendor.Length == 0 ? null : new CpuIdentity(vendor, family, model, name);
    }

    /// <summary>AMD family/model to the SMU command routing family (mirrors the CPU code names of the Pawn service).</summary>
    public RyzenFamily ResolveRyzenFamily()
    {
        if (!IsAmd) return RyzenFamily.Unknown;

        return Family switch
        {
            0x17 => Model switch
            {
                0x01 or 0x08 or 0x31 => RyzenFamily.Zen1Desktop,
                0x10 or 0x11 or 0x18 or 0x20 => RyzenFamily.Raven,
                >= 0x60 and <= 0x6F => RyzenFamily.Renoir,       // Renoir 0x60, Lucienne 0x68
                0x71 => RyzenFamily.Matisse,
                0x90 or 0x91 => RyzenFamily.Mobile,               // Van Gogh
                >= 0xA0 and <= 0xAF => RyzenFamily.Mobile,        // Mendocino
                _ => RyzenFamily.Unknown,
            },
            0x19 => Model switch
            {
                0x21 or 0x20 or 0x22 or 0x23 or 0x01 or 0x08 => RyzenFamily.Matisse, // Vermeer, Milan, Chagall
                >= 0x50 and <= 0x5F => RyzenFamily.Renoir,       // Cezanne
                >= 0x40 and <= 0x4F => RyzenFamily.Mobile,       // Rembrandt
                0x61 or 0x60 => RyzenFamily.Raphael,             // Raphael, Dragon Range
                >= 0x74 and <= 0x7F => RyzenFamily.Mobile,       // Phoenix, Hawk Point
                _ => RyzenFamily.Unknown,
            },
            0x1A => Model switch
            {
                >= 0x20 and <= 0x2F => RyzenFamily.StrixPoint,   // Strix Point
                >= 0x60 and <= 0x6F => RyzenFamily.StrixPoint,   // Krackan Point
                >= 0x70 and <= 0x7F => RyzenFamily.StrixHalo,    // Strix Halo
                >= 0x40 and <= 0x4F => RyzenFamily.Raphael,      // Granite Ridge
                _ => RyzenFamily.Unknown,
            },
            _ => RyzenFamily.Unknown,
        };
    }
}

/// <summary>
/// Ryzen SMU mailbox access through the out-of-tree ryzen_smu kernel module (/sys/kernel/ryzen_smu_drv):
/// arguments go to smu_args (6 x u32), the command id to mp1_smu_cmd / rsmu_cmd (the module executes it and blocks until
/// the SMU answered). The command tables are the ones of the legacy PawnIO based RyzenSmuService (RyzenAdj derived).
/// </summary>
internal sealed class RyzenSmuLinux
{
    public const string Dir = "/sys/kernel/ryzen_smu_drv";

    private readonly SysfsRoot root;
    private readonly object gate = new();

    public RyzenSmuLinux(SysfsRoot root, RyzenFamily family)
    {
        this.root = root;
        Family = family;
    }

    public RyzenFamily Family { get; }

    public static bool DriverPresent(SysfsRoot root)
        => root.FileExists(Dir + "/smu_args") && (root.FileExists(Dir + "/mp1_smu_cmd") || root.FileExists(Dir + "/rsmu_cmd"));

    public bool CanSend => Family != RyzenFamily.Unknown && DriverPresent(root);

    private LinuxSmuStatus Send(string commandFile, uint cmd, uint arg)
    {
        string path = Dir + "/" + commandFile;
        if (!root.FileExists(path)) return LinuxSmuStatus.Failed;

        lock (gate)
        {
            var args = new byte[24];
            BitConverter.GetBytes(arg).CopyTo(args, 0);
            if (!root.TryWriteBytes(Dir + "/smu_args", args)) return LinuxSmuStatus.Failed;
            if (!root.TryWriteBytes(path, BitConverter.GetBytes(cmd))) return LinuxSmuStatus.Failed;

            // the module reports the SMU response code when the command file is read back
            byte[]? status = root.ReadBytes(path);
            if (status is { Length: >= 4 })
            {
                uint code = BitConverter.ToUInt32(status, 0);
                return Enum.IsDefined(typeof(LinuxSmuStatus), code) ? (LinuxSmuStatus)code : LinuxSmuStatus.Failed;
            }
            return LinuxSmuStatus.OK;
        }
    }

    private LinuxSmuStatus Mp1(uint cmd, uint arg) => Send("mp1_smu_cmd", cmd, arg);
    private LinuxSmuStatus Psmu(uint cmd, uint arg) => Send("rsmu_cmd", cmd, arg);

    private static uint EncodeCurve(int steps) => (uint)(0x100000 - (uint)(-steps));

    public LinuxSmuStatus SetStapm(int watts)
    {
        uint mw = (uint)watts * 1000;
        switch (Family)
        {
            case RyzenFamily.Raven: return Mp1(0x1A, mw);
            case RyzenFamily.Renoir:
                var s = Mp1(0x14, mw);
                Psmu(0x31, mw);
                return s;
            case RyzenFamily.Mobile:
            case RyzenFamily.StrixPoint:
            case RyzenFamily.StrixHalo: return Mp1(0x14, mw);
            case RyzenFamily.Raphael: return Mp1(0x4F, mw);
            default: return LinuxSmuStatus.Failed;
        }
    }

    public LinuxSmuStatus SetFast(int watts)
    {
        uint mw = (uint)watts * 1000;
        switch (Family)
        {
            case RyzenFamily.Raven: return Mp1(0x1B, mw);
            case RyzenFamily.Renoir:
                var s = Mp1(0x15, mw);
                Psmu(0x32, mw);
                return s;
            case RyzenFamily.Mobile:
            case RyzenFamily.StrixPoint:
            case RyzenFamily.StrixHalo: return Mp1(0x15, mw);
            case RyzenFamily.Raphael: return Mp1(0x3E, mw);
            default: return LinuxSmuStatus.Failed;
        }
    }

    public LinuxSmuStatus SetSlow(int watts)
    {
        uint mw = (uint)watts * 1000;
        switch (Family)
        {
            case RyzenFamily.Raven: return Mp1(0x1C, mw);
            case RyzenFamily.Renoir:
                var s = Mp1(0x16, mw);
                Psmu(0x33, mw);
                Psmu(0x34, mw);
                return s;
            case RyzenFamily.Mobile:
            case RyzenFamily.StrixPoint:
            case RyzenFamily.StrixHalo: return Mp1(0x16, mw);
            case RyzenFamily.Raphael: return Mp1(0x5F, mw);
            default: return LinuxSmuStatus.Failed;
        }
    }

    public LinuxSmuStatus SetCoAll(int value)
    {
        uint v = EncodeCurve(value);
        return Family switch
        {
            RyzenFamily.Renoir => Mp1(0x55, v),
            RyzenFamily.Mobile or RyzenFamily.StrixPoint => Mp1(0x4C, v),
            RyzenFamily.StrixHalo => Mp1(0x4C, v) is var s && s == LinuxSmuStatus.OK ? s : Psmu(0x5D, v),
            RyzenFamily.Raphael => Psmu(0x07, v),
            _ => LinuxSmuStatus.Failed,
        };
    }

    public LinuxSmuStatus SetCoPer(int core, int value)
    {
        uint sel = Family == RyzenFamily.Raphael ? (uint)(core / 8 << 8 | core % 8) : (uint)core;
        uint v = sel << 20 | ((uint)value & 0xFFFF);
        return Family switch
        {
            RyzenFamily.Renoir => Mp1(0x54, v),
            RyzenFamily.Mobile or RyzenFamily.StrixPoint or RyzenFamily.StrixHalo => Mp1(0x4B, v),
            RyzenFamily.Raphael => Psmu(0x06, v),
            _ => LinuxSmuStatus.Failed,
        };
    }

    public LinuxSmuStatus SetCoGfx(int value)
    {
        uint v = EncodeCurve(value);
        return Family switch
        {
            RyzenFamily.Renoir => Mp1(0x64, v),
            RyzenFamily.Mobile or RyzenFamily.StrixHalo => Psmu(0xB7, v),
            _ => LinuxSmuStatus.Failed,
        };
    }

    public LinuxSmuStatus SetThm(int celsius)
    {
        uint v = (uint)celsius;
        return Family switch
        {
            RyzenFamily.Raven => Mp1(0x1F, v),
            RyzenFamily.Renoir or RyzenFamily.Mobile or RyzenFamily.StrixPoint or RyzenFamily.StrixHalo => Mp1(0x19, v),
            RyzenFamily.Matisse => Mp1(0x3E, v),
            RyzenFamily.Raphael => Mp1(0x3F, v),
            _ => LinuxSmuStatus.Failed,
        };
    }

    /// <summary>(STAPM, fast, slow) limits in watts from the PM table, null when unreadable.</summary>
    public (int stapm, int fast, int slow)? ReadPowerLimits()
    {
        byte[]? table = root.ReadBytes(Dir + "/pm_table");
        if (table is null || table.Length < 24) return null;

        float stapm = BitConverter.ToSingle(table, 0);
        float fast = BitConverter.ToSingle(table, 8);
        float slow = BitConverter.ToSingle(table, 16);
        if (stapm <= 0f || float.IsNaN(stapm) || stapm > 500f) return null;
        return ((int)Math.Round(stapm), (int)Math.Round(fast), (int)Math.Round(slow));
    }
}

/// <summary>
/// ICpuTuning on Linux. AMD: ryzen_smu module (TDP, curve optimizer, thermal limit, PM table). Intel: package power from
/// RAPL powercap / the msr module (the legacy app only reads power on Intel, limits go through ACPI).
/// </summary>
public sealed class LinuxCpuTuning : ICpuTuning
{
    private const string RaplEnergy = "/sys/class/powercap/intel-rapl:0/energy_uj";
    private const string RaplRange = "/sys/class/powercap/intel-rapl:0/max_energy_range_uj";

    private readonly SysfsRoot root;
    private readonly CpuIdentity? cpu;
    private RyzenSmuLinux? smu;
    private bool initialised;

    private double lastEnergyUj = -1;
    private long lastTick;

    public LinuxCpuTuning(LinuxContext ctx)
    {
        root = ctx.Root;
        cpu = CpuIdentity.Read(root);
    }

    public bool IsAmd => cpu?.IsAmd ?? false;
    public bool IsIntel => cpu?.IsIntel ?? false;

    public bool IsSupported => IsAmd ? RyzenSmuLinux.DriverPresent(root) : IsIntel && ReadIntelEnergy() is not null;

    public bool Initialize()
    {
        if (initialised) return IsSupported;
        initialised = true;

        if (cpu is { IsAmd: true })
        {
            RyzenFamily family = cpu.ResolveRyzenFamily();
            smu = new RyzenSmuLinux(root, family);
            if (!smu.CanSend)
                LinuxLog.Write(RyzenSmuLinux.DriverPresent(root)
                    ? $"Unknown AMD CPU family {cpu.Family:X}h model {cpu.Model:X}h"
                    : "ryzen_smu kernel module not loaded: CPU tuning unavailable");
        }
        return IsSupported;
    }

    private RyzenSmuLinux? Smu
    {
        get
        {
            if (!initialised) Initialize();
            return smu is { CanSend: true } ? smu : null;
        }
    }

    public bool CanSetTDP() => Smu is not null;
    public bool CanSetCoAll() => Smu is not null;
    public bool CanSetThm() => Smu is not null;

    public bool SetAllLimits(int stapmW, int fastW, int slowW)
    {
        if (Smu is not { } s) return false;
        return s.SetStapm(stapmW) == LinuxSmuStatus.OK & s.SetFast(fastW) == LinuxSmuStatus.OK & s.SetSlow(slowW) == LinuxSmuStatus.OK;
    }

    public int SetCoAll(int value) => Smu is { } s ? (int)s.SetCoAll(value) : (int)LinuxSmuStatus.Failed;

    public int SetCoPer(int core, int value) => Smu is { } s ? (int)s.SetCoPer(core, value) : (int)LinuxSmuStatus.Failed;

    public int SetCoGfx(int value) => Smu is { } s ? (int)s.SetCoGfx(value) : (int)LinuxSmuStatus.Failed;

    public int SetThm(int celsius) => Smu is { } s ? (int)s.SetThm(celsius) : (int)LinuxSmuStatus.Failed;

    public (int stapmW, int fastW, int slowW)? GetPowerLimits()
    {
        if (Smu is not { } s) return null;
        var limits = s.ReadPowerLimits();
        return limits is { } l ? (l.stapm, l.fast, l.slow) : null;
    }

    // ------------------------------------------------------------------ Intel package power

    /// <summary>Energy counter in microjoules (wraps at the returned range).</summary>
    private (double energyUj, double rangeUj)? ReadIntelEnergy()
    {
        long? energy = root.ReadLong(RaplEnergy);
        if (energy is not null)
        {
            long range = root.ReadLong(RaplRange) ?? long.MaxValue;
            return (energy.Value, range);
        }

        // msr module: MSR_RAPL_POWER_UNIT (0x606) energy unit, MSR_PKG_ENERGY_STATUS (0x611) 32 bit counter
        ulong? unit = ReadMsr(0x606);
        ulong? raw = ReadMsr(0x611);
        if (unit is null || raw is null) return null;

        int esu = (int)((unit.Value >> 8) & 0x1F);
        double joulesPerTick = 1.0 / (1UL << esu);
        return ((uint)raw.Value * joulesPerTick * 1_000_000.0, uint.MaxValue * joulesPerTick * 1_000_000.0);
    }

    private ulong? ReadMsr(long msr)
    {
        try
        {
            using var handle = File.OpenHandle(root.Map("/dev/cpu/0/msr"), FileMode.Open, FileAccess.Read);
            var buffer = new byte[8];
            if (RandomAccess.Read(handle, buffer, msr) != 8) return null;
            return BitConverter.ToUInt64(buffer, 0);
        }
        catch
        {
            return null;
        }
    }

    public float? GetIntelPackagePower()
    {
        if (!IsIntel) return null;
        var sample = ReadIntelEnergy();
        if (sample is null) return null;

        long tick = Environment.TickCount64;
        double energy = sample.Value.energyUj;
        double previous = lastEnergyUj;
        long previousTick = lastTick;
        lastEnergyUj = energy;
        lastTick = tick;

        if (previous < 0) return null;
        double seconds = (tick - previousTick) / 1000.0;
        if (seconds < 0.05) return null;

        double delta = energy - previous;
        if (delta < 0) delta += sample.Value.rangeUj; // counter wrapped
        return (float)(delta / 1_000_000.0 / seconds);
    }

    public void Dispose()
    {
    }
}
