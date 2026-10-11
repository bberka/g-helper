using GHelper.Platform.Linux;
using Xunit;

namespace GHelper.Tests.Linux;

public class LinuxAsusBackendTests
{
    private const string PlatformDir = "/sys/devices/platform/asus-nb-wmi";
    private const string Armoury = "/sys/class/firmware-attributes/asus-armoury/attributes";

    private static LinuxAsusBackend Create(FakeSysfs fs, FakeRunner? runner = null, LinuxAsusBackendOptions? options = null)
    {
        options ??= new LinuxAsusBackendOptions { EnableEventListener = false };
        return new LinuxAsusBackend(new LinuxContext(fs.Root, runner ?? new FakeRunner()), options);
    }

    private static void ProfileHandler(FakeSysfs fs, string current, string choices = "quiet balanced performance")
    {
        fs.File(PlatformDir + "/dgpu_disable", "0"); // marks an ASUS machine
        fs.File("/sys/class/platform-profile/platform-profile-0/name", "asus-wmi");
        fs.File("/sys/class/platform-profile/platform-profile-0/profile", current);
        fs.File("/sys/class/platform-profile/platform-profile-0/choices", choices);
    }

    // ------------------------------------------------------------------ connection

    [Fact]
    public void NonAsusMachineIsNotConnectedAndEverythingIsUnsupported()
    {
        using var fs = new FakeSysfs();
        using var backend = Create(fs);

        Assert.False(backend.IsConnected);
        Assert.True(backend.DeviceGet(AsusACPI.PerformanceMode) < 0);
        Assert.True(backend.DeviceGet(AsusACPI.BatteryLimit) < 0);
        Assert.True(backend.DeviceGet(0x12345678) < 0);
        Assert.NotEqual(1, backend.DeviceSet(AsusACPI.PPT_APUA3, 45, null));
        Assert.Equal(16, backend.DeviceGetBuffer(AsusACPI.CPU_Fan).Length);
        Assert.All(backend.DeviceGetBuffer(AsusACPI.CPU_Fan), b => Assert.Equal(0, b));
    }

    [Fact]
    public void ArmouryOnlyMachineCountsAsConnected()
    {
        using var fs = new FakeSysfs();
        fs.File(Armoury + "/ppt_pl1_spl/current_value", "30");
        using var backend = Create(fs);
        Assert.True(backend.IsConnected);
    }

    // ------------------------------------------------------------------ performance mode

    [Theory]
    [InlineData("balanced", 0)]
    [InlineData("performance", 1)]
    [InlineData("quiet", 2)]
    public void PlatformProfileIsTranslatedToWindowsPerformanceMode(string profile, int expected)
    {
        using var fs = new FakeSysfs();
        ProfileHandler(fs, profile);
        using var backend = Create(fs);

        Assert.Equal(expected, backend.DeviceGet(AsusACPI.PerformanceMode));
    }

    [Theory]
    [InlineData(0, "balanced")]
    [InlineData(1, "performance")]
    [InlineData(2, "quiet")]
    [InlineData(3, "performance")] // full speed has no kernel equivalent
    public void SettingPerformanceModeWritesTheProfile(int mode, string expected)
    {
        using var fs = new FakeSysfs();
        ProfileHandler(fs, "balanced");
        using var backend = Create(fs);

        Assert.Equal(1, backend.DeviceSet(AsusACPI.PerformanceMode, mode, "Mode"));
        Assert.Equal(expected, fs.Read("/sys/class/platform-profile/platform-profile-0/profile"));
    }

    [Fact]
    public void ManualModeKeepsTheCurrentProfile()
    {
        using var fs = new FakeSysfs();
        ProfileHandler(fs, "quiet");
        using var backend = Create(fs);

        Assert.Equal(1, backend.DeviceSet(AsusACPI.PerformanceMode, AsusACPI.PerformanceManual, null));
        Assert.Equal("quiet", fs.Read("/sys/class/platform-profile/platform-profile-0/profile"));
    }

    [Fact]
    public void ProfileNamesFallBackWhenChoicesDiffer()
    {
        using var fs = new FakeSysfs();
        ProfileHandler(fs, "low-power", "low-power balanced balanced-performance");
        using var backend = Create(fs);

        Assert.Equal(2, backend.DeviceGet(AsusACPI.PerformanceMode));
        Assert.Equal(1, backend.DeviceSet(AsusACPI.PerformanceMode, 1, null));
        Assert.Equal("balanced-performance", fs.Read("/sys/class/platform-profile/platform-profile-0/profile"));
    }

