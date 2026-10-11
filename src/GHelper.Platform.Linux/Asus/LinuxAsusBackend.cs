namespace GHelper.Platform.Linux;

public sealed class LinuxAsusBackendOptions
{
    /// <summary>Forward hotkeys the desktop already handles (brightness, volume, kbd backlight, rfkill ...).</summary>
    public bool ForwardDesktopHandledKeys { get; set; }

    /// <summary>Use /sys/kernel/debug/asus-nb-wmi dev_id/ctrl_param/devs/dsts for ids without a sysfs attribute.</summary>
    public bool UseDebugfsFallback { get; set; } = true;

    /// <summary>Open the evdev nodes in <see cref="LinuxAsusBackend.StartEventListener"/>.</summary>
    public bool EnableEventListener { get; set; } = true;
}

/// <summary>
/// IAsusBackend on top of the mainline asus-wmi / asus-nb-wmi / asus-armoury drivers.
///
/// Values keep the *Windows convention* of the legacy ATKACPI interface so the AsusACPI facade needs no changes:
/// DeviceGet returns the DSTS value without the presence bit (negative = unsupported), DeviceSet returns 1 on success.
/// Each device id is translated to the sysfs attribute of the driver; ids without an attribute use the debugfs
/// dev_id/ctrl_param/dsts/devs files when readable and report "unsupported" otherwise.
/// </summary>
public sealed class LinuxAsusBackend : IAsusBackend
{
    private const uint PresenceBit = 0x10000;

    private sealed class Endpoint
    {
        public Func<int?>? Get;
        public Func<int, bool>? Set;
        public Func<byte[], bool>? SetBytes;
        public Func<uint, byte[]?>? GetBytes;
    }

    private readonly LinuxContext context;
    private readonly SysfsRoot root;
    private readonly LinuxAsusBackendOptions options;
    private readonly Dictionary<uint, Endpoint> endpoints = new();
    private readonly FanCurveAccess fanCurves;
    private AsusDebugfs? debugfs;
    private AtkEventSource? eventSource;
    private bool listening;
    private bool? miniLed2;
    private int lastCustomProfile;

    public event Action<int>? AtkEvent;

    public LinuxAsusBackend(LinuxContext context, LinuxAsusBackendOptions? options = null)
    {
        this.context = context;
        this.options = options ?? new LinuxAsusBackendOptions();
        root = context.Root;
        Layout = new AsusWmiLayout(root);
        if (Layout.DebugfsDir is not null && this.options.UseDebugfsFallback)
            debugfs = new AsusDebugfs(root, Layout.DebugfsDir);

        fanCurves = new FanCurveAccess(root, () => Layout.FanCurveHwmonDir, CurrentFanMode);
        BuildTable();
    }

    public AsusWmiLayout Layout { get; }

    public string Name => "asus-wmi (Linux sysfs)";

    public bool IsConnected => Layout.IsAsusWmiPresent || debugfs is not null;

    /// <summary>true when the id has a sysfs mapping on this kernel (not counting debugfs).</summary>
    public bool HasSysfsEndpoint(uint deviceId)
        => endpoints.TryGetValue(deviceId, out Endpoint? ep) && ep.Get is not null && ep.Get() is not null;

    // ------------------------------------------------------------------ IAsusBackend

    public int DeviceGet(uint deviceId)
    {
        if (endpoints.TryGetValue(deviceId, out Endpoint? ep) && ep.Get is not null)
        {
            try
            {
                int? value = ep.Get();
                if (value is not null) return value.Value;
            }
            catch (Exception ex)
            {
                LinuxLog.Write($"DSTS {deviceId:X8}: {ex.Message}");
            }
        }

        if (debugfs?.Dsts(deviceId) is uint raw)
            return unchecked((int)(raw - PresenceBit));

        return -1;
    }

    public byte[] DeviceGetBuffer(uint deviceId, uint status = 0) => GetBuffer(deviceId, status, 16);

    public byte[] DeviceGetLarge(uint deviceId, int extraIn = 8, int outSize = 64) => GetBuffer(deviceId, 0, Math.Max(16, outSize));

