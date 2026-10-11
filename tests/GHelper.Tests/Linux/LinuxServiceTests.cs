using GHelper.Platform;
using GHelper.Platform.Linux;
using Xunit;

namespace GHelper.Tests.Linux;

public class LinuxPowerServiceTests
{
    private static LinuxPowerService Create(FakeSysfs fs, FakeRunner runner, uint euid = 1000)
        => new(new LinuxContext(fs.Root, runner, getEnv: _ => null, getEuid: () => euid), pollIntervalMs: 0);

    [Fact]
    public void PowerProfilesDaemonIsPreferred()
    {
        using var fs = new FakeSysfs();
        var runner = new FakeRunner().Has("powerprofilesctl").Reply("powerprofilesctl get", "performance\n");
        using var power = Create(fs, runner);

        Assert.Equal("performance", power.GetPowerMode());
        power.SetPowerMode(2);
        Assert.Contains("powerprofilesctl set power-saver", runner.Calls);
        power.SetPowerMode("balanced");
        Assert.Contains("powerprofilesctl set balanced", runner.Calls);
    }

    [Fact]
    public void BusctlIsUsedWhenTheCliIsMissing()
    {
        using var fs = new FakeSysfs();
        var runner = new FakeRunner()
            .Reply("busctl --system get-property net.hadess.PowerProfiles /net/hadess/PowerProfiles net.hadess.PowerProfiles ActiveProfile",
                "s \"power-saver\"\n");
        using var power = Create(fs, runner);

        Assert.Equal("power-saver", power.GetPowerMode());
    }

    [Fact]
    public void PlatformProfileIsTheFallbackWithoutDaemon()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/class/platform-profile/platform-profile-0/name", "asus-wmi");
        fs.File("/sys/class/platform-profile/platform-profile-0/profile", "quiet");
        fs.File("/sys/class/platform-profile/platform-profile-0/choices", "quiet balanced performance");
        using var power = Create(fs, new FakeRunner());

