using GHelper.Gpu;
using GHelper.Gpu.AMD;
using GHelper.Gpu.NVidia;
using Xunit;

namespace GHelper.Tests.Hw;

public sealed class FakeAmdDriver : IAmdGpuDriver
{
    public bool HasDiscrete { get; set; } = true;
    public bool HasIntegrated { get; set; } = true;
    public string DiscreteName { get; set; } = "AMD Radeon RX 6850M XT";

    public AmdSensorSample? Discrete;
    public AmdSensorSample? Integrated;
    public int DiscreteQueries;
    public bool Legacy;
    public AmdLegacySample LegacySample;
    public long? TotalVram = 12288;
    public int? UsedVram = 1024;
    public int TotalVramQueries;
    public int? SceDisplay = 3;
    public int SceState = 2;
    public int? LastSetSce;
    public int FpsLimit = 60;
    public float Frames = 59.5f;
    public List<string> Apps = new();
    public bool Disposed;

    public AmdSensorSample? QuerySensors(bool integrated)
    {
        if (!integrated) DiscreteQueries++;
        return integrated ? Integrated : Discrete;
    }

    public bool IsLegacyOverdrive() => Legacy;
    public AmdLegacySample QueryLegacy() => LegacySample;

    public long? GetTotalVramMb(bool integrated)
    {
        TotalVramQueries++;
        return TotalVram;
    }

    public int? GetUsedVramMb(bool integrated) => UsedVram;
    public bool SetVariBright(int enabled) => enabled == 0;
    public bool GetVariBright(out int supported, out int enabled)
    {
        supported = 1;
        enabled = 1;
        return true;
    }

    public int? FindSceDisplay() => SceDisplay;
    public int? GetSceState(int displayIndex) => SceState;
    public bool SetSceState(int displayIndex, int state)
    {
        LastSetSce = state;
        return true;
    }

    public void StartFrameMetrics() { }
    public void StopFrameMetrics() { }
    public float GetFrameMetrics() => Frames;
    public int GetFpsLimit() => FpsLimit;
    public bool SetFpsLimit(int limit)
    {
        FpsLimit = limit;
        return true;
    }

    public IReadOnlyList<string> GetGpuApplications() => Apps;
    public void Dispose() => Disposed = true;
}

[Collection("Hw platform")]
public class NvmlTests : IDisposable
{
    public NvmlTests()
    {
        HwTestPlatform.Ensure();
        NvmlHelper.SetLibraryCandidates(["ghelper-test-no-such-nvml-library", "/nonexistent/libnvidia-ml.so.1"]);
    }

    public void Dispose() => NvmlHelper.SetLibraryCandidates(null);

    [Fact]
    public void MissingLibraryDegradesGracefully()
    {
        Assert.False(NvmlHelper.IsAvailable);
        NvmlHelper.Init();

        Assert.Equal(0, NvmlHelper.GetDeviceCount());
        Assert.Null(NvmlHelper.GetDeviceName());
        Assert.Null(NvmlHelper.GetTemperature());
        Assert.Null(NvmlHelper.GetGpuPower());
        Assert.Null(NvmlHelper.GetMemoryInfo());
        Assert.Null(NvmlHelper.GetGpuUtilization());
        Assert.Empty(NvmlHelper.GetProcessIds());
        Assert.False(NvmlHelper.SupportsClockOffsets);
        Assert.False(NvmlHelper.GetClockOffsets(out int core, out int memory));
        Assert.Equal((0, 0), (core, memory));
        Assert.Equal(-1, NvmlHelper.SetClockOffsets(0, 0));
        Assert.Equal(-1, NvmlHelper.SetLockedClock(1500));
        NvmlHelper.Shutdown();
    }

    [Fact]
    public void NvidiaControlIsInvalidButSafeWithoutNvml()
    {
        using var gpu = new NvidiaGpuControl();

        Assert.False(gpu.IsValid);
        Assert.False(gpu.IsNvidia);
        Assert.Equal("", gpu.FullName);
        Assert.Null(gpu.GetCurrentTemperature());
        Assert.Null(gpu.GetGpuUse());
        Assert.Null(gpu.GetGpuPower());
        Assert.Null(gpu.GetVramInfo());
        Assert.False(gpu.GetClocks(out _, out _));
        gpu.KillGPUApps();
    }

    [Fact]
    public void FactoryFindsNothingWithoutAnyGpuDriver()
    {
        Assert.Null(GpuControlFactory.CreateDiscrete());
    }

    [Fact]
    public void DefaultLibraryNamesCoverBothPlatforms()
    {
        NvmlHelper.SetLibraryCandidates(null);
        var names = NvmlHelper.LibraryCandidates;

        if (OperatingSystem.IsWindows()) Assert.Contains("nvml.dll", names);
        else Assert.Contains("libnvidia-ml.so.1", names);
    }

