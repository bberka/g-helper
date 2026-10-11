using GHelper.Gpu;
using GHelper.Gpu.NVidia;
using GHelper.Platform;
using GHelper.Platform.Linux;
using GHelper.Tests.Hw;
using GHelper.Tests.Linux;
using PawnIO;
using Xunit;

namespace GHelper.Tests.Seams;

/// <summary>Behaviour of the seams once a platform supplies them (the platform object is faked, Core's code is real).</summary>
[Collection("Hw platform")]
public class CoreCallSiteWiringTests : IDisposable
{
    public CoreCallSiteWiringTests()
    {
        HwTestPlatform.Ensure();
        NvmlHelper.SetLibraryCandidates(["ghelper-test-no-such-nvml-library"]);
    }

    public void Dispose()
    {
        var provider = HwTestPlatform.Instance.GpuProvider;
        provider.OverclockAvailable = false;
        provider.OverclockCore = provider.OverclockMemory = 0;
        provider.ClockLock = -1;
        provider.OffsetsSet.Clear();
        NvmlHelper.SetLibraryCandidates(null);
    }

    [Fact]
    public void ClockOffsetsGoThroughThePlatformBackendBeforeNvml()
    {
        var provider = HwTestPlatform.Instance.GpuProvider;
        provider.OverclockAvailable = true;
        provider.OverclockCore = 50;
        provider.OverclockMemory = 100;
        using var gpu = new NvidiaGpuControl();

        Assert.True(gpu.GetClocks(out int core, out int memory));
        Assert.Equal((50, 100), (core, memory));

        Assert.Equal(1, gpu.SetClocks(120, 300));
        Assert.Equal([(120, 300)], provider.OffsetsSet);
    }

    [Fact]
    public void ClockOffsetsFailCleanlyWithoutBackendAndWithoutNvml()
    {
        var provider = HwTestPlatform.Instance.GpuProvider;
        using var gpu = new NvidiaGpuControl();

        Assert.False(gpu.GetClocks(out _, out _));
        Assert.Equal(-1, gpu.SetClocks(120, 300)); // backend refuses (-1), NVML is not there
    }

    [Fact]
    public void ClockLockIsReadFromThePlatformBackend()
    {
        var provider = HwTestPlatform.Instance.GpuProvider;
        using var gpu = new NvidiaGpuControl();

        provider.ClockLock = 1500;
        Assert.Equal(1500, gpu.GetMaxGPUCLock());
        Assert.Equal(0, gpu.SetMaxGPUClock(1500));   // already locked at that value

        // NVAPI reports 0 for "no lock": a reset has nothing to do
        provider.ClockLock = 0;
        Assert.Equal(-1, gpu.GetMaxGPUCLock());
        Assert.Equal(0, gpu.SetMaxGPUClock(0));
    }

    [Fact]
    public void PawnCpuTuningRunsCoresSmuServiceOverThePlatformDriver()
    {
        FakePawnIo? last = null;
        var tuning = new PawnCpuTuning(() => last = new FakePawnIo { CodeName = (ulong)CpuCodeName.Phoenix },
            platformSupported: true, isAmd: true, isIntel: false);

        Assert.True(tuning.IsSupported);
        Assert.True(tuning.IsDriverInstalled);
        Assert.Null(((ICpuFamilyInfo)tuning).CpuFamily);   // not initialised: no driver access just to answer

        Assert.True(tuning.Initialize());
        Assert.Equal("Mobile", ((ICpuFamilyInfo)tuning).CpuFamily);
        Assert.Equal("Phoenix", ((ICpuFamilyInfo)tuning).CpuCodeName);
        Assert.True(tuning.CanSetTDP());
        Assert.True(tuning.SetAllLimits(35, 45, 40));
        Assert.Equal(GHelper.Mode.ModeControl.SmuOk, tuning.SetCoAll(-20));
        Assert.Contains((0x03B10998u, 0x100000u - 20u), last!.Writes);

        tuning.Dispose();
        Assert.True(last.Disposed);
        Assert.False(tuning.Initialize()); // disposed
    }