    [Fact]
    public void LegacyThrottleThermalPolicyIsUsedWithoutPlatformProfile()
    {
        using var fs = new FakeSysfs();
        fs.File(PlatformDir + "/throttle_thermal_policy", "2");
        using var backend = Create(fs);

        Assert.Equal(2, backend.DeviceGet(AsusACPI.PerformanceMode));
        Assert.Equal(1, backend.DeviceSet(AsusACPI.PerformanceMode, 1, null));
        Assert.Equal("1", fs.Read(PlatformDir + "/throttle_thermal_policy"));
    }

    [Fact]
    public void VivobookModeSwapsTurboAndSilent()
    {
        using var fs = new FakeSysfs();
        ProfileHandler(fs, "balanced");
        using var backend = Create(fs);

        // the facade already swapped: raw vivo value 1 means "silent"
        Assert.Equal(1, backend.DeviceSet(AsusACPI.VivoBookMode, 1, null));
        Assert.Equal("quiet", fs.Read("/sys/class/platform-profile/platform-profile-0/profile"));
        Assert.Equal(1, backend.DeviceGet(AsusACPI.VivoBookMode));
    }

    [Fact]
    public void OlderKernelsUseTheGlobalPlatformProfileOnAsusOnly()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/firmware/acpi/platform_profile", "performance");
        fs.File("/sys/firmware/acpi/platform_profile_choices", "quiet balanced performance");

        using (var foreign = Create(fs))
            Assert.True(foreign.DeviceGet(AsusACPI.PerformanceMode) < 0); // not an ASUS machine: do not touch