    [Theory]
    [InlineData(0, new[] { "NVIDIA GeForce RTX 4060 Laptop GPU" })]
    [InlineData(1, new[] { "NVIDIA GeForce RTX 4090", "NVIDIA GeForce RTX 4070 Laptop GPU" })]
    [InlineData(0, new[] { "NVIDIA RTX A2000", "NVIDIA GeForce GTX 1650" })]
    [InlineData(-1, new string[0])]
    public void InternalGpuIsTheLaptopPart(int expected, string[] names)
    {
        Assert.Equal(expected, NvmlHelper.SelectInternalDevice(names));
    }
}

[Collection("Hw platform")]
public class AmdGpuControlTests
{
    private static AmdGpuControl Create(FakeAmdDriver driver)
    {
        HwTestPlatform.Ensure();
        return new AmdGpuControl(driver);
    }

    [Fact]
    public void ReportsDiscreteAdapterIdentity()
    {
        using var gpu = Create(new FakeAmdDriver());

        Assert.True(gpu.IsValid);
        Assert.False(gpu.IsNvidia);
        Assert.Equal("AMD Radeon RX 6850M XT", gpu.FullName);
    }

    [Fact]
    public void NoDiscreteAdapterIsInvalidAndReturnsNulls()
    {
        using var gpu = Create(new FakeAmdDriver { HasDiscrete = false, HasIntegrated = false });

        Assert.False(gpu.IsValid);
        Assert.Null(gpu.GetCurrentTemperature());
        Assert.Null(gpu.GetGpuUse());
        Assert.Null(gpu.GetGpuPower());
        Assert.Null(gpu.GetVramInfo());
        Assert.Equal(0, gpu.GetiGpuPower());
        Assert.False(gpu.GetOledPowerOptimization());
    }

    [Fact]
    public void SensorsComeFromThePmLog()
    {
        var driver = new FakeAmdDriver
        {
            Discrete = new AmdSensorSample { TemperatureEdge = 61, GfxActivity = 37, AsicPower = 55 },
        };
        using var gpu = Create(driver);

        Assert.Equal(61, gpu.GetCurrentTemperature());
        Assert.Equal(37, gpu.GetGpuUse());
        Assert.Equal(55f, gpu.GetGpuPower());
    }

    [Fact]
    public void PmLogIsCachedBetweenCloseReads()
    {
        var driver = new FakeAmdDriver { Discrete = new AmdSensorSample { TemperatureEdge = 50, GfxActivity = 1 } };
        using var gpu = Create(driver);

        gpu.GetCurrentTemperature();
        gpu.GetGpuUse();
        gpu.GetGpuPower();

        Assert.Equal(1, driver.DiscreteQueries);
    }

    [Fact]
    public void PowerPrefersAsicThenGfxThenBoard()
    {
        var driver = new FakeAmdDriver { Discrete = new AmdSensorSample { AsicPower = 0, GfxPower = 0, BoardPower = 33 } };
        using var gpu = Create(driver);

        Assert.Equal(33f, gpu.GetGpuPower());
    }

    [Fact]
    public void LegacyOverdriveIsTheFallbackForOldGpus()
    {
        var driver = new FakeAmdDriver
        {
            Discrete = null,
            Legacy = true,
            LegacySample = new AmdLegacySample(70, 12, 88.5f),
        };
        using var gpu = Create(driver);

        Assert.Equal(70, gpu.GetCurrentTemperature());
        Assert.Equal(12, gpu.GetGpuUse());
        Assert.Equal(88.5f, gpu.GetGpuPower());
    }

    [Fact]
    public void ModernGpuWithoutSensorsDoesNotUseLegacyPath()
    {
        var driver = new FakeAmdDriver { Discrete = null, Legacy = false, LegacySample = new AmdLegacySample(99, 99, 99) };
        using var gpu = Create(driver);

        Assert.Null(gpu.GetCurrentTemperature());
        Assert.Null(gpu.GetGpuPower());
    }

    [Fact]
    public void TotalVramIsQueriedOnce()
    {
        var driver = new FakeAmdDriver { TotalVram = 8192, UsedVram = 2048 };
        using var gpu = Create(driver);

        Assert.Equal((2048L, 8192L), gpu.GetVramInfo());
        driver.UsedVram = 4096;
        Assert.Equal((4096L, 8192L), gpu.GetVramInfo());
        Assert.Equal(1, driver.TotalVramQueries);
    }