        Assert.Equal("power-saver", power.GetPowerMode());
        power.SetPowerMode(1);
        Assert.Equal("performance", fs.Read("/sys/class/platform-profile/platform-profile-0/profile"));
        power.SetPowerMode("power-saver");
        Assert.Equal("quiet", fs.Read("/sys/class/platform-profile/platform-profile-0/profile"));
    }

    [Theory]
    [InlineData(0, "balanced")]
    [InlineData(1, "performance")]
    [InlineData(2, "power-saver")]
    public void AsusModesMapToProfiles(int mode, string profile)
    {
        using var fs = new FakeSysfs();
        using var power = Create(fs, new FakeRunner());
        Assert.Equal(profile, power.GetDefaultPowerMode(mode));
    }

    [Fact]
    public void CpuBoostUsesIntelPstateOrCpufreq()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/devices/system/cpu/intel_pstate/no_turbo", "0");
        using var power = Create(fs, new FakeRunner());

        Assert.Equal(1, power.GetCPUBoost());
        power.SetCPUBoost(0);
        Assert.Equal("1", fs.Read("/sys/devices/system/cpu/intel_pstate/no_turbo"));
        Assert.Equal(0, power.GetCPUBoost());
        power.SetCPUBoost(2);
        Assert.Equal("0", fs.Read("/sys/devices/system/cpu/intel_pstate/no_turbo"));
    }

    [Fact]
    public void CpufreqBoostFile()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/devices/system/cpu/cpufreq/boost", "1");
        using var power = Create(fs, new FakeRunner());

        Assert.Equal(1, power.GetCPUBoost());
        power.SetCPUBoost(0);
        Assert.Equal("0", fs.Read("/sys/devices/system/cpu/cpufreq/boost"));
    }

    [Fact]
    public void AspmPolicyIsTranslated()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/module/pcie_aspm/parameters/policy", "default performance [powersave] powersupersave");
        using var power = Create(fs, new FakeRunner());

        Assert.Equal(2, power.GetASPM());
        power.SetASPM(0);
        Assert.Equal("performance", fs.Read("/sys/module/pcie_aspm/parameters/policy"));
    }

    [Theory]
    [InlineData("ignore", 0)]
    [InlineData("suspend", 1)]
    [InlineData("hibernate", 2)]
    [InlineData("poweroff", 3)]
    [InlineData(null, 1)]
    public void LogindLidActionsMapToWindowsNumbers(string? logind, int windows)
    {
        Assert.Equal(windows, LinuxPowerService.LidActionFromLogind(logind));
    }

    [Fact]
    public void LidActionIsReadFromLogindConfWithDropIns()
    {
        using var fs = new FakeSysfs();
        fs.File("/etc/systemd/logind.conf", "[Login]\n#HandleLidSwitch=suspend\nHandleLidSwitch=hibernate\n");
        fs.File("/etc/systemd/logind.conf.d/10-ext.conf", "[Login]\nHandleLidSwitchExternalPower=ignore\n");
        using var power = Create(fs, new FakeRunner());

        Assert.Equal(2, power.GetLidAction(ac: false));
        Assert.Equal(0, power.GetLidAction(ac: true));
    }

    [Fact]
    public void LidActionWriteNeedsRootAndCreatesDropIn()
    {
        using var fs = new FakeSysfs();
        var runner = new FakeRunner().Has("systemctl");

        using (var user = Create(fs, runner, 1000))
        {
            user.SetLidAction(0);
            Assert.False(fs.Exists("/etc/systemd/logind.conf.d/50-ghelper-lid.conf"));
        }

        using var root = Create(fs, runner, 0);
        root.SetLidAction(0, acOnly: true);
        string text = fs.Read("/etc/systemd/logind.conf.d/50-ghelper-lid.conf");
        Assert.Contains("HandleLidSwitchExternalPower=ignore", text);
        Assert.DoesNotContain("HandleLidSwitch=", text.Replace("HandleLidSwitchExternalPower=", ""));
        Assert.Contains("systemctl kill -s HUP systemd-logind.service", runner.Calls);
    }

    [Fact]
    public void HibernateDelayRoundTrip()
    {
        using var fs = new FakeSysfs();
        using var power = Create(fs, new FakeRunner(), 0);

        Assert.Equal(120, power.GetHibernateAfter());
        power.SetHibernateAfter(45);
        Assert.Equal(45, power.GetHibernateAfter());
        power.SetHibernateAfter(0);
        Assert.False(fs.Exists("/etc/systemd/sleep.conf.d/50-ghelper-hibernate.conf"));
    }

    [Fact]
    public void PowerSourceAndLidAreReadFromSysfs()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/class/power_supply/ACAD/type", "Mains");
        fs.File("/sys/class/power_supply/ACAD/online", "1");
        fs.File("/sys/class/power_supply/BAT0/type", "Battery");
        fs.File("/proc/acpi/button/lid/LID0/state", "state:      closed\n");
        using var power = Create(fs, new FakeRunner());

        Assert.Equal(PowerSource.AC, power.GetPowerSource());
        Assert.True(power.IsLidClosed());

        fs.File("/sys/class/power_supply/ACAD/online", "0");
        fs.File("/proc/acpi/button/lid/LID0/state", "state:      open\n");
        Assert.Equal(PowerSource.Battery, power.GetPowerSource());
        Assert.False(power.IsLidClosed());
    }

    [Fact]
    public void LogindSignalsRaiseEvents()
    {
        using var fs = new FakeSysfs();
        var runner = new FakeRunner().Has("gdbus");
        using var power = Create(fs, runner);

        var log = new List<string>();
        power.Suspending += () => log.Add("suspend");
        power.Resumed += () => log.Add("resume");
        power.SessionLockChanged += l => log.Add(l ? "lock" : "unlock");
        power.LidChanged += c => log.Add(c ? "lid-closed" : "lid-open");

        // the first subscription started the gdbus monitors
        Assert.Contains(runner.LineStreams, s => s.file == "gdbus" && s.args.Contains("org.freedesktop.login1"));

        power.OnLogindLine("/org/freedesktop/login1: org.freedesktop.login1.Manager.PrepareForSleep (true,)");
        power.OnLogindLine("/org/freedesktop/login1: org.freedesktop.login1.Manager.PrepareForSleep (false,)");
        power.OnLogindLine("/org/freedesktop/login1/session/_32: org.freedesktop.login1.Session.Lock ()");
        power.OnLogindLine("/org/freedesktop/login1/session/_32: org.freedesktop.login1.Session.Unlock ()");
        power.OnLogindLine("/org/freedesktop/login1: org.freedesktop.DBus.Properties.PropertiesChanged ('org.freedesktop.login1.Manager', {'LidClosed': <true>}, @as [])");
        power.OnLogindLine("Monitoring signals from all objects owned by org.freedesktop.login1");

        Assert.Equal(["suspend", "resume", "lock", "unlock", "lid-closed"], log);
    }

    [Fact]
    public void UpowerPropertyChangeRaisesPowerSource()
    {
        using var fs = new FakeSysfs();
        using var power = Create(fs, new FakeRunner().Has("gdbus"));

        var sources = new List<PowerSource>();
        power.PowerSourceChanged += sources.Add;

        power.OnUpowerLine("/org/freedesktop/UPower: org.freedesktop.DBus.Properties.PropertiesChanged ('org.freedesktop.UPower', {'OnBattery': <true>}, @as [])");
        power.OnUpowerLine("/org/freedesktop/UPower: org.freedesktop.DBus.Properties.PropertiesChanged ('org.freedesktop.UPower', {'OnBattery': <false>}, @as [])");

        Assert.Equal([PowerSource.Battery, PowerSource.AC], sources);
    }

    [Fact]
    public void SleepAndLockUseSystemd()
    {
        using var fs = new FakeSysfs();
        var runner = new FakeRunner().Has("systemctl", "loginctl");
        using var power = Create(fs, runner);

        power.Sleep();
        power.LockWorkstation();
        Assert.Contains("systemctl suspend", runner.Calls);
        Assert.Contains("loginctl lock-session", runner.Calls);
    }
}