    private byte[] GetBuffer(uint deviceId, uint status, int size)
    {
        if (endpoints.TryGetValue(deviceId, out Endpoint? ep) && ep.GetBytes is not null)
        {
            try
            {
                byte[]? bytes = ep.GetBytes(status);
                if (bytes is not null) return Pad(bytes, size);
            }
            catch (Exception ex)
            {
                LinuxLog.Write($"DSTS buffer {deviceId:X8}: {ex.Message}");
            }
        }

        int value = DeviceGet(deviceId);
        var buffer = new byte[size];
        if (value >= 0) BitConverter.GetBytes(unchecked((uint)value + PresenceBit)).CopyTo(buffer, 0);
        return buffer;
    }

    public int DeviceSet(uint deviceId, int status, string? logName)
    {
        int result = SetInternal(deviceId, status);
        if (logName is not null) LinuxLog.Write($"{logName} = {status} : {(result == 1 ? "OK" : result)}");
        return result;
    }

    private int SetInternal(uint deviceId, int status)
    {
        if (endpoints.TryGetValue(deviceId, out Endpoint? ep) && ep.Set is not null)
        {
            try
            {
                // an endpoint that exists but whose attribute is missing falls through to debugfs
                if (ep.Get is null || ep.Get() is not null)
                    return ep.Set(status) ? 1 : -1;
            }
            catch (Exception ex)
            {
                LinuxLog.Write($"DEVS {deviceId:X8}: {ex.Message}");
                return -1;
            }
        }

        if (debugfs?.Devs(deviceId, unchecked((uint)status)) is uint raw) return unchecked((int)raw);
        return -1;
    }

    public int DeviceSet(uint deviceId, byte[] parameters, string? logName)
    {
        int result = -1;

        if (endpoints.TryGetValue(deviceId, out Endpoint? ep) && ep.SetBytes is not null)
        {
            try
            {
                if (ep.Get is null || ep.Get() is not null || ep.GetBytes is not null)
                    result = ep.SetBytes(parameters) ? 1 : -1;
            }
            catch (Exception ex)
            {
                LinuxLog.Write($"DEVS {deviceId:X8}: {ex.Message}");
            }
        }
        else if (debugfs is not null && parameters.Length <= 4)
        {
            // the kernel debugfs interface carries a single 32 bit argument
            Span<byte> padded = stackalloc byte[4];
            parameters.CopyTo(padded);
            uint arg = BitConverter.ToUInt32(padded);
            if (debugfs.Devs(deviceId, arg) is uint raw) result = unchecked((int)raw);
        }

        if (logName is not null) LinuxLog.Write($"{logName} = {BitConverter.ToString(parameters)} : {(result == 1 ? "OK" : result)}");
        return result;
    }

    public int DeviceSetAlternative(uint deviceId, int status) => DeviceSet(deviceId, status, null);

    public byte[] DeviceInit() => new byte[16];

    public byte[] DeviceWatchDog() => new byte[16];

    public void StartEventListener()
    {
        if (listening || !options.EnableEventListener) return;
        listening = true;

        eventSource = new AtkEventSource(root, id => AtkEvent?.Invoke(id), options.ForwardDesktopHandledKeys);
        eventSource.Start();
    }

    public void Dispose()
    {
        eventSource?.Dispose();
        eventSource = null;
    }

    // ------------------------------------------------------------------ table

    private void Reg(uint id, Endpoint endpoint) => endpoints[id] = endpoint;

    private void Reg(int id, Endpoint endpoint) => endpoints[unchecked((uint)id)] = endpoint;

