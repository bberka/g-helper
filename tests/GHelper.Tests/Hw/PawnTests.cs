using PawnIO;
using Xunit;

namespace GHelper.Tests.Hw;

/// <summary>Scriptable stand-in for the PawnIO driver: emulates the SMU mailbox registers of the RyzenSMU module.</summary>
public sealed class FakePawnIo : IPawnIo
{
    private static readonly HashSet<uint> CommandRegisters = new()
    {
        0x03B10528, 0x03B10928, 0x03B10530, // MP1
        0x03B1051C, 0x03B10524, 0x03B10A20, 0x03B10924, // PSMU
    };

    public PawnConnectResult ConnectResult = PawnConnectResult.OK;
    public bool LoadResult = true;
    public byte[]? LoadedModule;
    public ulong CodeName = (ulong)CpuCodeName.Phoenix;
    public ulong SmuVersion = 0x004C2800;
    public uint PmTableVersion = 0x400005;
    public float[] PmTable = new float[128];
    public ulong Msr606;
    public ulong Msr611;

    public readonly Dictionary<uint, uint> Registers = new();
    public readonly List<(uint addr, uint value)> Writes = new();
    public readonly List<string> Calls = new();
    private readonly HashSet<uint> _zeroed = new();

    public bool IsConnected { get; private set; }
    public bool IsModuleLoaded { get; private set; }
    public bool Disposed { get; private set; }

    public PawnConnectResult Connect()
    {
        IsConnected = ConnectResult == PawnConnectResult.OK;
        return ConnectResult;
    }

    public bool LoadModule(byte[] data)
    {
        if (!IsConnected || !LoadResult) return false;
        LoadedModule = data;
        IsModuleLoaded = true;
        return true;
    }

    public bool Execute(string functionName, ulong[]? input, ulong[]? output)
    {
        Calls.Add(functionName);
        if (!IsModuleLoaded) return false;

        switch (functionName)
        {
            case "ioctl_get_code_name":
                output![0] = CodeName;
                return true;
            case "ioctl_get_smu_version":
                output![0] = SmuVersion;
                return true;
            case "ioctl_read_smu_register":
                output![0] = Registers.TryGetValue((uint)input![0], out uint v) ? v : 1u;
                return true;
            case "ioctl_write_smu_register":
                Write((uint)input![0], (uint)input![1]);
                return true;
            case "ioctl_resolve_pm_table":
                output![0] = PmTableVersion;
                return true;
            case "ioctl_update_pm_table":
                return true;
            case "ioctl_read_pm_table":
                for (int i = 0; i < output!.Length; i++)
                {
                    uint lo = BitConverter.SingleToUInt32Bits(PmTable[i * 2]);
                    uint hi = BitConverter.SingleToUInt32Bits(PmTable[i * 2 + 1]);
                    output[i] = lo | ((ulong)hi << 32);
                }
                return true;
            case "ioctl_read_msr":
                output![0] = input![0] == 0x606 ? Msr606 : Msr611;
                return true;
        }

        return false;
    }

    private void Write(uint addr, uint value)
    {
        Writes.Add((addr, value));
        Registers[addr] = value;

        if (value == 0) _zeroed.Add(addr);
        else _zeroed.Remove(addr);

        if (CommandRegisters.Contains(addr) && value != 0)
        {
            // the SMU answers "OK" on the response register that was cleared for this command
            foreach (uint rsp in _zeroed.ToArray()) { Registers[rsp] = 1; _zeroed.Remove(rsp); }
        }
    }

    public void Dispose() => Disposed = true;
}