    [Fact]
    public void IntegratedSensorsAreExposedForAllyAutoTdp()
    {
        var driver = new FakeAmdDriver
        {
            HasDiscrete = false,
            Integrated = new AmdSensorSample { TemperatureSoc = 48, GfxActivity = 77, AsicPower = 14, CpuPower = 9, GfxPower = 5 },
        };
        using var gpu = Create(driver);

        Assert.False(gpu.IsValid);
        Assert.Equal(14, gpu.GetiGpuPower());
        Assert.Equal(77, gpu.GetiGpuUse());
        Assert.Equal((48, 77, 5, 9, 14), gpu.GetiGpuSensors());
        Assert.Equal(59.5f, gpu.GetFPS());
        Assert.Equal(60, gpu.GetFPSLimit());
        Assert.Equal(1, gpu.SetFPSLimit(30));
        Assert.Equal(30, driver.FpsLimit);
    }

    [Fact]
    public void OledPowerOptimisationReadAndDisable()
    {
        var driver = new FakeAmdDriver { SceState = 2 };
        using var gpu = Create(driver);

        Assert.True(gpu.GetOledPowerOptimization());
        Assert.True(gpu.DisableOledPowerOptimization());
        Assert.Equal(1, driver.LastSetSce);
    }

    [Fact]
    public void OledPowerOptimisationUnsupportedDisplay()
    {
        var driver = new FakeAmdDriver { SceDisplay = -1 };
        using var gpu = Create(driver);

        Assert.False(gpu.GetOledPowerOptimization());
        Assert.False(gpu.DisableOledPowerOptimization());
        Assert.Null(driver.LastSetSce);
    }

    [Fact]
    public void KillGpuAppsSkipsSystemProcesses()
    {
        var platform = HwTestPlatform.Ensure();
        platform.ProcessService.Killed.Clear();
        var driver = new FakeAmdDriver { Apps = { "game", "svchost", "Xorg", "blender" } };
        using var gpu = new AmdGpuControl(driver);

        gpu.KillGPUApps();

        Assert.Equal(new[] { "game", "blender" }, platform.ProcessService.Killed);
    }

    [Fact]
    public void DriverComesFromThePlatformWhenNotPassed()
    {
        var platform = HwTestPlatform.Ensure();
        var driver = new FakeAmdDriver();
        platform.GpuProvider.AmdDriver = driver;
        try
        {
            using var gpu = new AmdGpuControl();
            Assert.True(gpu.IsValid);
        }
        finally
        {
            platform.GpuProvider.AmdDriver = null;
        }
    }

    [Fact]
    public void DisposeReleasesTheDriver()
    {
        var driver = new FakeAmdDriver();
        var gpu = Create(driver);
        gpu.Dispose();

        Assert.True(driver.Disposed);
        Assert.False(gpu.IsValid);
    }

    [Fact]
    public void FactoryFallsBackToAmdAndRecognisesTheXgmSpecialCard()
    {
        var platform = HwTestPlatform.Ensure();
        NvmlHelper.SetLibraryCandidates(["ghelper-test-no-such-nvml-library"]);
        try
        {
            var gpu = GpuControlFactory.CreateDiscrete(new FakeAmdDriver());
            Assert.NotNull(gpu);
            Assert.False(gpu!.IsNvidia);
            Assert.Equal(1, AppConfig.Get("xgm_special"));
            AppConfig.Remove("xgm_special");
            gpu.Dispose();
        }
        finally
        {
            NvmlHelper.SetLibraryCandidates(null);
        }
    }
}

[Collection("Hw platform")]
public class IntelVramTests
{
    [Fact]
    public void FacadeDelegatesToTheGpuProvider()
    {
        var platform = HwTestPlatform.Ensure();
        platform.GpuProvider.IntelVramAvailable = true;
        platform.GpuProvider.Sizes = [1, 2, 4, 8];
        platform.GpuProvider.Index = 3;

        Assert.True(IntelVram.Init());
        Assert.True(IntelVram.Supported);
        Assert.Equal(new[] { 1, 2, 4, 8 }, IntelVram.Sizes);
        Assert.Equal(3, IntelVram.Index);

        IntelVram.SetIndex(2);
        Assert.Equal(2, platform.GpuProvider.LastIntelIndex);
    }

    [Fact]
    public void UnsupportedPlatformReportsNothing()
    {
        var platform = HwTestPlatform.Ensure();
        platform.GpuProvider.IntelVramAvailable = false;
        platform.GpuProvider.LastIntelIndex = null;

        Assert.False(IntelVram.Init());
        Assert.False(IntelVram.Supported);
        Assert.Empty(IntelVram.Sizes);

        IntelVram.SetIndex(1);
        Assert.Null(platform.GpuProvider.LastIntelIndex);
    }
}
