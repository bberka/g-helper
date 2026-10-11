using GHelper.Platform;

namespace PawnIO
{
    /// <summary>
    /// The one <see cref="ICpuTuning"/> built on Core's <see cref="RyzenSmuService"/> (Ryzen SMU mailbox: power limits,
    /// curve optimizer, temperature limit) and <see cref="IntelMsr"/> (RAPL package power) over a platform supplied
    /// <see cref="IPawnIo"/> driver. A platform whose driver speaks the PawnIO module protocol (Windows: PawnIO) only has
    /// to provide the driver factory; platforms with a different kernel interface (Linux: ryzen_smu sysfs) implement
    /// <see cref="ICpuTuning"/> directly.
    /// Also reports the SMU code name / family through <see cref="ICpuFamilyInfo"/> (Core reads it to decide when the
    /// limits have to be re-applied).
    /// </summary>
    public class PawnCpuTuning : ICpuTuning, ICpuFamilyInfo
    {
        private readonly Func<IPawnIo?> _driverFactory;
        private readonly bool _platformSupported;
        private readonly bool? _isAmd;
        private readonly bool? _isIntel;
        private readonly object _gate = new();

        private RyzenSmuService? _smu;
        private IntelMsr? _intelMsr;
        private bool _intelMsrFailed;
        private bool _disposed;

        /// <param name="driverFactory">New, unconnected driver instance per call (the caller owns and disposes it); null = none.</param>
        /// <param name="platformSupported">OS / architecture gate (e.g. PawnIO is Windows x64 only).</param>
        /// <param name="isAmd">Override of the CPUID vendor detection (tests); null = the host CPU.</param>
        /// <param name="isIntel">Override of the CPUID vendor detection (tests); null = the host CPU.</param>
        public PawnCpuTuning(Func<IPawnIo?> driverFactory, bool platformSupported = true, bool? isAmd = null, bool? isIntel = null)
        {
            _driverFactory = driverFactory;
            _platformSupported = platformSupported;
            _isAmd = isAmd;
            _isIntel = isIntel;
        }

        public bool IsAmd => _isAmd ?? CpuInfo.IsAMD;

        public bool IsIntel => _isIntel ?? CpuInfo.IsIntel;

        public bool IsSupported => _platformSupported && (IsAmd || IsIntel);

        /// <summary>A new driver instance for Core's RyzenSmuService / IntelMsr default constructors.</summary>
        public IPawnIo? CreatePawnIo()
        {
            if (!IsSupported) return null;
            try { return _driverFactory(); }
            catch (Exception ex)
            {
                Logger.WriteLine("PawnIO driver: " + ex.Message);
                return null;
            }
        }

        /// <summary>The driver is present, even when opening it needs elevation (legacy RyzenSmuService.IsPawnInstalled).</summary>
        public bool IsDriverInstalled
        {
            get
            {
                if (!IsSupported) return false;

                try
                {
                    using var probe = _driverFactory();
                    return probe is not null && probe.Connect() != PawnConnectResult.NotInstalled;
                }
                catch
                {
                    return false;
                }
            }
        }

        public string? CpuFamily
        {
            get
            {
                var smu = _smu;
                if (smu is not { IsInitialized: true }) return null;
                return smu.Family.ToString();
            }
        }

        public string? CpuCodeName
        {
            get
            {
                var smu = _smu;
                if (smu is not { IsInitialized: true }) return null;
                return smu.CpuCodeName.ToString();
            }
        }

        public bool Initialize()
        {
            if (!IsSupported) return false;
            return IsAmd ? GetSmu() is not null : GetIntelMsr() is not null;
        }

        private RyzenSmuService? GetSmu()
        {
            if (!IsSupported || !IsAmd || _disposed) return null;

            lock (_gate)
            {
                if (_smu is { IsInitialized: true }) return _smu;

                _smu?.Dispose();
                _smu = null;

                try
                {
                    var io = _driverFactory();
                    if (io is null) return null;

                    var candidate = new RyzenSmuService(io);
                    if (!candidate.Initialize())
                    {
                        candidate.Dispose();
                        return null;
                    }

                    Logger.WriteLine($"SMU Init: {candidate.CpuCodeName} ({candidate.Family}), SMU v{candidate.SmuVersion >> 16}.{(candidate.SmuVersion >> 8) & 0xFF}.{candidate.SmuVersion & 0xFF}");
                    _smu = candidate;
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("SMU Init: " + ex.Message);
                }

                return _smu;
            }
        }

        private IntelMsr? GetIntelMsr()
        {
            if (!IsSupported || !IsIntel || _intelMsrFailed || _disposed) return null;

            lock (_gate)
            {
                if (_intelMsr is not null) return _intelMsr;

                try
                {
                    var io = _driverFactory();
                    var msr = new IntelMsr(io);
                    if (!msr.Initialize())
                    {
                        msr.Dispose();
                        _intelMsrFailed = true;
                        Logger.WriteLine("Intel MSR: PawnIO/IntelMSR module unavailable (not installed?)");
                        return null;
                    }

                    _intelMsr = msr;
                    Logger.WriteLine("CPU Power source: Intel RAPL MSR (PawnIO)");
                }
                catch (Exception ex)
                {
                    _intelMsrFailed = true;
                    Logger.WriteLine("Intel MSR: " + ex.Message);
                }

                return _intelMsr;
            }
        }

        // --- Ryzen ----------------------------------------------------------------------------

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

        // --- Intel ----------------------------------------------------------------------------

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
                _intelMsrFailed = true;
                Logger.WriteLine("Intel MSR power read failed: " + ex.Message);
                return null;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _smu?.Dispose();
                _smu = null;
                _intelMsr?.Dispose();
                _intelMsr = null;
            }
        }
    }
}