    private void BuildTable()
    {
        // --- performance mode / thermal policy ------------------------------------------------
        Reg(AsusACPI.PerformanceMode, PerformanceEndpoint(vivo: false));
        Reg(AsusACPI.VivoBookMode, PerformanceEndpoint(vivo: true));

        // --- fans -----------------------------------------------------------------------------
        Reg(AsusACPI.CPU_Fan, FanSpeedEndpoint(1));
        Reg(AsusACPI.GPU_Fan, FanSpeedEndpoint(2));
        Reg(AsusACPI.Mid_Fan, FanSpeedEndpoint(3));

        Reg(AsusACPI.DevsCPUFanCurve, FanCurveEndpoint(AsusFanIndex.Cpu));
        Reg(AsusACPI.DevsGPUFanCurve, FanCurveEndpoint(AsusFanIndex.Gpu));
        Reg(AsusACPI.DevsMidFanCurve, FanCurveEndpoint(AsusFanIndex.Mid));

        // --- PPT / GPU boost (asus-armoury first, deprecated platform attributes second) -----
        Reg(AsusACPI.PPT_APUA3, IntAttr("ppt_pl1_spl", "ppt_pl1_spl"));
        Reg(AsusACPI.PPT_APUA0, IntAttr("ppt_pl2_sppt", "ppt_pl2_sppt"));
        Reg(AsusACPI.PPT_APUC1, IntAttr("ppt_pl3_fppt", "ppt_fppt"));
        Reg(AsusACPI.PPT_CPUB0, IntAttr("ppt_apu_sppt", "ppt_apu_sppt"));
        Reg(AsusACPI.PPT_CPUB1, IntAttr("ppt_platform_sppt", "ppt_platform_sppt"));
        Reg(AsusACPI.PPT_GPUC0, IntAttr("nv_dynamic_boost", "nv_dynamic_boost"));
        Reg(AsusACPI.PPT_GPUC2, IntAttr("nv_temp_target", "nv_temp_target"));
        Reg(AsusACPI.GPU_POWER, IntAttr("nv_tgp", null));
        Reg(AsusACPI.GPU_BASE, IntAttr("nv_base_tgp", null, writable: false));

        // --- GPU mode -----------------------------------------------------------------------
        Reg(AsusACPI.GPUEcoROG, IntAttr("dgpu_disable", "dgpu_disable"));
        Reg(AsusACPI.GPUMuxROG, IntAttr("gpu_mux_mode", "gpu_mux_mode"));
        Reg(AsusACPI.GPUMuxVivo, IntAttr("gpu_mux_mode", "gpu_mux_mode"));
        Reg(AsusACPI.GPUXGConnected, IntAttr("egpu_connected", "egpu_connected", writable: false));
        Reg(AsusACPI.GPUXG, EgpuEndpoint());
        Reg(AsusACPI.APU_MEM, ApuMemEndpoint());

        // --- screen -------------------------------------------------------------------------
        Reg(AsusACPI.ScreenOverdrive, IntAttr("panel_overdrive", "panel_od"));
        Reg(AsusACPI.ScreenOverdriveSupport, OverdriveSupportEndpoint());
        Reg(AsusACPI.ScreenMiniled1, MiniLedEndpoint(second: false));
        Reg(AsusACPI.ScreenMiniled2, MiniLedEndpoint(second: true));
        Reg(AsusACPI.ScreenFHD, IntAttr("panel_hd_mode", null));
        Reg(AsusACPI.ScreenOptimalBrightness, IntAttr("screen_auto_brightness", null));
        Reg(AsusACPI.ScreenPadBrightness, BacklightEndpoint(() => Layout.ScreenpadBacklightDir));

        // --- battery / charger ----------------------------------------------------------------
        Reg(AsusACPI.BatteryLimit, BatteryLimitEndpoint());
        Reg(AsusACPI.ChargerMode, IntAttr("charge_mode", "charge_mode", writable: false));
        Reg(AsusACPI.BatteryDischarge, BatteryDischargeEndpoint());
        Reg(AsusACPI.BootSound, IntAttr("boot_sound", "boot_sound"));

        // --- keyboard / LEDs -----------------------------------------------------------------
        Reg(AsusACPI.TUF_KB_BRIGHTNESS, KeyboardBacklightEndpoint());
        Reg(AsusACPI.TUF_KB, TufRgbEndpoint());
        Reg(AsusACPI.TUF_KB2, TufRgbEndpoint());
        Reg(AsusACPI.TUF_KB_STATE, TufStateEndpoint());
        Reg(AsusACPI.MicMuteLed, LedEndpoint("/sys/class/leds/platform::micmute"));
        Reg(AsusACPI.CameraLed, LedEndpoint("/sys/class/leds/asus::camera"));
    }

    private enum AsusFanIndex { Cpu = 1, Gpu = 2, Mid = 3 }

    // ------------------------------------------------------------------ generic endpoints

    /// <summary>Integer attribute of asus-armoury (preferred) or of the deprecated platform device directory.</summary>
    private Endpoint IntAttr(string armouryName, string? platformName, bool writable = true)
    {
        string? Path() => Layout.Armoury(armouryName) ?? (platformName is null ? null : Layout.Platform(platformName));

        return new Endpoint
        {
            Get = () => Path() is { } p ? root.ReadInt(p) : null,
            Set = writable ? v => Path() is { } p && root.TryWrite(p, v.ToString()) : null,
        };
    }