[Collection("Hw platform")]
public class RyzenSmuTests
{
    [Theory]
    [InlineData(CpuCodeName.Colfax, CpuFamily.Zen1Desktop)]
    [InlineData(CpuCodeName.SummitRidge, CpuFamily.Zen1Desktop)]
    [InlineData(CpuCodeName.PinnacleRidge, CpuFamily.Zen1Desktop)]
    [InlineData(CpuCodeName.Threadripper, CpuFamily.Zen1Desktop)]
    [InlineData(CpuCodeName.CastlePeak, CpuFamily.Zen1Desktop)]
    [InlineData(CpuCodeName.RavenRidge, CpuFamily.Raven)]
    [InlineData(CpuCodeName.RavenRidge2, CpuFamily.Raven)]
    [InlineData(CpuCodeName.Picasso, CpuFamily.Raven)]
    [InlineData(CpuCodeName.Dali, CpuFamily.Raven)]
    [InlineData(CpuCodeName.Renoir, CpuFamily.Renoir)]
    [InlineData(CpuCodeName.Lucienne, CpuFamily.Renoir)]
    [InlineData(CpuCodeName.Cezanne, CpuFamily.Renoir)]
    [InlineData(CpuCodeName.Matisse, CpuFamily.Matisse)]
    [InlineData(CpuCodeName.Vermeer, CpuFamily.Matisse)]
    [InlineData(CpuCodeName.Vangogh, CpuFamily.Mobile)]
    [InlineData(CpuCodeName.Rembrandt, CpuFamily.Mobile)]
    [InlineData(CpuCodeName.Mendocino, CpuFamily.Mobile)]
    [InlineData(CpuCodeName.Phoenix, CpuFamily.Mobile)]
    [InlineData(CpuCodeName.Phoenix2, CpuFamily.Mobile)]
    [InlineData(CpuCodeName.HawkPoint, CpuFamily.Mobile)]
    [InlineData(CpuCodeName.FireFlight, CpuFamily.Mobile)]
    [InlineData(CpuCodeName.Raphael, CpuFamily.Raphael)]
    [InlineData(CpuCodeName.GraniteRidge, CpuFamily.Raphael)]
    [InlineData(CpuCodeName.DragonRange, CpuFamily.Raphael)]
    [InlineData(CpuCodeName.StrixPoint, CpuFamily.StrixPoint)]
    [InlineData(CpuCodeName.KrackanPoint, CpuFamily.StrixPoint)]
    [InlineData(CpuCodeName.KrackanPoint2, CpuFamily.StrixPoint)]
    [InlineData(CpuCodeName.StrixHalo, CpuFamily.StrixHalo)]
    [InlineData(CpuCodeName.ShimadaPeak, CpuFamily.ShimadaPeak)]
    [InlineData(CpuCodeName.Milan, CpuFamily.Unknown)]
    [InlineData(CpuCodeName.Rome, CpuFamily.Unknown)]
    [InlineData(CpuCodeName.Naples, CpuFamily.Unknown)]
    [InlineData(CpuCodeName.Genoa, CpuFamily.Unknown)]
    [InlineData(CpuCodeName.Mero, CpuFamily.Unknown)]
    [InlineData(CpuCodeName.Undefined, CpuFamily.Unknown)]
    public void CodeNameMapsToFamily(CpuCodeName name, CpuFamily family)
    {
        Assert.Equal(family, RyzenSmuService.GetFamily(name));
    }

    [Theory]
    [InlineData(CpuCodeName.Renoir, 1u)]
    [InlineData(CpuCodeName.Phoenix, 23u)]
    [InlineData(CpuCodeName.StrixPoint, 31u)]
    [InlineData(CpuCodeName.StrixHalo, 32u)]
    [InlineData(CpuCodeName.Raphael, 16u)]
    [InlineData(CpuCodeName.Undefined, 0xFFFFFFFFu)]
    public void CodeNameValuesMatchPawnModule(CpuCodeName name, uint value)
    {
        Assert.Equal(value, (uint)name);
    }

    [Fact]
    public void SmuStatusValuesMatchFirmwareResponses()
    {
        Assert.Equal(0x01u, (uint)SmuStatus.OK);
        Assert.Equal(0xFFu, (uint)SmuStatus.Failed);
        Assert.Equal(0xFEu, (uint)SmuStatus.UnknownCmd);
        Assert.Equal(0xFDu, (uint)SmuStatus.CmdRejectedPrereq);
        Assert.Equal(0xFCu, (uint)SmuStatus.CmdRejectedBusy);
    }

    [Fact]
    public void UninitialisedServiceCannotTune()
    {
        using var smu = new RyzenSmuService(new FakePawnIo { ConnectResult = PawnConnectResult.NotInstalled });

        Assert.False(smu.Initialize(new byte[] { 1 }));
        Assert.False(smu.IsInitialized);
        Assert.Equal(CpuCodeName.Undefined, smu.CpuCodeName);
        Assert.False(smu.CanSetTDP());
        Assert.False(smu.CanSetCoAll());
        Assert.False(smu.CanSetThm());
    }