public class LinuxDisplayParsingTests
{
    private const string Xrandr = """
        Screen 0: minimum 320 x 200, current 2560 x 1600, maximum 16384 x 16384
        eDP-1 connected primary 2560x1600+0+0 (normal left inverted right x axis y axis) 345mm x 215mm
           2560x1600    165.00*+ 120.00  60.00
           1920x1200    165.00   60.00
           1600x1000    60.00
        HDMI-1 disconnected (normal left inverted right x axis y axis)
        DP-2 connected 1920x1080+2560+0 (normal left inverted right x axis y axis) 527mm x 296mm
           1920x1080     60.00*+  59.94
        """;

    [Fact]
    public void XrandrModesAreParsedPerOutput()
    {
        var modes = XrandrBackend.ParseOutput(Xrandr, "eDP-1");

        Assert.Equal(3 + 2 + 1, modes.Count);
        DisplayMode current = Assert.Single(modes, m => m.IsCurrent);
        Assert.Equal(2560, current.Width);
        Assert.Equal(165, current.RoundedHz);
        Assert.Equal(165, modes.Max(m => m.RoundedHz));

        var external = XrandrBackend.ParseOutput(Xrandr, "DP-2");
        Assert.Equal(2, external.Count);
        Assert.Empty(XrandrBackend.ParseOutput(Xrandr, "HDMI-1"));
    }

    [Fact]
    public void XrandrOutputsAreListed()
    {
        var outputs = XrandrBackend.ParseOutputs(Xrandr);
        Assert.True(outputs["eDP-1"]);
        Assert.False(outputs["HDMI-1"]);
    }

    [Fact]
    public void WlrRandrModesAreParsed()
    {
        const string text = """
            eDP-1 "AU Optronics 0xD291 (eDP-1)"
              Physical size: 340x210 mm
              Enabled: yes
              Modes:
                2560x1600 px, 165.000000 Hz (preferred, current)
                2560x1600 px, 60.000000 Hz
              Position: 0,0
            HDMI-A-1 "Dell"
              Modes:
                1920x1080 px, 60.000000 Hz (preferred, current)
            """;
        var modes = WlrRandrBackend.ParseOutput(text, "eDP-1");

        Assert.Equal(2, modes.Count);
        Assert.Equal(165, Assert.Single(modes, m => m.IsCurrent).RoundedHz);
    }

