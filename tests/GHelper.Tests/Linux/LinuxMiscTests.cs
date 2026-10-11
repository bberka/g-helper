using GHelper.Input;
using GHelper.Platform;
using GHelper.Platform.Linux;
using Xunit;

namespace GHelper.Tests.Linux;

public class SysfsRootTests
{
    [Fact]
    public void ReadsDecimalHexAndTrimsWhitespace()
    {
        using var fs = new FakeSysfs();
        fs.File("/a/dec", " 42\n").File("/a/hex", "0x1A\n").File("/a/neg", "-5").File("/a/text", "balanced\n").File("/a/empty", "");

        Assert.Equal(42, fs.Root.ReadInt("/a/dec"));
        Assert.Equal(26, fs.Root.ReadInt("/a/hex"));
        Assert.Equal(-5, fs.Root.ReadInt("/a/neg"));
        Assert.Equal("balanced", fs.Root.ReadText("/a/text"));
        Assert.Null(fs.Root.ReadInt("/a/text"));
        Assert.Null(fs.Root.ReadInt("/a/empty"));
        Assert.Null(fs.Root.ReadInt("/a/missing"));
        Assert.Null(fs.Root.ReadText("/a/missing"));
    }

    [Fact]
    public void WriteReplacesTheWholeContentAndNeverCreatesFiles()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/x/value", "balanced-performance");

        Assert.True(fs.Root.TryWrite("/sys/x/value", "1"));
        Assert.Equal("1", fs.Read("/sys/x/value"));

        Assert.False(fs.Root.TryWrite("/sys/x/absent", "1"));
        Assert.False(fs.Exists("/sys/x/absent"));
    }

    [Fact]
    public void ListsDirectoriesWithLogicalPaths()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/class/hwmon/hwmon1/name", "a").File("/sys/class/hwmon/hwmon0/name", "b");

        Assert.Equal(["/sys/class/hwmon/hwmon0", "/sys/class/hwmon/hwmon1"], fs.Root.ListDirectories("/sys/class/hwmon"));
        Assert.Empty(fs.Root.ListDirectories("/sys/class/none"));
    }
}

public class LinuxParsingTests
{
    [Fact]
    public void DbusSignalLinesAreParsed()
    {
        DbusSignal? s = DbusSignal.Parse("/org/freedesktop/login1: org.freedesktop.login1.Manager.PrepareForSleep (true,)");
        Assert.NotNull(s);
        Assert.Equal("PrepareForSleep", s!.Value.Member);
        Assert.Equal("org.freedesktop.login1.Manager", s.Value.Interface);
        Assert.True(s.Value.FirstBool);

        DbusSignal? props = DbusSignal.Parse("/org/x: org.freedesktop.DBus.Properties.PropertiesChanged ('org.x', {'LidClosed': <false>, 'Mode': <'abc'>}, @as [])");
        Assert.Equal(false, props!.Value.PropertyBool("LidClosed"));
        Assert.Equal("abc", props.Value.PropertyString("Mode"));
        Assert.Null(props.Value.PropertyBool("Other"));

        Assert.Null(DbusSignal.Parse("Monitoring signals from all objects owned by org.freedesktop.login1"));
    }

    [Fact]
    public void SystemdConfLaterFilesWinAndEmptyResets()
    {
        using var fs = new FakeSysfs();
        fs.File("/usr/lib/systemd/logind.conf", "[Login]\nHandleLidSwitch=suspend\n");
        fs.File("/etc/systemd/logind.conf.d/a.conf", "[Login]\nHandleLidSwitch=hibernate\n");
        fs.File("/etc/systemd/logind.conf.d/b.conf", "[Other]\nHandleLidSwitch=poweroff\n[Login]\nHandleLidSwitch=ignore\n");

        Assert.Equal("ignore", SystemdConf.Read(fs.Root, "logind.conf", "Login", "HandleLidSwitch"));
        Assert.Null(SystemdConf.Read(fs.Root, "logind.conf", "Login", "Missing"));

        fs.File("/etc/systemd/logind.conf.d/c.conf", "[Login]\nHandleLidSwitch=\n");
        Assert.Null(SystemdConf.Read(fs.Root, "logind.conf", "Login", "HandleLidSwitch"));
    }