    [Fact]
    public void InitializeReadsCodeNameAndVersion()
    {
        var io = new FakePawnIo { CodeName = (ulong)CpuCodeName.StrixHalo, SmuVersion = 0x00640A00 };
        using var smu = new RyzenSmuService(io);

        Assert.True(smu.Initialize(new byte[] { 1, 2, 3 }));

        Assert.True(smu.IsInitialized);
        Assert.Equal(CpuCodeName.StrixHalo, smu.CpuCodeName);
        Assert.Equal(CpuFamily.StrixHalo, smu.Family);
        Assert.Equal(0x00640A00u, smu.SmuVersion);
        Assert.Equal(new byte[] { 1, 2, 3 }, io.LoadedModule);
        Assert.True(smu.CanSetTDP());
    }

    [Fact]
    public void ModuleLoadFailureLeavesServiceUninitialised()
    {
        using var smu = new RyzenSmuService(new FakePawnIo { LoadResult = false });
        Assert.False(smu.Initialize(new byte[] { 1 }));
        Assert.False(smu.IsInitialized);
    }

    [Fact]
    public void SetCoAllOnMobileUsesMp1CommandWithEncodedCurve()
    {
        var io = new FakePawnIo { CodeName = (ulong)CpuCodeName.Phoenix };
        using var smu = new RyzenSmuService(io);
        smu.Initialize(new byte[] { 1 });

        var status = smu.SetCoAll(-20);

        Assert.Equal(SmuStatus.OK, status);
        // Mobile: MP1 mailbox, command 0x4C, argument = 0x100000 - 20
        Assert.Contains((0x03B10998u, 0x100000u - 20u), io.Writes);
        Assert.Contains((0x03B10528u, 0x4Cu), io.Writes);
    }

    [Fact]
    public void SetCoAllOnRaphaelUsesPsmuCommand()
    {
        var io = new FakePawnIo { CodeName = (ulong)CpuCodeName.Raphael };
        using var smu = new RyzenSmuService(io);
        smu.Initialize(new byte[] { 1 });

        Assert.Equal(SmuStatus.OK, smu.SetCoAll(-10));

        Assert.Contains((0x03B10A40u, 0x100000u - 10u), io.Writes);
        Assert.Contains((0x03B10524u, 0x07u), io.Writes);
    }

    [Fact]
    public void SetCoPerOnDragonRangeEncodesCcdAndCore()
    {
        var io = new FakePawnIo { CodeName = (ulong)CpuCodeName.Raphael };
        using var smu = new RyzenSmuService(io);
        smu.Initialize(new byte[] { 1 });

        // core 9 -> ccd 1, core 1: selector (1 << 8 | 1), offset in the low 16 bits
        Assert.Equal(SmuStatus.OK, smu.SetCoPer(9, -15));

        uint expected = ((1u << 8 | 1u) << 20) | (unchecked((uint)-15) & 0xFFFF);
        Assert.Contains((0x03B10A40u, expected), io.Writes);
        Assert.Contains((0x03B10524u, 0x06u), io.Writes);
    }

    [Fact]
    public void UnsupportedFamilyIsRejectedWithoutTouchingTheMailbox()
    {
        var io = new FakePawnIo { CodeName = (ulong)CpuCodeName.Matisse };
        using var smu = new RyzenSmuService(io);
        smu.Initialize(new byte[] { 1 });
        io.Writes.Clear();

        Assert.Equal(SmuStatus.Failed, smu.SetCoAll(-10));
        Assert.Equal(SmuStatus.Failed, smu.SetCoGfx(-10));
        Assert.Empty(io.Writes);
    }

    [Fact]
    public void SetAllLimitsOnMobileSendsMilliwattsToMp1()
    {
        var io = new FakePawnIo { CodeName = (ulong)CpuCodeName.HawkPoint };
        using var smu = new RyzenSmuService(io);
        smu.Initialize(new byte[] { 1 });

        Assert.True(smu.SetAllLimits(35, 45, 40));

        Assert.Contains((0x03B10528u, 0x14u), io.Writes); // STAPM
        Assert.Contains((0x03B10528u, 0x15u), io.Writes); // fast
        Assert.Contains((0x03B10528u, 0x16u), io.Writes); // slow
        Assert.Contains((0x03B10998u, 35_000u), io.Writes);
        Assert.Contains((0x03B10998u, 45_000u), io.Writes);
        Assert.Contains((0x03B10998u, 40_000u), io.Writes);
    }