    [Fact]
    public void KscreenDoctorModesAreParsed()
    {
        const string text = "\u001b[01;32mOutput: \u001b[0;0m1 eDP-1 enabled connected priority 1 Panel Modes:  0:2560x1600@60.00!  1:2560x1600@165.00*  2:1920x1200@165.00 Geometry: 0,0 1280x800";
        var modes = KscreenBackend.ParseOutput(text, "eDP-1");

        Assert.Equal(3, modes.Count);
        DisplayMode current = Assert.Single(modes, m => m.IsCurrent);
        Assert.Equal("1", current.Id);
        Assert.Equal(165, current.RoundedHz);
    }

    private const string MutterStateText =
        "(uint32 7, [(('eDP-1', 'AUO', '0xd291', '0x00000000'), [('2560x1600@60.000', 2560, 1600, 60.0, 1.0, [1.0, 2.0], {}), " +
        "('2560x1600@165.000', 2560, 1600, 165.0, 1.0, [1.0, 2.0], {'is-current': <true>, 'is-preferred': <true>})], " +
        "{'display-name': <'Built-in display'>}), (('HDMI-1', 'DEL', 'x', 'y'), [('1920x1080@60.000', 1920, 1080, 60.0, 1.0, [1.0], {'is-current': <true>})], {})], " +
        "[(0, 0, 1.0, uint32 0, true, [('eDP-1', 'AUO', '0xd291', '0x00000000')], {}), (2560, 0, 1.0, uint32 0, false, [('HDMI-1', 'DEL', 'x', 'y')], {})], " +
        "{'layout-mode': <uint32 1>})";

    [Fact]
    public void MutterStateIsParsed()
    {
        MutterState? state = MutterState.Parse(MutterStateText);

        Assert.NotNull(state);
        Assert.Equal(7, state!.Serial);
        Assert.Equal(2, state.Monitors.Count);
        MutterMonitor panel = state.Monitors[0];
        Assert.Equal("eDP-1", panel.Connector);
        Assert.Equal(2, panel.Modes.Count);
        Assert.Equal(165, Assert.Single(panel.Modes, m => m.IsCurrent).RoundedHz);
        Assert.Equal(2, state.Logical.Count);
        Assert.True(state.Logical[0].Primary);
    }

    [Fact]
    public void MutterApplyConfigKeepsOtherMonitorsAndSwitchesTheMode()
    {
        MutterState state = MutterState.Parse(MutterStateText)!;
        string config = state.BuildApplyConfig("eDP-1", "2560x1600@60.000");

        Assert.Equal(
            "[(0, 0, 1.0, uint32 0, true, [('eDP-1', '2560x1600@60.000', {})]), " +
            "(2560, 0, 1.0, uint32 0, false, [('HDMI-1', '1920x1080@60.000', {})])]",
            config);
    }

    [Fact]
    public void MutterGarbageIsRejected()
    {
        Assert.Null(MutterState.Parse("nonsense"));
        Assert.Null(MutterState.Parse(""));
    }

    [Fact]
    public void XinputTouchscreenIsFound()
    {
        const string list = """
            ⎡ Virtual core pointer                    	id=2	[master pointer  (3)]
            ⎜   ↳ ELAN9009:00 04F3:2C1A                	id=11	[slave  pointer  (2)]
            ⎜   ↳ ELAN9009:00 04F3:2C1A Touchscreen    	id=12	[slave  pointer  (2)]
            """;
        Assert.Equal(12, LinuxDisplayService.ParseTouchscreenId(list));
        Assert.Null(LinuxDisplayService.ParseTouchscreenId("⎡ Virtual core pointer\tid=2\t[master pointer  (3)]"));
    }