    [Fact]
    public void EdidMonitorNameAndManufacturer()
    {
        var edid = new byte[128];
        // manufacturer "SDC" = 0x4C83
        edid[8] = 0x4C;
        edid[9] = 0x83;
        byte[] name = System.Text.Encoding.ASCII.GetBytes("ATNA60CL07-0\n");
        edid[54 + 3] = 0xFC;
        Array.Copy(name, 0, edid, 54 + 5, Math.Min(13, name.Length));

        Assert.Equal("SDC", Edid.Manufacturer(edid));
        Assert.Equal("ATNA60CL07-0", Edid.MonitorName(edid));
        Assert.True(Edid.LooksLikeOled("ATNA60CL07-0"));
        Assert.True(Edid.LooksLikeOled("ASUS ROG OLED"));
        Assert.False(Edid.LooksLikeOled("NE160QDM-NY1"));
        Assert.Null(Edid.MonitorName(new byte[10]));
    }

    [Fact]
    public void AudioMuteOutputIsParsed()
    {
        Assert.True(LinuxAudioService.ParseWpctlMuted("Volume: 0.40 [MUTED]"));
        Assert.False(LinuxAudioService.ParseWpctlMuted("Volume: 0.40"));
        Assert.Null(LinuxAudioService.ParseWpctlMuted("Could not find"));
        Assert.True(LinuxAudioService.ParsePactlMuted("Mute: yes"));
        Assert.False(LinuxAudioService.ParsePactlMuted("Mute: no"));
        Assert.Null(LinuxAudioService.ParsePactlMuted(""));
    }

    [Fact]
    public void ProcStatAndMeminfoAreParsed()
    {
        var stat = LinuxSystemSensors.ParseCpuStat("cpu  100 0 50 800 50 0 0 0 0 0");
        Assert.Equal(1000UL, stat!.Value.total);
        Assert.Equal(850UL, stat.Value.idle);
        Assert.Null(LinuxSystemSensors.ParseCpuStat("cpu0 1 2 3"));

        var mem = LinuxSystemSensors.ParseMeminfo(["MemTotal:       16000000 kB", "MemFree: 1000000 kB", "MemAvailable:    8000000 kB"]);
        Assert.Equal(50, mem!.Value.percent);
        Assert.Equal(7812, mem.Value.usedMb);
    }

    [Fact]
    public void KeyMapIsBijectiveForTheKeysTheAppUses()
    {
        foreach (Keys key in new[] { Keys.A, Keys.Z, Keys.D0, Keys.F1, Keys.F12, Keys.F24, Keys.LWin, Keys.Left, Keys.VolumeUp,
                     Keys.MediaPlayPause, Keys.PrintScreen, Keys.OemSemicolon, Keys.LControlKey })
        {
            Assert.True(LinuxKeyMap.TryGetEvdev(key, out int code), key.ToString());
            Assert.True(LinuxKeyMap.TryGetKey(code, out Keys back));
            Assert.Equal(key, back);
        }

        Assert.True(LinuxKeyMap.TryGetEvdev(Keys.A, out int a));
        Assert.Equal(30, a);
        Assert.Equal(HotkeyModifiers.Control, LinuxKeyMap.ModifierOf(97));
        Assert.Equal(HotkeyModifiers.Win, LinuxKeyMap.ModifierOf(125));
        Assert.Equal(HotkeyModifiers.None, LinuxKeyMap.ModifierOf(30));
    }
}