    [Fact]
    public void PawnCpuTuningReportsDriverAbsenceAndNeverTouchesItWhenUnsupported()
    {
        var absent = new PawnCpuTuning(() => new FakePawnIo { ConnectResult = PawnConnectResult.NotInstalled }, true, isAmd: true);
        Assert.True(absent.IsSupported);
        Assert.False(absent.IsDriverInstalled);
        Assert.False(absent.Initialize());

        // installed but the process is not elevated: still "installed"
        var denied = new PawnCpuTuning(() => new FakePawnIo { ConnectResult = PawnConnectResult.AccessDenied }, true, isAmd: true);
        Assert.True(denied.IsDriverInstalled);
        Assert.False(denied.Initialize());

        int created = 0;
        var foreign = new PawnCpuTuning(() => { created++; return new FakePawnIo(); }, platformSupported: false, isAmd: true);
        Assert.False(foreign.IsSupported);
        Assert.False(foreign.IsDriverInstalled);
        Assert.False(foreign.Initialize());
        Assert.Null(foreign.CreatePawnIo());
        Assert.Equal(0, created);
    }

    [Fact]
    public void PawnCpuTuningReadsIntelPackagePowerThroughTheMsrModule()
    {
        var io = new FakePawnIo { Msr606 = 0x000A0E03UL, Msr611 = 1000 };
        var tuning = new PawnCpuTuning(() => io, true, isAmd: false, isIntel: true);

        Assert.True(tuning.Initialize());
        Assert.Null(tuning.GetIntelPackagePower()); // first sample only primes the counter
        Assert.Contains("ioctl_read_msr", io.Calls);
    }

    [Fact]
    public void AmdDriverFeedsCoresAmdGpuControlFromSysfs()
    {
        var (fs, pci) = SeamFixtures.AmdFake();
        using var _ = fs;
        var driver = new SysfsAmdGpuDriver(SeamFixtures.Context(fs), [pci]);
        using var gpu = new GHelper.Gpu.AMD.AmdGpuControl(driver);

        Assert.True(driver.HasDiscrete);
        Assert.False(driver.HasIntegrated);
        Assert.True(gpu.IsValid);
        Assert.False(gpu.IsNvidia);
        Assert.Equal(55, gpu.GetCurrentTemperature());
        Assert.Equal(37, gpu.GetGpuUse());
        Assert.Equal((2048L, 8192L), gpu.GetVramInfo());
        Assert.Equal(42f, gpu.GetGpuPower());
    }

}

internal static class SeamFixtures
{
    public static LinuxContext Context(FakeSysfs fs, FakeRunner? runner = null, Func<string, string?>? env = null) =>
        new(fs.Root, runner ?? new FakeRunner(), env ?? (n => n == "HOME" ? "/home/test" : null), () => 1000, "/usr/bin/ghelper");

    public static (FakeSysfs fs, PciGpu gpu) AmdFake()
    {
        var fs = new FakeSysfs();
        const string dev = "/sys/bus/pci/devices/0000:03:00.0";
        fs.File(dev + "/class", "0x030000").File(dev + "/vendor", "0x1002").File(dev + "/device", "0x73ef");
        fs.File(dev + "/boot_vga", "0").File(dev + "/power/runtime_status", "active");
        fs.File(dev + "/mem_info_vram_total", (8L * 1024 * 1024 * 1024).ToString());
        fs.File(dev + "/mem_info_vram_used", (2L * 1024 * 1024 * 1024).ToString());
        fs.File(dev + "/gpu_busy_percent", "37");
        fs.File(dev + "/hwmon/hwmon7/temp1_input", "55000");
        fs.File(dev + "/hwmon/hwmon7/power1_average", "42000000");
        fs.File("/sys/class/drm/card0-eDP-1/amdgpu/panel_power_savings", "3\n");

        var pci = new PciGpu("0000:03:00.0", dev, GpuVendor.Amd, 0x73ef, "amdgpu", false)
        {
            RuntimeStatus = "active",
        };
        return (fs, pci);
    }
}

