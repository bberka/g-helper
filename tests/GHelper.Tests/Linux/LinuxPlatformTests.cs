using GHelper.Platform;
using GHelper.Platform.Linux;
using Xunit;

namespace GHelper.Tests.Linux;

public class LinuxPlatformTests
{
    [Fact]
    public void DefaultConstructionNeverThrows()
    {
        // runs on every OS: on Windows "/" simply holds none of the Linux files
        var platform = new LinuxPlatform();
        Assert.Equal(PlatformKind.Linux, platform.Kind);
        Assert.NotNull(platform.Fps);
        platform.Dispose();
    }

    [Fact]
    public void EveryServiceIsAvailableAndSafeOnAMachineWithoutAsusHardware()
    {
        using var fs = new FakeSysfs();
        using var platform = new LinuxPlatform(new LinuxContext(fs.Root, new FakeRunner(),
            getEnv: n => n == "HOME" ? "/home/test" : null, getEuid: () => 1000, processPath: "/usr/bin/ghelper"));

        // ISystemInfo
        Assert.Equal(PlatformKind.Linux, platform.System.Kind);
        Assert.Equal("", platform.System.GetModel());
        Assert.Equal(("", ""), platform.System.GetBiosAndModel());
        Assert.Equal("", platform.System.GetSerialNumber());
        Assert.Null(platform.System.GetCpuName());
        Assert.False(platform.System.IsElevated);
        Assert.False(platform.System.IsRunningAsSystem);
        Assert.False(platform.System.IsDesktop);
        Assert.Null(platform.System.IsOledPanel());
        Assert.False(string.IsNullOrEmpty(platform.System.ConfigDirectory));
        Assert.False(string.IsNullOrEmpty(platform.System.OsDescription));

        // IAsusBackend
        Assert.False(platform.Asus.IsConnected);
        Assert.True(platform.Asus.DeviceGet(AsusACPI.PerformanceMode) < 0);
        Assert.Equal(16, platform.Asus.DeviceInit().Length);
        platform.Asus.StartEventListener();

        // IPowerService
        Assert.Equal(PowerSource.AC, platform.Power.GetPowerSource()); // no battery: desktop like
        Assert.False(platform.Power.IsLidClosed());
        Assert.Equal(-1, platform.Power.GetCPUBoost());
        Assert.Equal(-1, platform.Power.GetASPM());
        Assert.False(platform.Power.GetBatterySaverStatus());
        platform.Power.SetCPUBoost(0);
        platform.Power.SetASPM(0);
        platform.Power.SetLidAction(0);
        platform.Power.SetConnectivityInStandby();

        // IDisplayService
        Assert.Null(platform.Display.FindLaptopScreen());
        Assert.False(platform.Display.IsExternalDisplayConnected());
        Assert.Equal(-1, platform.Display.GetBrightness());
        Assert.Equal(-1, platform.Display.GetRefreshRate(null));
        Assert.Equal(-1, platform.Display.GetMaxRefreshRate(null));
        Assert.False(platform.Display.GetHdrStatus(out _));
        Assert.False(platform.Display.SupportsGammaRamp);
        Assert.Null(platform.Display.GetTouchscreenStatus());
        platform.Display.SetBrightness(50);

        // IStartupService / IProcessService
        Assert.True(platform.Startup.IsSupported);
        Assert.False(platform.Startup.IsScheduled());
        platform.Startup.StartupCheck();
        platform.Startup.ReScheduleAdmin();
        Assert.Equal("", platform.Process.RunCommand("nonexistent-binary-xyz", ""));

        // IAudioService
        Assert.False(platform.Audio.IsSupported);
        Assert.False(platform.Audio.IsMicMuted());
        Assert.Null(platform.Audio.StartLoopbackCapture((_, _) => { }));

        // IVendorServices
        Assert.False(platform.Vendor.IsAsusOptimizationRunning());
        Assert.False(platform.Vendor.IsArmouryRunning());
        Assert.Equal(0, platform.Vendor.GetRunningCount());
        Assert.False(platform.Vendor.IsDynamicLightingSupported);
        Assert.Null(platform.Vendor.GetGpuError());

        // ICpuTuning
        Assert.False(platform.CpuTuning.IsSupported);
        Assert.False(platform.CpuTuning.CanSetTDP());
        Assert.False(platform.CpuTuning.SetAllLimits(30, 40, 50));
        Assert.Null(platform.CpuTuning.GetPowerLimits());
        Assert.Null(platform.CpuTuning.GetIntelPackagePower());

        // IGpuProvider
        Assert.Null(platform.Gpu.CreateDiscrete());
        Assert.Null(platform.Gpu.CreateAmdApu());
        Assert.False(platform.Gpu.InitIntelVram());
        platform.Gpu.UnloadNative();

        // ISystemSensors
        Assert.Null(platform.Sensors.GetCpuTemp());
        Assert.Null(platform.Sensors.GetCpuPower());
        Assert.Null(platform.Sensors.GetRamInfo());
        Assert.Null(platform.Sensors.GetBatteryState());
        Assert.Null(platform.Sensors.GetFanRpm(0));

        // IFpsMonitorFactory
        Assert.False(platform.Fps.IsSupported);
        Assert.Null(platform.Fps.Create());
    }