public class LinuxStartupServiceTests
{
    private static (LinuxStartupService startup, LinuxContext ctx) Create(FakeSysfs fs, FakeRunner? runner = null, bool systemd = false, uint euid = 1000)
    {
        var ctx = new LinuxContext(fs.Root, runner ?? new FakeRunner(),
            getEnv: n => n == "HOME" ? "/home/u" : null, getEuid: () => euid, processPath: "/opt/ghelper/GHelper.App");
        return (new LinuxStartupService(ctx, systemd), ctx);
    }

    [Fact]
    public void ScheduleWritesAnXdgAutostartEntry()
    {
        using var fs = new FakeSysfs();
        var (startup, _) = Create(fs);

        Assert.False(startup.IsScheduled());
        startup.Schedule();
        Assert.True(startup.IsScheduled());

        string text = fs.Read("/home/u/.config/autostart/ghelper.desktop");
        Assert.Contains("[Desktop Entry]", text);
        Assert.Contains("Exec=\"/opt/ghelper/GHelper.App\"", text);
        Assert.Contains("Type=Application", text);

        startup.UnSchedule();
        Assert.False(startup.IsScheduled());
    }

    [Fact]
    public void SystemdUserUnitIsOptional()
    {
        using var fs = new FakeSysfs();
        var runner = new FakeRunner().Has("systemctl");
        var (startup, _) = Create(fs, runner, systemd: true);

        startup.Schedule();
        string unit = fs.Read("/home/u/.config/systemd/user/ghelper.service");
        Assert.Contains("ExecStart=/opt/ghelper/GHelper.App", unit);
        Assert.Contains("WantedBy=graphical-session.target", unit);
        Assert.Contains("systemctl --user enable ghelper.service", runner.Calls);

        startup.UnSchedule();
        Assert.Contains("systemctl --user disable ghelper.service", runner.Calls);
        Assert.False(fs.Exists("/home/u/.config/systemd/user/ghelper.service"));
    }

    [Fact]
    public void ExecQuotingFollowsTheDesktopEntrySpec()
    {
        Assert.Equal("\"/opt/my app/ghelper\"", LinuxStartupService.QuoteExec("/opt/my app/ghelper"));
        Assert.Equal("\"a\\$b\\\"c%%\"", LinuxStartupService.QuoteExec("a$b\"c%"));
        Assert.Equal("/opt/my app/ghelper", LinuxStartupService.ParseExecTarget("[Desktop Entry]\nExec=\"/opt/my app/ghelper\" --x\n"));
        Assert.Equal("/usr/bin/ghelper", LinuxStartupService.ParseExecTarget("Exec=/usr/bin/ghelper %U"));
        Assert.Null(LinuxStartupService.ParseExecTarget("Name=x"));
    }

    [Fact]
    public void StartupCheckRepairsEntriesPointingToMissingFiles()
    {
        using var fs = new FakeSysfs();
        fs.File("/home/u/.config/autostart/ghelper.desktop", "[Desktop Entry]\nExec=\"/old/place/ghelper\"\n");
        var (startup, _) = Create(fs);

        startup.StartupCheck();
        Assert.Contains("/opt/ghelper/GHelper.App", fs.Read("/home/u/.config/autostart/ghelper.desktop"));
    }

    [Fact]
    public void ChargeUnitIsOnlyManagedAsRoot()
    {
        using var fs = new FakeSysfs();
        var runner = new FakeRunner().Has("systemctl");

        var (user, _) = Create(fs, runner, euid: 1000);
        user.ScheduleCharge();
        Assert.False(fs.Exists(LinuxStartupService.ChargeUnitPath));

        var (root, _) = Create(fs, runner, euid: 0);
        root.ScheduleCharge();
        Assert.Contains("ExecStart=/opt/ghelper/GHelper.App charge", fs.Read(LinuxStartupService.ChargeUnitPath));
        Assert.Contains("systemctl enable ghelper-charge.service", runner.Calls);

        root.UnscheduleCharge();
        Assert.False(fs.Exists(LinuxStartupService.ChargeUnitPath));
    }
}