    [Fact]
    public void SetThmOnRavenUsesMp1Command1F()
    {
        const uint cmd = 0x1F;
        var io = new FakePawnIo { CodeName = (ulong)CpuCodeName.Picasso };
        using var smu = new RyzenSmuService(io);
        smu.Initialize(new byte[] { 1 });

        Assert.Equal(SmuStatus.OK, smu.SetThm(85));

        Assert.Contains((0x03B10528u, cmd), io.Writes);
        Assert.Contains((0x03B10998u, 85u), io.Writes);
    }

    [Fact]
    public void PowerLimitsAreParsedFromThePmTable()
    {
        var io = new FakePawnIo { PmTableVersion = 0x400005 };
        io.PmTable[0] = 30f;   // stapm
        io.PmTable[2] = 54f;   // fast
        io.PmTable[4] = 45f;   // slow
        io.PmTable[6] = 40f;   // apu slow
        io.PmTable[16] = 95f;  // tctl for 0x40xxxx tables
        using var smu = new RyzenSmuService(io);
        smu.Initialize(new byte[] { 1 });

        var limits = smu.GetPowerLimits();

        Assert.NotNull(limits);
        Assert.Equal(30f, limits!.Stapm);
        Assert.Equal(54f, limits.Fast);
        Assert.Equal(45f, limits.Slow);
        Assert.Equal(95f, limits.TctlTemp);
        Assert.Equal(40f, limits.ApuSlow);
    }

    [Fact]
    public void RaphaelPmTableHasNoApuSlowAndTctlAtIndexTen()
    {
        var io = new FakePawnIo { PmTableVersion = 0x540004 };
        io.PmTable[0] = 65f;
        io.PmTable[10] = 89f;
        using var smu = new RyzenSmuService(io);
        smu.Initialize(new byte[] { 1 });

        var limits = smu.GetPowerLimits();

        Assert.NotNull(limits);
        Assert.Equal(89f, limits!.TctlTemp);
        Assert.Null(limits.ApuSlow);
    }

    [Fact]
    public void EmptyPmTableMeansNoLimits()
    {
        using var smu = new RyzenSmuService(new FakePawnIo());
        smu.Initialize(new byte[] { 1 });
        Assert.Null(smu.GetPowerLimits());
    }

    [Fact]
    public void EmbeddedModulesShipInsideCore()
    {
        Assert.Equal(38196, PawnModules.Load(PawnModules.RyzenSmuResource).Length);
        Assert.Equal(5292, PawnModules.Load(PawnModules.IntelMsrResource).Length);
        Assert.Throws<InvalidOperationException>(() => PawnModules.Load("GHelper.Nothing.bin"));
    }

    [Fact]
    public void InitializeWithoutArgumentsLoadsTheEmbeddedModule()
    {
        var io = new FakePawnIo();
        using var smu = new RyzenSmuService(io);

        Assert.True(smu.Initialize());
        Assert.Equal(38196, io.LoadedModule!.Length);
    }

    [Fact]
    public void DefaultConstructorTakesTheDriverFromThePlatform()
    {
        var platform = HwTestPlatform.Ensure();
        var io = new FakePawnIo { CodeName = (ulong)CpuCodeName.Renoir };
        platform.Tuning.Io = io;
        try
        {
            using var smu = new RyzenSmuService();
            Assert.True(smu.Initialize(new byte[] { 9 }));
            Assert.Equal(CpuFamily.Renoir, smu.Family);
            Assert.Same(io, PawnModules.CreateDriver());
        }
        finally
        {
            platform.Tuning.Io = null;
        }
    }

    [Fact]
    public void PlatformWithoutPawnIoNeverInitialises()
    {
        var platform = HwTestPlatform.Ensure();
        platform.Tuning.Io = null;

        using var smu = new RyzenSmuService();
        Assert.False(smu.Initialize(new byte[] { 1 }));
        Assert.False(RyzenSmuService.IsPawnInstalled());
    }

    [Fact]
    public void PawnInstalledProbeDistinguishesMissingFromDenied()
    {
        var platform = HwTestPlatform.Ensure();
        try
        {
            platform.Tuning.Io = new FakePawnIo { ConnectResult = PawnConnectResult.NotInstalled };
            Assert.False(RyzenSmuService.IsPawnInstalled());

            platform.Tuning.Io = new FakePawnIo { ConnectResult = PawnConnectResult.AccessDenied };
            Assert.True(RyzenSmuService.IsPawnInstalled());
        }
        finally
        {
            platform.Tuning.Io = null;
        }
    }
}