    [Fact]
    public void SystemInfoReadsDmiAndOsRelease()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/class/dmi/id/product_name", "ROG Zephyrus G14 GA402RJ_GA402RJ\n");
        fs.File("/sys/class/dmi/id/bios_version", "GA402RJ.320");
        fs.File("/sys/class/dmi/id/product_serial", "ABC123");
        fs.File("/sys/class/dmi/id/chassis_type", "10");
        fs.File("/etc/os-release", "NAME=\"Fedora Linux\"\nPRETTY_NAME=\"Fedora Linux 42 (Workstation Edition)\"\n");
        fs.File("/proc/sys/kernel/osrelease", "6.19.2-200.fc42.x86_64");
        fs.File("/proc/cpuinfo", "processor\t: 0\nvendor_id\t: AuthenticAMD\ncpu family\t: 25\nmodel\t\t: 68\nmodel name\t: AMD Ryzen 9 6900HS\n\nprocessor\t: 1\nmodel name\t: other\n");
        fs.File("/home/u/.config/kdeglobals", "[General]\nColorScheme=BreezeDark\n");

        var ctx = new LinuxContext(fs.Root, new FakeRunner(), getEnv: n => n == "HOME" ? "/home/u" : null, getEuid: () => 1000);
        var info = new LinuxSystemInfo(ctx);

        Assert.Equal("ROG Zephyrus G14 GA402RJ_GA402RJ", info.GetModel());
        Assert.Equal(("320", "GA402RJ"), info.GetBiosAndModel());
        Assert.Equal("ABC123", info.GetSerialNumber());
        Assert.Equal("AMD Ryzen 9 6900HS", info.GetCpuName());
        Assert.False(info.IsDesktop);
        Assert.Contains("Fedora Linux 42", info.OsDescription);
        Assert.Contains("6.19.2", info.OsDescription);
        Assert.True(info.IsDarkTheme());
        Assert.EndsWith(Path.Combine(".config", "ghelper"), info.ConfigDirectory.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(info.ConfigDirectory));
    }

    [Theory]
    [InlineData("3", true)]
    [InlineData("13", true)]
    [InlineData("9", false)]
    [InlineData("10", false)]
    [InlineData("31", false)]
    public void ChassisTypeDecidesDesktop(string chassis, bool desktop)
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/class/dmi/id/chassis_type", chassis);
        var info = new LinuxSystemInfo(new LinuxContext(fs.Root, new FakeRunner()));
        Assert.Equal(desktop, info.IsDesktop);
    }

    [Fact]
    public void ElevatedProcessUsesTheInvokingUsersConfigDirectory()
    {
        using var fs = new FakeSysfs();
        fs.File("/etc/passwd", "root:x:0:0:root:/root:/bin/bash\nberg:x:1000:1000:Berg:/home/berg:/bin/zsh\n");
        var env = new Dictionary<string, string> { ["PKEXEC_UID"] = "1000", ["HOME"] = "/root" };
        var ctx = new LinuxContext(fs.Root, new FakeRunner(), getEnv: n => env.GetValueOrDefault(n), getEuid: () => 0);

        Assert.True(ctx.IsRoot);
        Assert.Equal(1000u, ctx.InvokingUid);
        Assert.Equal("/home/berg", ctx.HomeDirectory);
        Assert.Equal("/home/berg/.config", ctx.XdgConfigHome);
    }
}