public class LinuxProcessServiceTests
{
    private static LinuxProcessService Create(FakeSysfs fs, FakeRunner? runner = null, uint euid = 1000, Dictionary<string, string>? env = null)
    {
        env ??= new Dictionary<string, string>();
        env["XDG_RUNTIME_DIR"] = "/run/user/1000";
        var ctx = new LinuxContext(fs.Root, runner ?? new FakeRunner(), getEnv: n => env.GetValueOrDefault(n), getEuid: () => euid,
            processPath: "/opt/ghelper/GHelper.App");
        return new LinuxProcessService(ctx);
    }

    [Fact]
    public void SecondInstanceIsRejectedAndFirstOneIsActivated()
    {
        using var fs = new FakeSysfs();
        fs.Dir("/run/user/1000");
        using var activated = new ManualResetEventSlim();

        var first = Create(fs);
        var second = Create(fs);
        try
        {
            Assert.True(first.AcquireSingleInstance("GHelper", activated.Set));
            Assert.False(second.AcquireSingleInstance("GHelper", () => { }));
            Assert.True(activated.Wait(TimeSpan.FromSeconds(5)), "first instance was not signalled");
        }
        finally
        {
            first.ReleaseSingleInstance();
            second.ReleaseSingleInstance();
        }

        // lock released: a new process can become the owner again
        var third = Create(fs);
        try
        {
            Assert.True(third.AcquireSingleInstance("GHelper", () => { }));
        }
        finally
        {
            third.ReleaseSingleInstance();
        }
    }

    [Fact]
    public void ElevationGoesThroughPkexecWithTheSessionEnvironment()
    {
        using var fs = new FakeSysfs();
        var runner = new FakeRunner().Has("pkexec");
        var env = new Dictionary<string, string> { ["DISPLAY"] = ":0", ["WAYLAND_DISPLAY"] = "wayland-0" };
        var process = Create(fs, runner, env: env);

        process.RunAsAdmin("gpu");

        string call = Assert.Single(runner.Calls, c => c.StartsWith("detached:pkexec", StringComparison.Ordinal));
        Assert.Contains("env", call);
        Assert.Contains("DISPLAY=:0", call);
        Assert.Contains("WAYLAND_DISPLAY=wayland-0", call);
        Assert.Contains("GHELPER_TAKEOVER=1", call);
        Assert.EndsWith("/opt/ghelper/GHelper.App gpu", call);
    }

    [Fact]
    public void ElevationUsesTheInstalledLauncherAndNeverRunsTwiceWhenRoot()
    {
        using var fs = new FakeSysfs();
        fs.File("/usr/lib/ghelper/ghelper-root-launcher", "#!/bin/sh");
        var runner = new FakeRunner().Has("pkexec");
        var process = Create(fs, runner);

        process.RunAsAdmin();
        Assert.Contains(runner.Calls, c => c.StartsWith("detached:pkexec /usr/lib/ghelper/ghelper-root-launcher", StringComparison.Ordinal) && c.Contains(" -- "));

        var rootRunner = new FakeRunner().Has("pkexec");
        Create(fs, rootRunner, euid: 0).RunAsAdmin();
        Assert.DoesNotContain(rootRunner.Calls, c => c.StartsWith("detached:pkexec", StringComparison.Ordinal));
    }

    [Fact]
    public void UrlsAreOpenedWithXdgOpen()
    {
        using var fs = new FakeSysfs();
        var runner = new FakeRunner().Has("xdg-open");
        var process = Create(fs, runner);

        process.OpenUrl("https://github.com/bberka/g-helper");
        Assert.Contains("detached:xdg-open https://github.com/bberka/g-helper", runner.Calls);
    }
}