[Collection("Hw platform")]
public class IntelMsrTests
{
    [Theory]
    [InlineData(0x000A0E03UL, 1.0 / 16384)]  // typical client part: ESU = 14
    [InlineData(0x00000300UL, 1.0 / 8)]
    [InlineData(0x00001000UL, 1.0 / 65536)]
    public void EnergyUnitIsDerivedFromEsuBits(ulong raplUnit, double expected)
    {
        Assert.Equal(expected, IntelMsr.EnergyUnitFromRaplUnit(raplUnit), 12);
    }

    [Fact]
    public void PowerHandlesCounterWrapAround()
    {
        double unit = 1.0 / 16384;
        // 16384 ticks = 1 J over 0.5 s = 2 W, counter wraps across uint.MaxValue
        double watts = IntelMsr.PowerFromCounters(uint.MaxValue - 100, 16283, unit, 0.5);
        Assert.Equal(2.0, watts, 6);
    }

    [Fact]
    public void InitializeReadsRaplUnitAndFirstSampleIsPrimingOnly()
    {
        var io = new FakePawnIo { Msr606 = 0x000A0E03, Msr611 = 1000 };
        using var msr = new IntelMsr(io);

        Assert.True(msr.Initialize(new byte[] { 1 }));
        Assert.True(msr.IsInitialized);
        Assert.Null(msr.GetPackagePower());   // first read only stores the counter
    }

    [Fact]
    public void WithoutDriverNothingInitialises()
    {
        using var msr = new IntelMsr((IPawnIo?)null);
        Assert.False(msr.Initialize(new byte[] { 1 }));
        Assert.Null(msr.GetPackagePower());
    }

    [Fact]
    public void MissingDriverIsReported()
    {
        using var msr = new IntelMsr(new FakePawnIo { ConnectResult = PawnConnectResult.AccessDenied });
        Assert.False(msr.Initialize());
    }
}

public class CpuInfoTests
{
    private static uint[] Regs(string text)
    {
        var bytes = new byte[48];
        System.Text.Encoding.ASCII.GetBytes(text).CopyTo(bytes, 0);
        var regs = new uint[12];
        for (int i = 0; i < 12; i++) regs[i] = BitConverter.ToUInt32(bytes, i * 4);
        return regs;
    }

    [Fact]
    public void BrandStringIsDecodedAndTrimmed()
    {
        Assert.Equal("AMD Ryzen 9 7940HS w/ Radeon 780M Graphics", CpuInfo.DecodeBrandString(Regs("AMD Ryzen 9 7940HS w/ Radeon 780M Graphics")));
        Assert.Equal("Intel(R) Core(TM) i9-13980HX", CpuInfo.DecodeBrandString(Regs("   Intel(R) Core(TM) i9-13980HX")));
        Assert.Equal("", CpuInfo.DecodeBrandString(new uint[12]));
    }

    [Fact]
    public void CaptionMatchesWindowsIdentifierFormat()
    {
        // Phoenix: family 0x19 (base 0xF + ext 0xA), model 0x74 (ext 7, base 4), stepping 1
        Assert.Equal("AMD64 Family 25 Model 116 Stepping 1", CpuInfo.FormatCaption(true, 0x00A70F41));
        // Alder Lake-P: family 6, model 0x9A (ext 9, base 0xA), stepping 3
        Assert.Equal("Intel64 Family 6 Model 154 Stepping 3", CpuInfo.FormatCaption(false, 0x000906A3));
    }

    [Theory]
    [InlineData("AMD Ryzen 9 7940HS w/ Radeon 780M Graphics", true, false)]
    [InlineData("AMD RYZEN AI MAX+ 395 w/ Radeon 8060S", true, false)]
    [InlineData("AMD Ryzen AI 9 HX 370", true, false)]
    [InlineData("AMD Ryzen 7 4800H", true, false)]
    [InlineData("AMD Ryzen 7 6800H", false, false)]
    [InlineData("AMD Ryzen 9 6900HS", true, true)]
    [InlineData("Intel(R) Core(TM) i9-13980HX", false, false)]
    public void UndervoltSupportIsDecidedByTheCpuName(string name, bool cpuUv, bool igpuUv)
    {
        Assert.Equal(cpuUv, CpuInfo.IsSupportedUV(name));
        Assert.Equal(igpuUv, CpuInfo.IsSupportedUViGPU(name));
    }
}
