using System.Reflection;

namespace PawnIO
{
    public sealed class IntelMsr : IDisposable
    {
        private const uint MSR_RAPL_POWER_UNIT   = 0x606;
        private const uint MSR_PKG_ENERGY_STATUS = 0x611;

        private readonly IPawnIo? _io;
        private bool _init;
        private double _energyUnit;
        private uint _lastEnergy;
        private long _lastTick;

        public IntelMsr() : this(PawnModules.CreateDriver())
        {
        }

        public IntelMsr(IPawnIo? io)
        {
            _io = io;
        }

        public bool IsInitialized => _init;

        public bool Initialize() => Initialize(PawnModules.Load(PawnModules.IntelMsrResource));

        public bool Initialize(Assembly assembly) => Initialize(PawnModules.Load(PawnModules.IntelMsrResource, assembly));

        public bool Initialize(byte[] moduleData)
        {
            if (_init) return true;
            if (_io is null) return false;
            if (_io.Connect() != PawnConnectResult.OK || !_io.LoadModule(moduleData)) return false;

            if (!ReadMsr(MSR_RAPL_POWER_UNIT, out ulong unit)) return false;
            _energyUnit = EnergyUnitFromRaplUnit(unit);

            _init = true;
            return true;
        }

        /// <summary>Joules per energy counter tick: 1 / 2^ESU, ESU = bits [12:8] of MSR_RAPL_POWER_UNIT.</summary>
        public static double EnergyUnitFromRaplUnit(ulong raplPowerUnit)
        {
            int esu = (int)((raplPowerUnit >> 8) & 0x1F);
            return 1.0 / (1UL << esu);
        }

        /// <summary>Average watts between two reads of the 32-bit package energy counter (wrap-around safe).</summary>
        public static double PowerFromCounters(uint previous, uint current, double energyUnit, double seconds)
        {
            double joules = unchecked(current - previous) * energyUnit;
            return joules / seconds;
        }

        public float? GetPackagePower()
        {
            if (!_init || !ReadMsr(MSR_PKG_ENERGY_STATUS, out ulong raw)) return null;

            uint energy = (uint)raw;
            long tick = Environment.TickCount64;

            if (_lastTick == 0) { _lastEnergy = energy; _lastTick = tick; return null; }

            double seconds = (tick - _lastTick) / 1000.0;
            if (seconds < 0.05) return null;

            double watts = PowerFromCounters(_lastEnergy, energy, _energyUnit, seconds);
            _lastEnergy = energy;
            _lastTick = tick;

            return (float)watts;
        }

        private bool ReadMsr(uint msr, out ulong value)
        {
            value = 0;
            if (_io is null) return false;
            var output = new ulong[1];
            if (!_io.Execute("ioctl_read_msr", new ulong[] { msr }, output)) return false;
            value = output[0];
            return true;
        }

        public void Dispose() => _io?.Dispose();
    }
}