public class LinuxVendorServicesTests
{
    [Fact]
    public void ConflictingDaemonsAreDetected()
    {
        using var fs = new FakeSysfs();
        fs.File("/proc/123/comm", "asusd\n").File("/proc/456/comm", "supergfxd\n").File("/proc/789/comm", "bash\n");
        fs.File("/usr/lib/systemd/system/asusd.service", "[Unit]");
        var ctx = new LinuxContext(fs.Root, new FakeRunner(), getEnv: _ => null, getEuid: () => 1000);
        var vendor = new LinuxVendorServices(ctx, configFlag: _ => false);

        Assert.True(vendor.IsAsusOptimizationRunning());
        Assert.True(vendor.IsArmouryRunning());
        Assert.Equal(2, vendor.GetRunningCount());
        Assert.Contains(vendor.DetectConflicts(), c => c.Name == "asusd" && c.Running);
    }

    [Fact]
    public void StoppingServicesRequiresRoot()
    {
        using var fs = new FakeSysfs();
        fs.File("/proc/1/comm", "asusd\n");
        fs.File("/usr/lib/systemd/system/asusd.service", "[Unit]");
        var runner = new FakeRunner().Has("systemctl");

        new LinuxVendorServices(new LinuxContext(fs.Root, runner, getEnv: _ => null, getEuid: () => 1000), configFlag: _ => false)
            .StopAsusServices();
        Assert.Empty(runner.Calls);

        new LinuxVendorServices(new LinuxContext(fs.Root, runner, getEnv: _ => null, getEuid: () => 0), configFlag: _ => false)
            .StopAsusServices();
        Assert.Contains("systemctl disable --now asusd.service", runner.Calls);
    }

    [Fact]
    public void GpuWithoutDriverIsReported()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/bus/pci/devices/0000:01:00.0/class", "0x030200");
        fs.File("/sys/bus/pci/devices/0000:01:00.0/vendor", "0x10de");
        fs.File("/sys/bus/pci/devices/0000:01:00.0/device", "0x2684");
        var vendor = new LinuxVendorServices(new LinuxContext(fs.Root, new FakeRunner()), configFlag: _ => false);

        Assert.Contains("0000:01:00.0", vendor.GetGpuError());
    }
}

public class LinuxCpuAndSensorTests
{
    [Theory]
    [InlineData("AuthenticAMD", 0x19, 0x44, "Mobile")]     // Rembrandt
    [InlineData("AuthenticAMD", 0x19, 0x50, "Renoir")]     // Cezanne
    [InlineData("AuthenticAMD", 0x17, 0x60, "Renoir")]     // Renoir
    [InlineData("AuthenticAMD", 0x19, 0x74, "Mobile")]     // Phoenix
    [InlineData("AuthenticAMD", 0x19, 0x61, "Raphael")]    // Raphael / Dragon Range
    [InlineData("AuthenticAMD", 0x1A, 0x24, "StrixPoint")]
    [InlineData("AuthenticAMD", 0x1A, 0x70, "StrixHalo")]
    [InlineData("GenuineIntel", 6, 0xB7, "Unknown")]
    public void RyzenFamilyIsResolvedFromFamilyAndModel(string vendor, int family, int model, string expected)
    {
        Assert.Equal(expected, new CpuIdentity(vendor, family, model, "").ResolveRyzenFamily().ToString());
    }