    private Endpoint BacklightEndpoint(Func<string?> dir) => new()
    {
        Get = () => dir() is { } d ? root.ReadInt(d + "/brightness") : null,
        Set = v => dir() is { } d && root.TryWrite(d + "/brightness", Math.Max(0, v).ToString()),
    };

    private Endpoint LedEndpoint(string dir) => new()
    {
        Get = () => root.DirectoryExists(dir) ? root.ReadInt(dir + "/brightness") : null,
        Set = v => root.DirectoryExists(dir) && root.TryWrite(dir + "/brightness", Math.Max(0, v).ToString()),
    };

    // ------------------------------------------------------------------ performance mode

    // ASUS throttle policy numbering used by the kernel: 0 default(balanced), 1 overboost(performance), 2 silent(quiet).
    // Vivobook firmware swaps 1 and 2 (the kernel driver hides that when it runs the throttle_thermal_policy_dev).
    private static int SwapOneTwo(int v) => v == 1 ? 2 : v == 2 ? 1 : v;

    private static readonly string[][] ProfileNames =
    [
        ["balanced"],
        ["performance", "balanced-performance", "max-power"],
        ["quiet", "low-power", "cool"],
    ];

    /// <summary>Kernel numbering (0 balanced, 1 performance, 2 quiet) of the active profile, null if unknown.</summary>
    private int? ReadKernelProfile()
    {
        if (Layout.ProfileFile is { } file)
        {
            string? text = root.ReadText(file);
            if (text is null) return null;
            switch (text)
            {
                case "balanced": lastCustomProfile = 0; return 0;
                case "performance":
                case "balanced-performance":
                case "max-power": lastCustomProfile = 1; return 1;
                case "quiet":
                case "low-power":
                case "cool": lastCustomProfile = 2; return 2;
                case "custom": return lastCustomProfile;
                default: return null;
            }
        }

        if (Layout.Platform("throttle_thermal_policy") is { } policy)
            return root.ReadInt(policy);

        return null;
    }

