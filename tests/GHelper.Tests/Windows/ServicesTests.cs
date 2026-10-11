using System.Drawing;
using GHelper.Platform;
using GHelper.Platform.Windows;
using GHelper.Platform.Windows.Asus;
using GHelper.Platform.Windows.Audio;
using GHelper.Platform.Windows.Fps;
using GHelper.Platform.Windows.Gpu;
using GHelper.Platform.Windows.Pawn;
using GHelper.Platform.Windows.Power;
using GHelper.Platform.Windows.Processes;
using GHelper.Platform.Windows.Sensors;
using GHelper.Platform.Windows.Startup;
using GHelper.Platform.Windows.Vendor;
using Xunit;

namespace GHelper.Tests.Windows;

public class WindowsPlatformTests
{
    [Fact]
    public void ConstructsOnAnyOsWithoutTouchingTheSystem()
    {
        var platform = new WindowsPlatform();

        Assert.Equal(PlatformKind.Windows, platform.Kind);
        platform.Dispose(); // nothing was created: must be a no-op
    }

    [Fact]
    public void ServicesAreSingletonsOfTheExpectedType()
    {
        using var platform = new WindowsPlatform();

        Assert.IsType<WindowsSystemInfo>(platform.System);
        Assert.Same(platform.System, platform.System);
        Assert.IsType<WindowsAsusBackend>(platform.Asus);
        Assert.IsType<WindowsPowerService>(platform.Power);
        Assert.IsType<WindowsCpuTuning>(platform.CpuTuning);
        Assert.IsType<WindowsGpuProvider>(platform.Gpu);
        Assert.IsType<WindowsSystemSensors>(platform.Sensors);
        Assert.IsType<WindowsFpsMonitorFactory>(platform.Fps);
        Assert.IsType<WindowsAudioService>(platform.Audio);
        Assert.Same(platform.Process, platform.Process);
    }

    [Fact]
    public void RegistersAsThePlatform()
    {
        using var platform = new WindowsPlatform();
        IPlatform asContract = platform;

        Assert.NotNull(asContract.Display);
        Assert.NotNull(asContract.Input);
        Assert.NotNull(asContract.Startup);
        Assert.NotNull(asContract.Vendor);
    }
}

public class SystemInfoLogicTests
{
    [Theory]
    [InlineData("GA402RJ.320", "320", "GA402RJ")]
    [InlineData("GU605MY.311", "311", "GU605MY")]
    [InlineData("FA507NV", "", "FA507NV")]
    [InlineData("", "", "")]
    public void BiosVersionSplit(string raw, string bios, string model)
    {
        var result = WindowsSystemInfo.ParseBiosVersion(raw);
        Assert.Equal(bios, result.Bios);
        Assert.Equal(model, result.ModelShort);
    }

    [Fact]
    public void ConfigDirectoriesAreNamedGHelper()
    {
        var info = new WindowsSystemInfo();

        Assert.Equal("GHelper", Path.GetFileName(info.ConfigDirectory));
        Assert.Equal("GHelper", Path.GetFileName(info.SharedConfigDirectory));
        Assert.NotEqual(info.ConfigDirectory, info.SharedConfigDirectory);
        Assert.Equal(PlatformKind.Windows, info.Kind);
    }
}