    [Fact]
    public void RyzenSmuCommandsAreWrittenToTheModuleFiles()
    {
        using var fs = new FakeSysfs();
        fs.File("/proc/cpuinfo", "vendor_id\t: AuthenticAMD\ncpu family\t: 25\nmodel\t\t: 68\nmodel name\t: AMD Ryzen 9 6900HS\n");
        fs.Bytes("/sys/kernel/ryzen_smu_drv/smu_args", new byte[24]);
        fs.Bytes("/sys/kernel/ryzen_smu_drv/mp1_smu_cmd", new byte[4]);
        fs.Bytes("/sys/kernel/ryzen_smu_drv/rsmu_cmd", new byte[4]);

        using var tuning = new LinuxCpuTuning(new LinuxContext(fs.Root, new FakeRunner()));
        Assert.True(tuning.IsAmd);
        Assert.True(tuning.Initialize());
        Assert.True(tuning.CanSetTDP());

        // Rembrandt: thermal limit = MP1 command 0x19 with the degrees as argument
        tuning.SetThm(95);
        Assert.Equal(0x19u, BitConverter.ToUInt32(File.ReadAllBytes(fs.Physical("/sys/kernel/ryzen_smu_drv/mp1_smu_cmd"))));
        Assert.Equal(95u, BitConverter.ToUInt32(File.ReadAllBytes(fs.Physical("/sys/kernel/ryzen_smu_drv/smu_args"))));

        // curve optimizer all-core -20: MP1 0x4C, 0x100000 - 20
        tuning.SetCoAll(-20);
        Assert.Equal(0x4Cu, BitConverter.ToUInt32(File.ReadAllBytes(fs.Physical("/sys/kernel/ryzen_smu_drv/mp1_smu_cmd"))));
        Assert.Equal(0x100000u - 20u, BitConverter.ToUInt32(File.ReadAllBytes(fs.Physical("/sys/kernel/ryzen_smu_drv/smu_args"))));
    }

    [Fact]
    public void RyzenModuleMissingMeansUnsupported()
    {
        using var fs = new FakeSysfs();
        fs.File("/proc/cpuinfo", "vendor_id\t: AuthenticAMD\ncpu family\t: 25\nmodel\t\t: 68\n");
        using var tuning = new LinuxCpuTuning(new LinuxContext(fs.Root, new FakeRunner()));

        Assert.True(tuning.IsAmd);
        Assert.False(tuning.Initialize());
        Assert.False(tuning.CanSetTDP());
        Assert.False(tuning.SetAllLimits(30, 40, 50));
        Assert.NotEqual(1, tuning.SetCoAll(-10));
    }

    [Fact]
    public void PmTableLimitsAreReadAsWatts()
    {
        using var fs = new FakeSysfs();
        fs.File("/proc/cpuinfo", "vendor_id\t: AuthenticAMD\ncpu family\t: 25\nmodel\t\t: 68\n");
        fs.Bytes("/sys/kernel/ryzen_smu_drv/smu_args", new byte[24]);
        fs.Bytes("/sys/kernel/ryzen_smu_drv/mp1_smu_cmd", new byte[4]);
        var table = new byte[64];
        BitConverter.GetBytes(35f).CopyTo(table, 0);
        BitConverter.GetBytes(45f).CopyTo(table, 8);
        BitConverter.GetBytes(40f).CopyTo(table, 16);
        fs.Bytes("/sys/kernel/ryzen_smu_drv/pm_table", table);

        using var tuning = new LinuxCpuTuning(new LinuxContext(fs.Root, new FakeRunner()));
        tuning.Initialize();
        Assert.Equal((35, 45, 40), tuning.GetPowerLimits());
    }

    [Fact]
    public void HwmonCpuTemperatureAndBatteryState()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/class/hwmon/hwmon2/name", "k10temp");
        fs.File("/sys/class/hwmon/hwmon2/temp1_input", "61250");
        fs.File("/sys/class/hwmon/hwmon2/temp1_label", "Tctl");
        fs.File("/sys/class/hwmon/hwmon4/name", "asus");
        fs.File("/sys/class/hwmon/hwmon4/fan1_input", "3800");
        fs.File("/sys/class/power_supply/BAT0/type", "Battery");
        fs.File("/sys/class/power_supply/BAT0/status", "Discharging");
        fs.File("/sys/class/power_supply/BAT0/capacity", "73");
        fs.File("/sys/class/power_supply/BAT0/energy_full_design", "76000000");
        fs.File("/sys/class/power_supply/BAT0/energy_full", "72000000");
        fs.File("/sys/class/power_supply/BAT0/voltage_now", "15200000");
        fs.File("/sys/class/power_supply/BAT0/power_now", "18500000");
        fs.File("/sys/class/power_supply/ACAD/type", "Mains");
        fs.File("/sys/class/power_supply/ACAD/online", "0");
        var sensors = new LinuxSystemSensors(new LinuxContext(fs.Root, new FakeRunner()));

