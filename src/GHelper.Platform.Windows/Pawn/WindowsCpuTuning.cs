using System.Runtime.InteropServices;

namespace GHelper.Platform.Windows.Pawn;

/// <summary>
/// <see cref="ICpuTuning"/> on top of the PawnIO driver: Ryzen SMU mailbox (power limits, curve optimizer, temperature
/// limit) and Intel RAPL package power over MSR reads. The signed driver modules (RyzenSMU.bin / IntelMSR.bin) are
/// embedded in this assembly. PawnIO is x64 only: everywhere else <see cref="IsSupported"/> is false and no driver
/// is ever opened.
/// W1-B's driver IO interface (GHelper.Pawn.IPawnIo) was not available when this was written, so the logic runs on the
/// local <see cref="PawnIoDevice"/> (see report).
/// </summary>
public sealed class WindowsCpuTuning : ICpuTuning
{
    private readonly object smuLock = new();
    private RyzenSmuService? smu;
    private IntelMsr? intelMsr;
    private bool intelMsrFailed;
    private bool disposed;

    public bool IsAmd => CpuVendor.Current == CpuVendorKind.Amd;

    public bool IsIntel => CpuVendor.Current == CpuVendorKind.Intel;

    public bool IsSupported =>
        OperatingSystem.IsWindows()
        && RuntimeInformation.ProcessArchitecture == Architecture.X64
        && (IsAmd || IsIntel);

    public bool IsDriverInstalled
    {
        get
        {
            if (!IsSupported) return false;

            try { return RyzenSmuService.IsPawnInstalled(); }
            catch { return false; }
        }
    }

    public string? SmuFamily => GetSmu()?.Family.ToString();

    public bool Initialize()
    {
        if (!IsSupported) return false;
        return IsAmd ? GetSmu() is not null : GetIntelMsr() is not null;
    }

    private RyzenSmuService? GetSmu()
    {
        if (!IsSupported || !IsAmd || disposed) return null;

        lock (smuLock)
        {
            if (smu is { IsInitialized: true }) return smu;

            smu?.Dispose();
            smu = null;

            try
            {
                var candidate = new RyzenSmuService();
                if (!candidate.Initialize(typeof(WindowsCpuTuning).Assembly))
                {
                    candidate.Dispose();
                    return null;
                }

                Logger.WriteLine($"SMU Init: {candidate.CpuCodeName} ({candidate.Family}), SMU v{candidate.SmuVersion >> 16}.{(candidate.SmuVersion >> 8) & 0xFF}.{candidate.SmuVersion & 0xFF}");
                smu = candidate;
            }
            catch (Exception ex)
            {
                Logger.WriteLine("SMU Init: " + ex.Message);
            }

            return smu;
        }
    }

    private IntelMsr? GetIntelMsr()
    {
        if (!IsSupported || !IsIntel || intelMsrFailed || disposed) return null;

        lock (smuLock)
        {
            if (intelMsr is not null) return intelMsr;

            try
            {
                var msr = new IntelMsr();
                if (!msr.Initialize(typeof(WindowsCpuTuning).Assembly))
                {
                    msr.Dispose();
                    intelMsrFailed = true;
                    Logger.WriteLine("Intel MSR: PawnIO/IntelMSR module unavailable (not installed?)");
                    return null;
                }

                intelMsr = msr;
                Logger.WriteLine("CPU Power source: Intel RAPL MSR (PawnIO)");
            }
            catch (Exception ex)
            {
                intelMsrFailed = true;
                Logger.WriteLine("Intel MSR: " + ex.Message);
            }

            return intelMsr;
        }
    }

    // --- Ryzen --------------------------------------------------------------------------------

    public bool CanSetTDP() => GetSmu()?.CanSetTDP() ?? false;

    public bool CanSetCoAll() => GetSmu()?.CanSetCoAll() ?? false;

    public bool CanSetThm() => GetSmu()?.CanSetThm() ?? false;

    public bool SetAllLimits(int stapmW, int fastW, int slowW)
    {
        var service = GetSmu();
        if (service is null) return false;

        service.SetAllLimits(stapmW, fastW, slowW, out SmuStatus stapm, out SmuStatus fast, out SmuStatus slow);
        Logger.WriteLine($"STAPM: {stapmW}W {stapm} | SLOW: {slowW}W {slow} | FAST: {fastW}W {fast}");
        return stapm == SmuStatus.OK && fast == SmuStatus.OK && slow == SmuStatus.OK;
    }

    // SmuStatus values: 1 OK, 0xFF failed, 0xFE unknown command, 0xFD rejected (prerequisite), 0xFC rejected (busy)
    public int SetCoAll(int value) => ToStatus(GetSmu()?.SetCoAll(value));

    public int SetCoPer(int core, int value) => ToStatus(GetSmu()?.SetCoPer(core, value));

    public int SetCoGfx(int value) => ToStatus(GetSmu()?.SetCoGfx(value));

    public int SetThm(int celsius) => ToStatus(GetSmu()?.SetThm(celsius));

    private static int ToStatus(SmuStatus? status) => status.HasValue ? (int)status.Value : (int)SmuStatus.Failed;

    public (int stapmW, int fastW, int slowW)? GetPowerLimits()
    {
        var service = GetSmu();
        if (service is null) return null;

        try
        {
            var limits = service.GetPowerLimits();
            if (limits is null) return null;

            string line = $"SPL: {limits.Stapm:F1}W | sPPT {limits.Slow:F1}W | fPPT {limits.Fast:F1}W";
            if (limits.ApuSlow.HasValue) line += $" | APU {limits.ApuSlow.Value:F1}W";
            line += $", Temp: {limits.TctlTemp:F0}°C";
            Logger.WriteLine("Ryzen Limits: " + line);

            return ((int)Math.Round(limits.Stapm), (int)Math.Round(limits.Fast), (int)Math.Round(limits.Slow));
        }
        catch (Exception ex)
        {
            Logger.WriteLine("ReadRyzenLimits Error: " + ex);
            return null;
        }
    }

    // --- Intel --------------------------------------------------------------------------------

    public float? GetIntelPackagePower()
    {
        var msr = GetIntelMsr();
        if (msr is null) return null;

        try
        {
            float? power = msr.GetPackagePower();
            return power > 0 ? power : null;
        }
        catch (Exception ex)
        {
            intelMsrFailed = true;
            Logger.WriteLine("Intel MSR power read failed: " + ex.Message);
            return null;
        }
    }

    public void Dispose()
    {
        lock (smuLock)
        {
            disposed = true;
            smu?.Dispose();
            smu = null;
            intelMsr?.Dispose();
            intelMsr = null;
        }
    }
}