public class StartupLogicTests
{
    [Theory]
    [InlineData("1.2.3.4", 1, 2, 3, 4)]
    [InlineData("0.286", 0, 286, 0, 0)]
    [InlineData("0.286.0.0", 0, 286, 0, 0)]
    [InlineData("5", 5, 0, 0, 0)]
    public void FileVersionParsing(string text, int major, int minor, int build, int revision)
    {
        Assert.True(WindowsStartupService.TryParseFileVersion(text, out var version));
        Assert.Equal(new Version(major, minor, build, revision), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.x")]
    public void BadFileVersionIsRejected(string? text)
    {
        Assert.False(WindowsStartupService.TryParseFileVersion(text, out _));
    }
}

public class VendorLogicTests
{
    [Fact]
    public void ServiceListContainsTheOptimizationServiceAndArmouryOnRequest()
    {
        var basic = WindowsVendorServices.ServiceNames(false).ToList();
        var all = WindowsVendorServices.ServiceNames(true).ToList();

        Assert.Contains("ASUSOptimization", basic);
        Assert.DoesNotContain("ArmouryCrateService", basic);
        Assert.Contains("ArmouryCrateService", all);
        Assert.Contains("LightingService", all);
        Assert.True(all.Count > basic.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void ArmouryServicesAreOnlyDisabledToManual()
    {
        Assert.True(WindowsVendorServices.IsAcService("ArmouryCrateSEService"));
        Assert.False(WindowsVendorServices.IsAcService("ASUSOptimization"));
    }

    [Fact]
    public void DynamicLightingColorSwapsRedAndBlue()
    {
        // AABBGGRR
        Assert.Equal(unchecked((int)0xFF0000FF), WindowsVendorServices.ToDynamicLightingColor(Color.FromArgb(255, 0xFF, 0x00, 0x00)));
        Assert.Equal(unchecked((int)0xFFFF0000), WindowsVendorServices.ToDynamicLightingColor(Color.FromArgb(255, 0x00, 0x00, 0xFF)));
        Assert.Equal(unchecked((int)0x80332211), WindowsVendorServices.ToDynamicLightingColor(Color.FromArgb(0x80, 0x11, 0x22, 0x33)));
    }

    [Theory]
    [InlineData(22u, "Device is disabled")]
    [InlineData(28u, "Drivers are not installed")]
    [InlineData(31u, "Driver failed to load")]
    [InlineData(39u, "Driver failed to load")]
    [InlineData(999u, "Unknown problem")]
    public void ProblemTexts(uint problem, string text)
    {
        Assert.Equal(text, DeviceProblems.ProblemText(problem));
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_2820&SUBSYS_1", true)]
    [InlineData(@"pci\ven_1002&dev_1681", true)]
    [InlineData(@"PCI\VEN_8086&DEV_46A6", false)]
    [InlineData(@"ROOT\BASICDISPLAY\0000", false)]
    public void OnlyNvidiaAndAmdAdaptersAreChecked(string id, bool expected)
    {
        Assert.Equal(expected, DeviceProblems.IsGpuDeviceId(id));
    }
}

public class ProcessLogicTests
{
    [Fact]
    public void SingleInstanceEventIsGlobal()
    {
        Assert.Equal(@"Global\GHelperApp-main", WindowsProcessService.EventName("main"));
    }

    [Fact]
    public void RestrictedCommandLineQuotesTheShell()
    {
        Assert.Equal("\"C:\\Windows\\System32\\cmd.exe\" /C start notepad",
            RestrictedProcess.BuildCommandLine(@"C:\Windows\System32\cmd.exe", "start notepad"));
    }
}

public class SensorLogicTests
{
    [Theory]
    [InlineData(0, 0, 0)]       // no delta: idle
    [InlineData(100, 100, 0)]   // everything idle
    [InlineData(0, 100, 100)]   // nothing idle
    [InlineData(25, 100, 75)]
    [InlineData(-5, 100, 100)]  // clamped
    public void CpuUsagePercent(long deltaIdle, long deltaTotal, float expected)
    {
        Assert.Equal(expected, WindowsSystemSensors.UsagePercent(deltaIdle, deltaTotal));
    }

    [Fact]
    public void BatteryPercentPrefersCapacityRatio()
    {
        Assert.Equal(50, BatteryMath.Percent(30, 60, 77), 0.001);
        Assert.Equal(100, BatteryMath.Percent(70, 60, 77), 0.001); // over full: clamp
    }

    [Fact]
    public void BatteryPercentFallsBackToOsValue()
    {
        Assert.Equal(77, BatteryMath.Percent(null, 60, 77));
        Assert.Equal(77, BatteryMath.Percent(0, 0, 77));
        Assert.Equal(0, BatteryMath.Percent(null, null, 255)); // unknown
    }

    [Fact]
    public void EnergyMeterDeltaToWatts()
    {
        // 1e9 picowatt-hours in 1000 ms: 1e9 * 3.6e-6 / 1000 = 3.6
        Assert.Equal(3.6f, BatteryMath.EnergyChannelWatts(1_000_000_000, 1000), 0.001f);
        Assert.Equal(0f, BatteryMath.EnergyChannelWatts(1_000_000, 0));
    }
}

public class AudioLogicTests
{
    [Fact]
    public void Int16BufferIsScaledAndDownmixedToFirstChannel()
    {
        // two frames of stereo 16 bit: (L=16384, R=-1) (L=-32768, R=5)
        byte[] buffer = new byte[8];
        BitConverter.GetBytes((short)16384).CopyTo(buffer, 0);
        BitConverter.GetBytes((short)-1).CopyTo(buffer, 2);
        BitConverter.GetBytes((short)-32768).CopyTo(buffer, 4);
        BitConverter.GetBytes((short)5).CopyTo(buffer, 6);

        float[] samples = LoopbackCaptureSession.ConvertBuffer(buffer, buffer.Length, 16, 2, isFloat: false);

        Assert.Equal(2, samples.Length);
        Assert.Equal(0.5f, samples[0]);
        Assert.Equal(-1f, samples[1]);
    }

    [Fact]
    public void FloatBufferIsPassedThrough()
    {
        byte[] buffer = new byte[16];
        BitConverter.GetBytes(0.25f).CopyTo(buffer, 0);
        BitConverter.GetBytes(0.75f).CopyTo(buffer, 4);   // right channel ignored
        BitConverter.GetBytes(-0.5f).CopyTo(buffer, 8);
        BitConverter.GetBytes(1f).CopyTo(buffer, 12);

        float[] samples = LoopbackCaptureSession.ConvertBuffer(buffer, 16, 32, 2, isFloat: true);

        Assert.Equal([0.25f, -0.5f], samples);
    }

    [Fact]
    public void PartialFramesAreDropped()
    {
        float[] samples = LoopbackCaptureSession.ConvertBuffer(new byte[7], 7, 16, 2, isFloat: false);
        Assert.Single(samples);
    }
}

public class PawnLogicTests
{
    [Fact]
    public void CpuVendorStringIsDecodedFromCpuidRegisters()
    {
        // "AuthenticAMD" = EBX "Auth", EDX "enti", ECX "cAMD"
        Assert.Equal(CpuVendorKind.Amd, CpuVendor.FromRegisters(0x68747541, 0x69746E65, 0x444D4163));
        // "GenuineIntel" = EBX "Genu", EDX "ineI", ECX "ntel"
        Assert.Equal(CpuVendorKind.Intel, CpuVendor.FromRegisters(0x756E6547, 0x49656E69, 0x6C65746E));
        Assert.Equal(CpuVendorKind.Unknown, CpuVendor.FromRegisters(0, 0, 0));
    }

    [Theory]
    [InlineData(CpuCodeName.Renoir, CpuFamily.Renoir)]
    [InlineData(CpuCodeName.Cezanne, CpuFamily.Renoir)]
    [InlineData(CpuCodeName.Phoenix, CpuFamily.Mobile)]
    [InlineData(CpuCodeName.HawkPoint, CpuFamily.Mobile)]
    [InlineData(CpuCodeName.Raphael, CpuFamily.Raphael)]
    [InlineData(CpuCodeName.DragonRange, CpuFamily.Raphael)]
    [InlineData(CpuCodeName.StrixPoint, CpuFamily.StrixPoint)]
    [InlineData(CpuCodeName.StrixHalo, CpuFamily.StrixHalo)]
    [InlineData(CpuCodeName.Undefined, CpuFamily.Unknown)]
    public void CodeNameToFamily(CpuCodeName name, CpuFamily family)
    {
        Assert.Equal(family, RyzenSmuService.GetFamily(name));
    }

    [Fact]
    public void CpuTuningReportsNothingWithoutDriverOrOnForeignOs()
    {
        using var tuning = new WindowsCpuTuning();

        if (!OperatingSystem.IsWindows())
        {
            Assert.False(tuning.IsSupported);
            Assert.False(tuning.IsDriverInstalled);
            Assert.False(tuning.Initialize());
        }

        // never throws, whatever the host
        Assert.False(tuning.IsAmd && tuning.IsIntel);
    }

    [Fact]
    public void IntelVramSizesArePowersOfTwoUpToHalfTheRam()
    {
        Assert.Equal([1, 2, 4, 8, 16], IntelVram.BuildSizes(32L * 1024 * 1024));  // 32 GB in KB
        Assert.Equal([1, 2, 4, 8], IntelVram.BuildSizes(16L * 1024 * 1024));
        Assert.Equal([1, 2, 4], IntelVram.BuildSizes(0));                          // unknown: 4 GB ceiling
    }

    [Fact]
    public void GpuProviderIsInertOnUnsupportedPlatforms()
    {
        var provider = new WindowsGpuProvider();

        Assert.False(provider.IntelVramSupported);
        Assert.Empty(provider.IntelVramSizes);
        Assert.Equal(0, provider.IntelVramIndex);

        if (!OperatingSystem.IsWindows())
        {
            Assert.False(WindowsGpuProvider.IsNvapiSupported);
            Assert.Null(provider.CreateDiscrete());
            Assert.Null(provider.CreateAmdApu());
            Assert.False(provider.InitIntelVram());
        }
    }
}