        Assert.Equal(61.25f, sensors.GetCpuTemp());
        Assert.Equal(3800, sensors.GetFanRpm(0));
        Assert.Null(sensors.GetFanRpm(1));

        BatteryState? battery = sensors.GetBatteryState();
        Assert.NotNull(battery);
        Assert.True(battery!.Value.Present);
        Assert.False(battery.Value.Charging);
        Assert.False(battery.Value.OnAc);
        Assert.Equal(73, battery.Value.Percent);
        Assert.Equal(18.5, battery.Value.DischargeRateW);
        Assert.Equal(76.0, battery.Value.DesignCapacityWh);
        Assert.Equal(72.0, battery.Value.FullChargeCapacityWh);
    }

    [Fact]
    public void AmdGpuTelemetryComesFromSysfs()
    {
        using var fs = new FakeSysfs();
        const string dev = "/sys/bus/pci/devices/0000:03:00.0";
        fs.File(dev + "/class", "0x030000").File(dev + "/vendor", "0x1002").File(dev + "/device", "0x73ef");
        fs.File(dev + "/boot_vga", "0").File(dev + "/power/runtime_status", "active");
        fs.File(dev + "/mem_info_vram_total", (8L * 1024 * 1024 * 1024).ToString());
        fs.File(dev + "/mem_info_vram_used", (2L * 1024 * 1024 * 1024).ToString());
        fs.File(dev + "/gpu_busy_percent", "37");
        fs.File(dev + "/hwmon/hwmon7/temp1_input", "55000");
        fs.File(dev + "/hwmon/hwmon7/power1_average", "42000000");
        // the driver symlink is only created on real systems; without it the provider must not claim the device
        var provider = new LinuxGpuProvider(new LinuxContext(fs.Root, new FakeRunner()));
        Assert.Null(provider.CreateDiscrete());

        PciGpu gpu = PciGpu.Enumerate(fs.Root).Single();
        Assert.Equal(GpuVendor.Amd, gpu.Vendor);
        Assert.Equal(0x73ef, gpu.DeviceId);

        var control = new SysfsGpuControl(new LinuxContext(fs.Root, new FakeRunner()), gpu, isNvidia: false);
        Assert.Equal(55, control.GetCurrentTemperature());
        Assert.Equal(37, control.GetGpuUse());
        Assert.Equal((2048L, 8192L), control.GetVramInfo());
        Assert.Equal(42f, control.GetGpuPower());

        // runtime suspended: no telemetry, no wake up
        fs.File(dev + "/power/runtime_status", "suspended");
        Assert.Null(control.GetCurrentTemperature());
        Assert.Null(control.GetGpuUse());
        Assert.Equal(0f, control.GetGpuPower());
    }

    [Fact]
    public void PciIdsNameLookup()
    {
        using var fs = new FakeSysfs();
        fs.File("/usr/share/hwdata/pci.ids",
            "# comment\n10de  NVIDIA Corporation\n\t2684  AD102 [GeForce RTX 4090]\n\t2860  AD107M [GeForce RTX 4070 Max-Q]\n1002  Advanced Micro Devices, Inc. [AMD/ATI]\n\t73ef  Navi 23 [Radeon RX 6650 XT]\n");

        Assert.Equal("NVIDIA GeForce RTX 4070 Max-Q", PciIds.Describe(fs.Root, GpuVendor.Nvidia, 0x2860));
        Assert.Equal("AMD Radeon RX 6650 XT", PciIds.Describe(fs.Root, GpuVendor.Amd, 0x73ef));
        Assert.Null(PciIds.Describe(fs.Root, GpuVendor.Nvidia, 0x0001));
    }
}