    [Fact]
    public void GammaExponentIsFittedFromTheRamp()
    {
        var linear = new ushort[256];
        for (int i = 0; i < 256; i++) linear[i] = (ushort)(i * 257);
        Assert.InRange(LinuxDisplayService.FitGamma(linear), 0.99, 1.01);

        var bright = new ushort[256];
        for (int i = 0; i < 256; i++) bright[i] = (ushort)(65535 * Math.Pow(i / 255.0, 1 / 1.5));
        Assert.InRange(LinuxDisplayService.FitGamma(bright), 1.45, 1.55);
    }
}

public class LinuxDisplayServiceTests
{
    private static LinuxDisplayService Create(FakeSysfs fs, FakeRunner? runner = null, Func<string, string?>? env = null)
        => new(new LinuxContext(fs.Root, runner ?? new FakeRunner(), getEnv: env ?? (_ => null), getEuid: () => 1000));

    [Fact]
    public void InternalAndExternalConnectorsAreDetectedFromDrm()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/class/drm/card1-eDP-1/status", "connected");
        fs.File("/sys/class/drm/card1-HDMI-A-1/status", "disconnected");
        fs.File("/sys/class/drm/card1-DP-3/status", "disconnected");
        fs.Dir("/sys/class/drm/card1");
        var display = Create(fs);

        Assert.Equal("eDP-1", display.FindLaptopScreen());
        Assert.False(display.IsExternalDisplayConnected());

        fs.File("/sys/class/drm/card1-HDMI-A-1/status", "connected");
        Assert.True(display.IsExternalDisplayConnected());
    }

    [Fact]
    public void BacklightPrefersFirmwareOverRawAndIgnoresScreenpad()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/class/backlight/amdgpu_bl1/brightness", "128").File("/sys/class/backlight/amdgpu_bl1/max_brightness", "255")
            .File("/sys/class/backlight/amdgpu_bl1/type", "raw");
        fs.File("/sys/class/backlight/asus_screenpad/brightness", "10").File("/sys/class/backlight/asus_screenpad/max_brightness", "255")
            .File("/sys/class/backlight/asus_screenpad/type", "raw");
        var display = Create(fs);

        Assert.Equal(50, display.GetBrightness());
        display.SetBrightness(100);
        Assert.Equal("255", fs.Read("/sys/class/backlight/amdgpu_bl1/brightness"));
        Assert.Equal("10", fs.Read("/sys/class/backlight/asus_screenpad/brightness"));

        Assert.Equal(90, display.AdjustBrightness(-10));
        display.SetBrightness(0);
        Assert.Equal("1", fs.Read("/sys/class/backlight/amdgpu_bl1/brightness")); // never fully dark
    }

    [Fact]
    public void RefreshRateIsQueriedThroughXrandrOnX11()
    {
        using var fs = new FakeSysfs();
        var runner = new FakeRunner().Has("xrandr")
            .Reply("xrandr --query", "eDP-1 connected primary 2560x1600+0+0\n   2560x1600    165.00*+ 60.00\n");
        var display = Create(fs, runner, n => n == "DISPLAY" ? ":0" : null);

        Assert.Equal(165, display.GetRefreshRate("eDP-1"));
        Assert.Equal(165, display.GetMaxRefreshRate("eDP-1"));
        Assert.Equal(1, display.SetRefreshRate("eDP-1", 60));
        Assert.Contains("xrandr --output eDP-1 --mode 2560x1600 --rate 60.00", runner.Calls);
        Assert.Equal(1, display.SetRefreshRate("eDP-1", 165)); // already active
    }

    [Fact]
    public async Task ColorProfileInstallAndLookup()
    {
        using var fs = new FakeSysfs();
        var display = Create(fs, env: n => n == "HOME" ? "/home/u" : null);

        Assert.False(display.IsColorProfileInstalled("ghelper.icm"));
        await display.InstallColorProfile("ghelper.icm", [1, 2, 3]);
        Assert.True(display.IsColorProfileInstalled("ghelper.icm"));
        Assert.True(fs.Exists("/home/u/.local/share/icc/ghelper.icm"));
    }
}