    private bool WriteKernelProfile(int kernelMode)
    {
        if (Layout.ProfileFile is { } file)
        {
            string[] choices = (root.ReadText(Layout.ProfileChoicesFile ?? "") ?? "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (string candidate in ProfileNames[kernelMode])
            {
                if (choices.Length == 0 || choices.Contains(candidate))
                    return root.TryWrite(file, candidate);
            }
            return false;
        }

        if (Layout.Platform("throttle_thermal_policy") is { } policy)
            return root.TryWrite(policy, kernelMode.ToString());

        return false;
    }

    /// <summary>Current profile in the "ASUS swapped" numbering FanCurveAccess / GetFanCurve use (1 silent, 2 turbo).</summary>
    private int CurrentFanMode() => SwapOneTwo(ReadKernelProfile() ?? 0);

    private Endpoint PerformanceEndpoint(bool vivo) => new()
    {
        Get = () =>
        {
            int? kernel = ReadKernelProfile();
            if (kernel is null) return null;
            return vivo ? SwapOneTwo(kernel.Value) : kernel.Value;
        },
        Set = v =>
        {
            if (vivo) v = SwapOneTwo(v);
            switch (v)
            {
                case 0:
                case 1:
                case 2:
                    return WriteKernelProfile(v);
                case 3: // "full speed": closest kernel equivalent
                    return WriteKernelProfile(1);
                case 4: // "manual": the kernel has no such mode, keep the profile and let PPT / fan curves decide
                    return true;
                default:
                    return false;
            }
        },
    };

    // ------------------------------------------------------------------ fans

    private Endpoint FanSpeedEndpoint(int index) => new()
    {
        // hwmon fanN_input is RPM, the ACPI value (and the Windows facade) is RPM / 100
        Get = () =>
        {
            string? dir = Layout.FanHwmonDir;
            if (dir is null) return null;
            int? rpm = root.ReadInt($"{dir}/fan{index}_input");
            return rpm is null ? null : rpm.Value / 100;
        },
    };

    private Endpoint FanCurveEndpoint(AsusFanIndex fan)
    {
        int index = (int)fan;
        return new Endpoint
        {
            Get = () => fanCurves.IsPresent(index) ? 1 : null,
            GetBytes = mode =>
            {
                if (!fanCurves.IsPresent(index)) return null;
                // legacy layout: the DSTS output of a curve id is exactly the 16 curve bytes (8 temps, 8 percents);
                // an all-zero buffer is what the Windows facade treats as "no curve known for this mode"
                return fanCurves.Read(index, (int)mode) ?? new byte[16];
            },
            SetBytes = curve => fanCurves.IsPresent(index) && fanCurves.Write(index, curve),
        };
    }

    // ------------------------------------------------------------------ GPU

    private static readonly int[] EgpuIndexToRaw = [0x0, 0x1, 0x101, 0x201];

    private Endpoint EgpuEndpoint() => new()
    {
        Get = () =>
        {
            if (Layout.Armoury("egpu_enable") is { } armoury)
            {
                int? index = root.ReadInt(armoury);
                return index is >= 0 && index < EgpuIndexToRaw.Length ? EgpuIndexToRaw[index.Value] : index;
            }
            return Layout.Platform("egpu_enable") is { } p ? root.ReadInt(p) : null;
        },
        Set = v =>
        {
            if (Layout.Armoury("egpu_enable") is { } armoury)
            {
                int index = Array.IndexOf(EgpuIndexToRaw, v);
                return index >= 0 && root.TryWrite(armoury, index.ToString());
            }
            return Layout.Platform("egpu_enable") is { } p && root.TryWrite(p, v == 0 ? "0" : "1");
        },
    };

    // armoury apu_mem_map: index -> raw WMI value (identical to the legacy apuMemEnum | 0x100 table)
    private static readonly int[] ApuMemIndexToRaw = [0x000, 0x102, 0x103, 0x104, 0x105, 0x107, 0x108, 0x109, 0x106];

    private Endpoint ApuMemEndpoint() => new()
    {
        Get = () =>
        {
            if (Layout.Armoury("apu_mem") is not { } p) return null;
            int? index = root.ReadInt(p);
            return index is >= 0 && index < ApuMemIndexToRaw.Length ? ApuMemIndexToRaw[index.Value] : null;
        },
        Set = v =>
        {
            if (Layout.Armoury("apu_mem") is not { } p) return false;
            int index = Array.IndexOf(ApuMemIndexToRaw, v);
            return index >= 0 && root.TryWrite(p, index.ToString());
        },
    };

    // ------------------------------------------------------------------ screen

    private Endpoint OverdriveSupportEndpoint() => new()
    {
        Get = () => Layout.Armoury("panel_overdrive") is not null || Layout.Platform("panel_od") is not null ? 1 : null,
    };

    private bool IsMiniLed2()
    {
        if (miniLed2 is not null) return miniLed2.Value;

        string? values = Layout.Armoury("mini_led_mode", "possible_values") is { } p ? root.ReadText(p) : null;
        values ??= Layout.Platform("available_mini_led_mode") is { } q ? root.ReadText(q) : null;

        if (values is null) return false; // not decided yet, retry on the next call

        int count = values.Split([';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
        miniLed2 = count >= 3;
        return miniLed2.Value;
    }

    // MINI_LED_MODE2 raw WMI values: 0 weak, 1 strong, 2 off. The kernel attribute index is 0 off, 1 weak, 2 strong
    private static readonly int[] MiniLed2IndexToRaw = [2, 0, 1];

    private Endpoint MiniLedEndpoint(bool second)
    {
        string? Path() => Layout.Armoury("mini_led_mode") ?? Layout.Platform("mini_led_mode");

        return new Endpoint
        {
            Get = () =>
            {
                if (Path() is not { } p) return null;
                int? index = root.ReadInt(p);
                if (index is null) return null;

                bool mode2 = IsMiniLed2();
                if (mode2 != second) return null; // the other id is the one this panel answers to
                return mode2 ? (index.Value is >= 0 and < 3 ? MiniLed2IndexToRaw[index.Value] : null) : index;
            },
            Set = v =>
            {
                if (Path() is not { } p) return false;
                if (IsMiniLed2() != second) return false;

                if (!second) return root.TryWrite(p, v.ToString());

                int index = Array.IndexOf(MiniLed2IndexToRaw, v);
                return index >= 0 && root.TryWrite(p, index.ToString());
            },
        };
    }

    // ------------------------------------------------------------------ battery

    private Endpoint BatteryLimitEndpoint() => new()
    {
        Get = () => Layout.BatteryDir is { } d ? root.ReadInt(d + "/charge_control_end_threshold") : null,
        Set = v =>
        {
            if (Layout.BatteryDir is not { } d) return false;
            if (v < 0 || v > 100) return false;
            return root.TryWrite(d + "/charge_control_end_threshold", v.ToString());
        },
    };

    private Endpoint BatteryDischargeEndpoint() => new()
    {
        // legacy buffer: int16 centi-watts, byte 2 = "valid" flag (the DSTS presence bit)
        GetBytes = _ =>
        {
            string? battery = PowerSupplyInfo.FindBattery(root);
            if (battery is null) return null;
            if (!string.Equals(root.ReadText(battery + "/status"), "Discharging", StringComparison.OrdinalIgnoreCase))
                return null;

            double? watts = PowerSupplyInfo.ReadPowerWatts(root, battery);
            if (watts is null) return null;

            var buffer = new byte[16];
            BitConverter.GetBytes((short)Math.Clamp((int)Math.Round(watts.Value * 100), 0, short.MaxValue)).CopyTo(buffer, 0);
            buffer[2] = 1;
            return buffer;
        },
    };

    // ------------------------------------------------------------------ keyboard

    private string? KeyboardLed => Layout.KeyboardLedDir;

    private Endpoint KeyboardBacklightEndpoint() => new()
    {
        // DSTS: 0x80 | brightness; DEVS param: 0x80 | brightness
        Get = () =>
        {
            if (KeyboardLed is not { } d) return null;
            int? brightness = root.ReadInt(d + "/brightness");
            return brightness is null ? null : 0x80 | (brightness.Value & 0x7F);
        },
        Set = v =>
        {
            if (KeyboardLed is not { } d) return false;
            return root.TryWrite(d + "/brightness", (v & 0x7F).ToString());
        },
    };

    private static readonly byte[] TufSpeedBytes = [0xE1, 0xEB, 0xF5];

    private Endpoint TufRgbEndpoint() => new()
    {
        Get = () => KeyboardLed is { } d && root.FileExists(d + "/kbd_rgb_mode") ? 1 : null,
        // payload as built by AsusACPI.TUFKeyboardRGB: [cmd(0xB3 set / 0xB4 save), mode, R, G, B, speed(0xE1/0xEB/0xF5)]
        SetBytes = payload =>
        {
            if (KeyboardLed is not { } d || payload.Length < 6) return false;
            string file = d + "/kbd_rgb_mode";
            if (!root.FileExists(file)) return false;

            int cmd = payload[0] == 0xB4 ? 1 : 0;
            int speed = Array.IndexOf(TufSpeedBytes, payload[5]);
            if (speed < 0) speed = 1;
            return root.TryWrite(file, $"{cmd} {payload[1]} {payload[2]} {payload[3]} {payload[4]} {speed}");
        },
    };

    private Endpoint TufStateEndpoint() => new()
    {
        Get = () => KeyboardLed is { } d && root.FileExists(d + "/kbd_rgb_state") ? 1 : null,
        // value as built by AsusACPI.TUFKeyboardPower: 0xBD | cmd << 8 | flags << 16 (flag pairs: boot 0x03, awake 0x0C, sleep 0x30, shutdown 0xC0)
        Set = state =>
        {
            if (KeyboardLed is not { } d) return false;
            string file = d + "/kbd_rgb_state";
            if (!root.FileExists(file)) return false;

            int flags = (state >> 16) & 0xFF;
            int cmd = (state >> 8) & 0xFF;
            int boot = (flags & 0x03) != 0 ? 1 : 0;
            int awake = (flags & 0x0C) != 0 ? 1 : 0;
            int sleep = (flags & 0x30) != 0 ? 1 : 0;
            int keyboard = (flags & 0xC0) != 0 ? 1 : 0;
            return root.TryWrite(file, $"{(cmd != 0 ? 1 : 0)} {boot} {awake} {sleep} {keyboard}");
        },
    };

    private static byte[] Pad(byte[] data, int size)
    {
        if (data.Length >= size) return data;
        var padded = new byte[size];
        data.CopyTo(padded, 0);
        return padded;
    }
}