[Collection("Hw platform")]
public class LinuxSeamTests
{
    public LinuxSeamTests() => HwTestPlatform.Ensure();

    private static LinuxContext Context(FakeSysfs fs, FakeRunner? runner = null, Func<string, string?>? env = null) =>
        SeamFixtures.Context(fs, runner, env);

    [Fact]
    public void DisplaySettingsChangedFiresWhenAConnectorAppears()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/class/drm/card0-eDP-1/status", "connected\n");
        fs.File("/sys/class/drm/card0-HDMI-A-1/status", "disconnected\n");

        using var display = new LinuxDisplayService(Context(fs)) { DisplayPollMs = 0 };
        int raised = 0;
        void Handler() => raised++;

        display.DisplaySettingsChanged += Handler;
        display.PollDisplays();
        Assert.Equal(0, raised);

        fs.File("/sys/class/drm/card0-HDMI-A-1/status", "connected\n");
        display.PollDisplays();
        Assert.Equal(1, raised);

        display.PollDisplays();   // nothing new
        Assert.Equal(1, raised);

        display.DisplaySettingsChanged -= Handler;
    }

    [Fact]
    public void ScreenCaptureIsRefusedOnWaylandAndWithoutADisplay()
    {
        using var fs = new FakeSysfs();

        using var noDisplay = new LinuxDisplayService(Context(fs));
        Assert.False(((GHelper.USB.IScreenCapture)noDisplay).IsSupported);

        using var wayland = new LinuxDisplayService(Context(fs, env: n => n switch
        {
            "DISPLAY" => ":0",
            "WAYLAND_DISPLAY" => "wayland-0",
            _ => null,
        }));
        Assert.False(((GHelper.USB.IScreenCapture)wayland).IsSupported);
        Assert.Null(((GHelper.USB.IScreenCapture)wayland).Capture(new System.Drawing.Rectangle(0, 0, 10, 10), 4, 2));
    }

    [Fact]
    public void X11DownsampleIsNearestNeighbourAndFlipsChannelOrder()
    {
        // 2x2 BGRX image: red, green / blue, white
        byte[] bgrx =
        [
            0, 0, 255, 0,     0, 255, 0, 0,
            255, 0, 0, 0,     255, 255, 255, 0,
        ];

        var buffer = X11ScreenGrab.Downsample(bgrx, stride: 8, width: 2, height: 2, outWidth: 2, outHeight: 2);

        Assert.Equal(System.Drawing.Color.FromArgb(255, 255, 0, 0), buffer.GetPixel(0, 0));
        Assert.Equal(System.Drawing.Color.FromArgb(255, 0, 255, 0), buffer.GetPixel(1, 0));
        Assert.Equal(System.Drawing.Color.FromArgb(255, 0, 0, 255), buffer.GetPixel(0, 1));

        var one = X11ScreenGrab.Downsample(bgrx, 8, 2, 2, 1, 1);
        Assert.Equal(System.Drawing.Color.FromArgb(255, 255, 0, 0), one.GetPixel(0, 0));
    }

    [Fact]
    public void IdleTimeComesFromTheGnomeIdleMonitor()
    {
        using var fs = new FakeSysfs();
        var runner = new FakeRunner().Has("gdbus");
        const string call = "gdbus call --session --dest org.gnome.Mutter.IdleMonitor --object-path /org/gnome/Mutter/IdleMonitor/Core --method org.gnome.Mutter.IdleMonitor.GetIdletime";
        runner.Reply(call, "(uint64 4200,)\n");

        using var input = new LinuxInputService(Context(fs, runner, n => n == "XDG_CURRENT_DESKTOP" ? "GNOME" : null));
        TimeSpan idle = ((GHelper.Input.IInputExtras)input).GetIdleTime();

        Assert.InRange(idle.TotalMilliseconds, 4200, 6000);
    }

    [Fact]
    public void TouchpadStateComesFromGsettings()
    {
        using var fs = new FakeSysfs();
        var runner = new FakeRunner().Has("gsettings");
        runner.Reply("gsettings get org.gnome.desktop.peripherals.touchpad send-events", "'disabled'\n");

        using var input = new LinuxInputService(Context(fs, runner, n => n == "XDG_CURRENT_DESKTOP" ? "GNOME" : null));
        Assert.False(((GHelper.Input.IInputExtras)input).GetTouchpadEnabled());

        runner.Reply("gsettings get org.gnome.desktop.peripherals.touchpad send-events", "'enabled'\n");
        Assert.True(((GHelper.Input.IInputExtras)input).GetTouchpadEnabled());
    }

    [Fact]
    public void SessionEndingIsRaisedOnLogindPrepareForShutdown()
    {
        using var fs = new FakeSysfs();
        using var power = new LinuxPowerService(Context(fs), pollIntervalMs: 0);
        int ended = 0;
        ((ISessionEndingEvents)power).SessionEnding += () => ended++;

        power.OnLogindLine("/org/freedesktop/login1: org.freedesktop.login1.Manager.PrepareForShutdown (true,)");
        power.OnLogindLine("/org/freedesktop/login1: org.freedesktop.login1.Manager.PrepareForSleep (true,)");

        Assert.Equal(1, ended);
        Assert.Contains(LinuxPowerService.Performance, ((IPowerModeCatalog)power).PowerModes.Keys);
    }

    private static (FakeSysfs fs, PciGpu gpu) AmdFake() => SeamFixtures.AmdFake();

    [Fact]
    public void AmdDriverNeverReadsASuspendedGpu()
    {
        var (fs, pci) = AmdFake();
        using var _ = fs;
        var driver = new SysfsAmdGpuDriver(Context(fs), [pci]);

        fs.File("/sys/bus/pci/devices/0000:03:00.0/power/runtime_status", "suspended");

        Assert.Null(driver.QuerySensors(false));
        Assert.Null(driver.GetUsedVramMb(false));
        Assert.Equal(8192L, driver.GetTotalVramMb(false)); // static info, no wake up needed
    }

    [Fact]
    public void PanelPowerSavingsIsTheOledPowerOptimisation()
    {
        var (fs, pci) = AmdFake();
        using var _ = fs;
        var driver = new SysfsAmdGpuDriver(Context(fs), [pci]);

        Assert.Equal(0, driver.FindSceDisplay());
        Assert.Equal(4, driver.GetSceState(0));       // level 3 = state 4 (> 1 means active)
        Assert.True(driver.SetSceState(0, 1));        // Core's "disable"
        Assert.Equal("0", fs.Read("/sys/class/drm/card0-eDP-1/amdgpu/panel_power_savings"));
        Assert.Equal(1, driver.GetSceState(0));

        fs.Dir("/sys/class/drm/card0-eDP-1/amdgpu"); // keep the tree
        File.Delete(fs.Physical("/sys/class/drm/card0-eDP-1/amdgpu/panel_power_savings"));
        Assert.Equal(-1, driver.FindSceDisplay());    // no ABM knob: unsupported
    }

    [Fact]
    public void DiscretePowerStateReadsRuntimePm()
    {
        using var fs = new FakeSysfs();
        const string dev = "/sys/bus/pci/devices/0000:01:00.0";
        fs.File(dev + "/class", "0x030200").File(dev + "/vendor", "0x10de").File(dev + "/device", "0x2860");
        fs.File(dev + "/power/runtime_status", "suspended");

        var provider = new LinuxGpuProvider(Context(fs));
        Assert.Equal(GpuPowerState.Asleep, provider.GetDiscretePowerState());

        fs.File(dev + "/power/runtime_status", "active");
        Assert.Equal(GpuPowerState.Active, provider.GetDiscretePowerState());

        using var empty = new FakeSysfs();
        Assert.Equal(GpuPowerState.Unknown, new LinuxGpuProvider(Context(empty)).GetDiscretePowerState());
    }
}