        fs.Dir(PlatformDir);
        using var backend = Create(fs);
        Assert.Equal(1, backend.DeviceGet(AsusACPI.PerformanceMode));
    }

    // ------------------------------------------------------------------ PPT / GPU

    [Fact]
    public void PptLimitsUseArmouryAttributes()
    {
        using var fs = new FakeSysfs();
        fs.File(Armoury + "/ppt_pl1_spl/current_value", "45");
        fs.File(Armoury + "/ppt_pl2_sppt/current_value", "55");
        fs.File(Armoury + "/ppt_pl3_fppt/current_value", "65");
        fs.File(Armoury + "/ppt_apu_sppt/current_value", "35");
        fs.File(Armoury + "/ppt_platform_sppt/current_value", "75");
        fs.File(Armoury + "/nv_dynamic_boost/current_value", "15");
        fs.File(Armoury + "/nv_temp_target/current_value", "80");
        using var backend = Create(fs);

        Assert.Equal(45, backend.DeviceGet(AsusACPI.PPT_APUA3));
        Assert.Equal(55, backend.DeviceGet(AsusACPI.PPT_APUA0));
        Assert.Equal(65, backend.DeviceGet(AsusACPI.PPT_APUC1));
        Assert.Equal(35, backend.DeviceGet(AsusACPI.PPT_CPUB0));
        Assert.Equal(75, backend.DeviceGet(AsusACPI.PPT_CPUB1));
        Assert.Equal(15, backend.DeviceGet(AsusACPI.PPT_GPUC0));
        Assert.Equal(80, backend.DeviceGet(AsusACPI.PPT_GPUC2));

        Assert.Equal(1, backend.DeviceSet(AsusACPI.PPT_APUA3, 60, "PL1"));
        Assert.Equal("60", fs.Read(Armoury + "/ppt_pl1_spl/current_value"));
    }

    [Fact]
    public void PptLimitsFallBackToDeprecatedPlatformAttributes()
    {
        using var fs = new FakeSysfs();
        fs.File(PlatformDir + "/ppt_pl1_spl", "25");
        fs.File(PlatformDir + "/ppt_fppt", "40");
        using var backend = Create(fs);

        Assert.Equal(25, backend.DeviceGet(AsusACPI.PPT_APUA3));
        Assert.Equal(40, backend.DeviceGet(AsusACPI.PPT_APUC1));
        Assert.Equal(1, backend.DeviceSet(AsusACPI.PPT_APUC1, 50, null));
        Assert.Equal("50", fs.Read(PlatformDir + "/ppt_fppt"));
        Assert.True(backend.DeviceGet(AsusACPI.PPT_GPUC0) < 0);
    }

    [Fact]
    public void GpuModeAttributesUseWindowsValues()
    {
        using var fs = new FakeSysfs();
        fs.File(Armoury + "/dgpu_disable/current_value", "1");
        fs.File(Armoury + "/gpu_mux_mode/current_value", "0");
        fs.File(Armoury + "/egpu_connected/current_value", "1");
        using var backend = Create(fs);

        Assert.Equal(1, backend.DeviceGet(AsusACPI.GPUEcoROG));
        Assert.Equal(0, backend.DeviceGet(AsusACPI.GPUMuxROG));
        Assert.Equal(1, backend.DeviceGet(AsusACPI.GPUXGConnected));

        Assert.Equal(1, backend.DeviceSet(AsusACPI.GPUEcoROG, 0, null));
        Assert.Equal("0", fs.Read(Armoury + "/dgpu_disable/current_value"));
        Assert.Equal(1, backend.DeviceSet(AsusACPI.GPUMuxROG, 1, null));
        Assert.Equal("1", fs.Read(Armoury + "/gpu_mux_mode/current_value"));

        // read only attribute
        Assert.NotEqual(1, backend.DeviceSet(AsusACPI.GPUXGConnected, 0, null));
    }

    [Theory]
    [InlineData("0", 0x0)]
    [InlineData("1", 0x1)]
    [InlineData("2", 0x101)]
    [InlineData("3", 0x201)]
    public void EgpuEnumIndexIsTranslatedToRawWmiValue(string index, int raw)
    {
        using var fs = new FakeSysfs();
        fs.File(Armoury + "/egpu_enable/current_value", index);
        using var backend = Create(fs);

        Assert.Equal(raw, backend.DeviceGet(AsusACPI.GPUXG));
        Assert.Equal(1, backend.DeviceSet(AsusACPI.GPUXG, raw, null));
        Assert.Equal(index, fs.Read(Armoury + "/egpu_enable/current_value"));
    }

    [Fact]
    public void ApuMemoryIndexIsTranslatedToRawWmiValue()
    {
        using var fs = new FakeSysfs();
        fs.File(Armoury + "/apu_mem/current_value", "4");
        using var backend = Create(fs);

        Assert.Equal(0x105, backend.DeviceGet(AsusACPI.APU_MEM));
        Assert.Equal(1, backend.DeviceSet(AsusACPI.APU_MEM, 0x102, null)); // index 1
        Assert.Equal("1", fs.Read(Armoury + "/apu_mem/current_value"));
        Assert.Equal(1, backend.DeviceSet(AsusACPI.APU_MEM, 0, null));
        Assert.Equal("0", fs.Read(Armoury + "/apu_mem/current_value"));
        Assert.NotEqual(1, backend.DeviceSet(AsusACPI.APU_MEM, 0x777, null));
    }

    // ------------------------------------------------------------------ screen

    [Fact]
    public void OverdriveAndSupportFlag()
    {
        using var fs = new FakeSysfs();
        fs.File(Armoury + "/panel_overdrive/current_value", "1");
        using var backend = Create(fs);

        Assert.Equal(1, backend.DeviceGet(AsusACPI.ScreenOverdriveSupport));
        Assert.Equal(1, backend.DeviceGet(AsusACPI.ScreenOverdrive));
        Assert.Equal(1, backend.DeviceSet(AsusACPI.ScreenOverdrive, 0, null));
        Assert.Equal("0", fs.Read(Armoury + "/panel_overdrive/current_value"));
    }

    [Fact]
    public void FirstGenerationMiniLedUsesMiniled1()
    {
        using var fs = new FakeSysfs();
        fs.File(Armoury + "/mini_led_mode/current_value", "1");
        fs.File(Armoury + "/mini_led_mode/possible_values", "0;1");
        using var backend = Create(fs);

        Assert.Equal(1, backend.DeviceGet(AsusACPI.ScreenMiniled1));
        Assert.True(backend.DeviceGet(AsusACPI.ScreenMiniled2) < 0);
        Assert.Equal(1, backend.DeviceSet(AsusACPI.ScreenMiniled1, 0, null));
        Assert.Equal("0", fs.Read(Armoury + "/mini_led_mode/current_value"));
    }

    [Theory]
    [InlineData("0", 2)] // kernel index 0 = off  -> raw 2
    [InlineData("1", 0)] // weak                  -> raw 0
    [InlineData("2", 1)] // strong                -> raw 1
    public void SecondGenerationMiniLedIsMappedToRawValues(string index, int raw)
    {
        using var fs = new FakeSysfs();
        fs.File(Armoury + "/mini_led_mode/current_value", index);
        fs.File(Armoury + "/mini_led_mode/possible_values", "0;1;2");
        using var backend = Create(fs);

        Assert.Equal(raw, backend.DeviceGet(AsusACPI.ScreenMiniled2));
        Assert.True(backend.DeviceGet(AsusACPI.ScreenMiniled1) < 0);
        Assert.Equal(1, backend.DeviceSet(AsusACPI.ScreenMiniled2, raw, null));
        Assert.Equal(index, fs.Read(Armoury + "/mini_led_mode/current_value"));
    }

    // ------------------------------------------------------------------ battery

    [Fact]
    public void ChargeLimitIsWrittenToThePowerSupply()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/class/power_supply/BAT0/charge_control_end_threshold", "100");
        using var backend = Create(fs);

        Assert.Equal(100, backend.DeviceGet(AsusACPI.BatteryLimit));
        Assert.Equal(1, backend.DeviceSet(AsusACPI.BatteryLimit, 80, "Battery"));
        Assert.Equal("80", fs.Read("/sys/class/power_supply/BAT0/charge_control_end_threshold"));
        Assert.NotEqual(1, backend.DeviceSet(AsusACPI.BatteryLimit, 150, null));
        Assert.Equal("80", fs.Read("/sys/class/power_supply/BAT0/charge_control_end_threshold"));
    }

    [Fact]
    public void ChargeLimitUsesBattNameOnNewerZenbooks()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/class/power_supply/BATT/charge_control_end_threshold", "60");
        using var backend = Create(fs);
        Assert.Equal(60, backend.DeviceGet(AsusACPI.BatteryLimit));
    }

    [Fact]
    public void ChargerModeIsReadRaw()
    {
        using var fs = new FakeSysfs();
        fs.File(Armoury + "/charge_mode/current_value", "2");
        using var backend = Create(fs);
        Assert.Equal(2, backend.DeviceGet(AsusACPI.ChargerMode));
    }

    [Fact]
    public void BatteryDischargeBufferFollowsTheLegacyLayout()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/class/power_supply/BAT0/type", "Battery");
        fs.File("/sys/class/power_supply/BAT0/status", "Discharging");
        fs.File("/sys/class/power_supply/BAT0/power_now", "12340000");
        using var backend = Create(fs);

        byte[] buffer = backend.DeviceGetBuffer(AsusACPI.BatteryDischarge);
        Assert.True(buffer[2] > 0);
        Assert.Equal(1234, BitConverter.ToInt16(buffer, 0)); // centi-watt

        fs.File("/sys/class/power_supply/BAT0/status", "Charging");
        Assert.Equal(0, backend.DeviceGetBuffer(AsusACPI.BatteryDischarge)[2]);
    }

    // ------------------------------------------------------------------ fans

    [Fact]
    public void FanSpeedIsReportedInHundredsOfRpm()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/class/hwmon/hwmon3/name", "asus");
        fs.File("/sys/class/hwmon/hwmon3/fan1_input", "4200");
        fs.File("/sys/class/hwmon/hwmon3/fan2_input", "3100");
        using var backend = Create(fs);

        Assert.Equal(42, backend.DeviceGet(AsusACPI.CPU_Fan));
        Assert.Equal(31, backend.DeviceGet(AsusACPI.GPU_Fan));
        Assert.True(backend.DeviceGet(AsusACPI.Mid_Fan) < 0);
    }

    private static void FanCurveHwmon(FakeSysfs fs, int pwmIndex, int[] temps, int[] pwms, string enable = "2")
    {
        const string dir = "/sys/class/hwmon/hwmon5";
        fs.File(dir + "/name", "asus_custom_fan_curve");
        fs.File($"{dir}/pwm{pwmIndex}_enable", enable);
        for (int i = 0; i < 8; i++)
        {
            fs.File($"{dir}/pwm{pwmIndex}_auto_point{i + 1}_temp", temps[i].ToString());
            fs.File($"{dir}/pwm{pwmIndex}_auto_point{i + 1}_pwm", pwms[i].ToString());
        }
    }

    [Fact]
    public void FanCurveIsWrittenAsPointsAndEnabled()
    {
        using var fs = new FakeSysfs();
        FanCurveHwmon(fs, 1, new[] { 30, 40, 50, 60, 70, 80, 90, 100 }, new int[8]);
        ProfileHandler(fs, "balanced");
        using var backend = Create(fs);

        byte[] curve = [30, 40, 50, 60, 70, 80, 90, 100, 0, 10, 20, 35, 50, 70, 90, 100];
        Assert.Equal(1, backend.DeviceSet(AsusACPI.DevsCPUFanCurve, curve, "FanCPU"));

        const string dir = "/sys/class/hwmon/hwmon5";
        Assert.Equal("30", fs.Read($"{dir}/pwm1_auto_point1_temp"));
        Assert.Equal("100", fs.Read($"{dir}/pwm1_auto_point8_temp"));
        Assert.Equal("0", fs.Read($"{dir}/pwm1_auto_point1_pwm"));
        Assert.Equal("26", fs.Read($"{dir}/pwm1_auto_point2_pwm"));   // 10 % -> ceil(25.5)
        Assert.Equal("255", fs.Read($"{dir}/pwm1_auto_point8_pwm"));
        Assert.Equal("1", fs.Read($"{dir}/pwm1_enable"));
    }

    [Fact]
    public void FanCurveRoundTripKeepsPercentages()
    {
        for (int percent = 0; percent <= 100; percent++)
        {
            int pwm = FanCurveAccess.PwmFromPercent(percent);
            Assert.InRange(pwm, 0, 255);
            Assert.Equal(percent, 100 * pwm / 255);              // what the kernel firmware call computes
            Assert.Equal(percent, FanCurveAccess.PercentFromPwm(pwm));
        }
    }

    [Fact]
    public void FanCurveIsReadBackAsWindowsBuffer()
    {
        using var fs = new FakeSysfs();
        FanCurveHwmon(fs, 2, new[] { 35, 45, 55, 65, 75, 85, 95, 100 }, new[] { 0, 26, 51, 89, 127, 178, 229, 255 });
        ProfileHandler(fs, "balanced");
        using var backend = Create(fs);

        byte[] curve = backend.DeviceGetBuffer(AsusACPI.DevsGPUFanCurve, 0);
        Assert.Equal(16, curve.Length);
        Assert.Equal(new byte[] { 35, 45, 55, 65, 75, 85, 95, 100 }, curve[..8]);
        Assert.Equal(new byte[] { 0, 10, 20, 35, 50, 70, 90, 100 }, curve[8..]);

        // factory defaults are reloaded through pwmN_enable = 3 (no hardware write)
        Assert.Equal("3", fs.Read("/sys/class/hwmon/hwmon5/pwm2_enable"));
    }

    [Fact]
    public void FanCurveOfAnotherProfileIsNotInventedFromTheActiveOne()
    {
        using var fs = new FakeSysfs();
        FanCurveHwmon(fs, 1, new[] { 30, 40, 50, 60, 70, 80, 90, 100 }, new[] { 0, 26, 51, 89, 127, 178, 229, 255 });
        ProfileHandler(fs, "balanced");
        using var backend = Create(fs);

        // fan_mode 2 = turbo: the kernel only exposes the active (balanced) profile
        byte[] turbo = backend.DeviceGetBuffer(AsusACPI.DevsCPUFanCurve, 2);
        Assert.All(turbo, b => Assert.Equal(0, b));
        Assert.True(AsusACPI.IsInvalidCurve(turbo));

        byte[] balanced = backend.DeviceGetBuffer(AsusACPI.DevsCPUFanCurve, 0);
        Assert.False(AsusACPI.IsInvalidCurve(balanced));
    }

    // ------------------------------------------------------------------ keyboard

    [Fact]
    public void KeyboardBacklightUsesTheLedClass()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/class/leds/asus::kbd_backlight/brightness", "2");
        using var backend = Create(fs);

        Assert.Equal(0x82, backend.DeviceGet(AsusACPI.TUF_KB_BRIGHTNESS));
        backend.TUFBrightness(3);
        Assert.Equal("3", fs.Read("/sys/class/leds/asus::kbd_backlight/brightness"));
    }

    [Fact]
    public void TufRgbModeIsWrittenToKbdRgbMode()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/class/leds/asus::kbd_backlight/brightness", "1");
        fs.File("/sys/class/leds/asus::kbd_backlight/kbd_rgb_mode", "");
        using var backend = Create(fs);

        byte[] payload = [0xB4, 0x02, 10, 20, 30, 0xF5];
        Assert.Equal(1, backend.DeviceSet(AsusACPI.TUF_KB, payload, "TUF RGB"));
        Assert.Equal("1 2 10 20 30 2", fs.Read("/sys/class/leds/asus::kbd_backlight/kbd_rgb_mode"));

        payload[0] = 0xB3;
        payload[5] = 0xE1;
        Assert.Equal(1, backend.DeviceSet(AsusACPI.TUF_KB2, payload, null));
        Assert.Equal("0 2 10 20 30 0", fs.Read("/sys/class/leds/asus::kbd_backlight/kbd_rgb_mode"));
    }

    [Fact]
    public void TufKeyboardPowerStateIsDecoded()
    {
        using var fs = new FakeSysfs();
        fs.File("/sys/class/leds/asus::kbd_backlight/brightness", "1");
        fs.File("/sys/class/leds/asus::kbd_backlight/kbd_rgb_state", "");
        using var backend = Create(fs);

        // boot + awake + sleep, cmd flag set: 0xbd | 1 << 8 | (0x03 | 0x0C | 0x30) << 16
        int state = 0xBD | (0x01 << 8) | ((0x03 | 0x0C | 0x30) << 16);
        Assert.Equal(1, backend.DeviceSet(AsusACPI.TUF_KB_STATE, state, null));
        Assert.Equal("1 1 1 1 0", fs.Read("/sys/class/leds/asus::kbd_backlight/kbd_rgb_state"));
    }

    // ------------------------------------------------------------------ debugfs fallback

    [Fact]
    public void UnmappedIdsUseDebugfs()
    {
        using var fs = new FakeSysfs();
        const string dbg = "/sys/kernel/debug/asus-nb-wmi";
        fs.File(dbg + "/dev_id", "0x0");
        fs.File(dbg + "/ctrl_param", "0x0");
        fs.File(dbg + "/dsts", "DSTS(0x1200a1) = 0x10028\n");
        fs.File(dbg + "/devs", "DEVS(0x1200a1, 0x30) = 0x1\n");
        using var backend = Create(fs);

        Assert.True(backend.IsConnected);
        Assert.Equal(0x28, backend.DeviceGet(AsusACPI.PPT_EDCA1));
        Assert.Equal("0x1200a1", fs.Read(dbg + "/dev_id"));

        Assert.Equal(1, backend.DeviceSet(AsusACPI.PPT_TDCA2, 0x30, null));
        Assert.Equal("0x1200a2", fs.Read(dbg + "/dev_id"));
        Assert.Equal("0x30", fs.Read(dbg + "/ctrl_param"));
    }

    [Fact]
    public void DebugfsReportsUnsupportedIdsAsNegative()
    {
        using var fs = new FakeSysfs();
        const string dbg = "/sys/kernel/debug/asus-nb-wmi";
        fs.File(dbg + "/dev_id", "0x0").File(dbg + "/ctrl_param", "0x0");
        fs.File(dbg + "/dsts", "DSTS(0x1) = 0xfffffffe\n");
        using var backend = Create(fs);

        Assert.True(backend.DeviceGet(AsusACPI.PPT_CROSS9F) < 0);
    }

    [Fact]
    public void DebugfsFallbackCanBeDisabled()
    {
        using var fs = new FakeSysfs();
        const string dbg = "/sys/kernel/debug/asus-nb-wmi";
        fs.File(dbg + "/dev_id", "0x0").File(dbg + "/ctrl_param", "0x0").File(dbg + "/dsts", "DSTS(0x1) = 0x10005\n");
        using var backend = Create(fs, options: new LinuxAsusBackendOptions { UseDebugfsFallback = false, EnableEventListener = false });

        Assert.True(backend.DeviceGet(AsusACPI.PPT_CROSS9F) < 0);
    }

    [Fact]
    public void DebugfsResultParser()
    {
        Assert.Equal(0x10001u, AsusDebugfs.ParseResult("DSTS(0x120075) = 0x10001"));
        Assert.Equal(1u, AsusDebugfs.ParseResult("DEVS(0x120075, 0x1) = 0x1\n"));
        Assert.Equal(0xfffffffeu, AsusDebugfs.ParseResult("DSTS(0x1) = 0xfffffffe"));
        Assert.Null(AsusDebugfs.ParseResult(""));
        Assert.Null(AsusDebugfs.ParseResult("garbage"));
    }
}

/// <summary>Small helper keeping the TUF brightness call readable in tests.</summary>
internal static class BackendTestExtensions
{
    public static int TUFBrightness(this LinuxAsusBackend backend, int brightness)
        => backend.DeviceSet(AsusACPI.TUF_KB_BRIGHTNESS, 0x80 | (brightness & 0x7F), "TUF Backlight");
}
