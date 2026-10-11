using GHelper.Input;

namespace GHelper.Platform.Linux;

/// <summary>
/// /dev/uinput virtual keyboard used to inject key presses. Created on first use. Requires write access to
/// /dev/uinput (udev rule in packaging/linux, group "ghelper" or the uaccess tag).
/// </summary>
internal sealed unsafe class UinputDevice : IDisposable
{
    private const nuint UI_DEV_CREATE = 0x5501;
    private const nuint UI_DEV_DESTROY = 0x5502;
    private const nuint UI_DEV_SETUP = 0x405C5503;
    private const nuint UI_SET_EVBIT = 0x40045564;
    private const nuint UI_SET_KEYBIT = 0x40045565;
    private const nuint UI_SET_RELBIT = 0x40045566;
    private const ushort BUS_VIRTUAL = 0x06;

    public const string DeviceName = "G-Helper virtual input";

    private int fd = -1;
    private readonly object gate = new();

    public static UinputDevice? TryCreate(string path = "/dev/uinput")
    {
        try
        {
            int fd = Native.open(path, Native.O_WRONLY | Native.O_NONBLOCK | Native.O_CLOEXEC);
            if (fd < 0) return null;

            var device = new UinputDevice { fd = fd };
            if (!device.Setup())
            {
                device.Dispose();
                return null;
            }
            return device;
        }
        catch (Exception ex)
        {
            LinuxLog.Write("uinput: " + ex.Message);
            return null;
        }
    }

    private bool Setup()
    {
        if (Native.ioctl(fd, UI_SET_EVBIT, EvCodes.EV_KEY) < 0) return false;
        if (Native.ioctl(fd, UI_SET_EVBIT, EvCodes.EV_SYN) < 0) return false;
        Native.ioctl(fd, UI_SET_EVBIT, EvCodes.EV_REL);
        Native.ioctl(fd, UI_SET_RELBIT, EvCodes.REL_WHEEL);

        for (int code = 1; code < 256; code++)
            Native.ioctl(fd, UI_SET_KEYBIT, code);

        // struct uinput_setup { struct input_id {u16 bustype, vendor, product, version}; char name[80]; u32 ff_effects_max; }
        byte* setup = stackalloc byte[92];
        new Span<byte>(setup, 92).Clear();
        *(ushort*)(setup + 0) = BUS_VIRTUAL;
        *(ushort*)(setup + 2) = 0x0B05;
        *(ushort*)(setup + 4) = 0x1;
        *(ushort*)(setup + 6) = 1;
        for (int i = 0; i < DeviceName.Length && i < 79; i++) setup[8 + i] = (byte)DeviceName[i];

        if (Native.ioctl(fd, UI_DEV_SETUP, setup) < 0) return false;
        if (Native.ioctl(fd, UI_DEV_CREATE, 0) < 0) return false;

        // let udev / the compositor pick the new device up before the first event
        Thread.Sleep(150);
        return true;
    }

    private bool Emit(ushort type, ushort code, int value)
    {
        byte[] data = EvdevReader.Encode(type, code, value);
        fixed (byte* p = data)
        {
            return Native.write(fd, p, (nuint)data.Length) == data.Length;
        }
    }

    public void Key(int code, bool down)
    {
        lock (gate)
        {
            Emit(EvCodes.EV_KEY, (ushort)code, down ? 1 : 0);
            Emit(EvCodes.EV_SYN, EvCodes.SYN_REPORT, 0);
        }
    }

    public void Wheel(int notches)
    {
        lock (gate)
        {
            Emit(EvCodes.EV_REL, EvCodes.REL_WHEEL, notches);
            Emit(EvCodes.EV_SYN, EvCodes.SYN_REPORT, 0);
        }
    }

    public void Dispose()
    {
        int old = Interlocked.Exchange(ref fd, -1);
        if (old < 0) return;
        try
        {
            Native.ioctl(old, UI_DEV_DESTROY, 0);
            Native.close(old);
        }
        catch { }
    }
}

/// <summary>
/// IInputService for Linux: uinput injection, evdev based hotkeys and key observation (works on X11 and Wayland, needs
/// read access to /dev/input/event* i.e. the "input" group or the udev rule shipped in packaging/linux).
/// </summary>
public sealed class LinuxInputService : IInputService
{
    private readonly LinuxContext ctx;
    private readonly object gate = new();
    private UinputDevice? uinput;
    private bool uinputFailed;

    private EvdevMonitor? monitor;
    private readonly List<Subscription> subscriptions = new();
    private HotkeyModifiers heldModifiers;

    private sealed class Subscription
    {
        public Action<Keys, bool>? KeyHandler;
        public HotkeyModifiers Modifiers;
        public Keys Key;
        public Action? Hotkey;
    }

    public LinuxInputService(LinuxContext ctx)
    {
        this.ctx = ctx;
    }

    // ------------------------------------------------------------------ injection

    private UinputDevice? Device()
    {
        lock (gate)
        {
            if (uinput is not null || uinputFailed) return uinput;
            uinput = UinputDevice.TryCreate(ctx.Root.Map("/dev/uinput"));
            if (uinput is null)
            {
                uinputFailed = true;
                LinuxLog.Write("/dev/uinput not accessible: key injection disabled (see packaging/linux/99-ghelper.rules)");
            }
            return uinput;
        }
    }

    private void Press(IReadOnlyList<Keys> keys, int holdMs, int gapMs)
    {
        UinputDevice? device = Device();
        if (device is null) return;

        var codes = new List<int>();
        foreach (Keys k in keys)
        {
            if (!LinuxKeyMap.TryGetEvdev(k, out int code))
            {
                LinuxLog.Write($"No evdev mapping for {k}");
                return;
            }
            codes.Add(code);
        }

        foreach (int code in codes)
        {
            device.Key(code, true);
            if (gapMs > 0) Thread.Sleep(gapMs);
        }
        if (holdMs > 0) Thread.Sleep(holdMs);
        for (int i = codes.Count - 1; i >= 0; i--)
        {
            device.Key(codes[i], false);
            if (gapMs > 0) Thread.Sleep(gapMs);
        }
    }

    public void KeyPress(Keys key) => Press([key], 1, 0);

    public void KeyKeyPress(Keys key, Keys key2) => Press([key, key2], 1, 0);

    public void KeyKeyKeyPress(Keys key, Keys key2, Keys key3, int sleep = 1, int interSleep = 0)
        => Press([key, key2, key3], sleep, interSleep);

    public void KeyKeyKeyKeyPress(Keys key, Keys key2, Keys key3, Keys key4, int sleep = 1)
        => Press([key, key2, key3, key4], sleep, 0);

    public void MouseWheel(int delta)
    {
        UinputDevice? device = Device();
        if (device is null) return;
        // Windows reports 120 units per notch
        int notches = delta / 120;
        if (notches == 0 && delta != 0) notches = delta > 0 ? 1 : -1;
        device.Wheel(notches);
    }

    // ------------------------------------------------------------------ observation / hotkeys

    private void EnsureMonitor()
    {
        lock (gate)
        {
            if (monitor is not null) return;
            monitor = new EvdevMonitor(ctx.Root,
                info => info.IsKeyboard && info.Name != UinputDevice.DeviceName,
                OnEvent);
            monitor.Start();
        }
    }

    private void OnEvent(InputDeviceInfo device, InputEvent e)
    {
        if (e.Type != EvCodes.EV_KEY || e.Value == 2) return;
        bool down = e.Value == 1;

        HotkeyModifiers modifier = LinuxKeyMap.ModifierOf(e.Code);
        HotkeyModifiers held;
        lock (gate)
        {
            if (modifier != HotkeyModifiers.None)
            {
                if (down) heldModifiers |= modifier;
                else heldModifiers &= ~modifier;
            }
            held = heldModifiers;
        }

        if (!LinuxKeyMap.TryGetKey(e.Code, out Keys key)) return;

        Subscription[] snapshot;
        lock (gate) snapshot = subscriptions.ToArray();

        foreach (Subscription s in snapshot)
        {
            try
            {
                if (s.KeyHandler is not null) s.KeyHandler(key, down);
                else if (down && s.Hotkey is not null && s.Key == key && s.Modifiers == held)
                    ThreadPool.QueueUserWorkItem(_ => s.Hotkey());
            }
            catch (Exception ex)
            {
                LinuxLog.Write("key handler: " + ex.Message);
            }
        }
    }

    private IDisposable Subscribe(Subscription subscription)
    {
        lock (gate) subscriptions.Add(subscription);
        EnsureMonitor();
        return new Unsubscriber(() =>
        {
            lock (gate) subscriptions.Remove(subscription);
        });
    }

    public IDisposable RegisterHotkey(HotkeyModifiers modifiers, Keys key, Action handler)
        => Subscribe(new Subscription { Modifiers = modifiers, Key = key & Keys.KeyCode, Hotkey = handler });

    public IDisposable ListenKeys(Action<Keys, bool> handler)
        => Subscribe(new Subscription { KeyHandler = handler });

    private sealed class Unsubscriber(Action action) : IDisposable
    {
        private Action? action = action;

        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }

    // ------------------------------------------------------------------ on screen keyboard

    private bool IsGnome => ctx.CurrentDesktop.Contains("GNOME", StringComparison.Ordinal)
                            && ctx.Runner.CommandExists("gsettings");

    public bool OnScreenKeyboardSupported => IsGnome || ctx.Runner.CommandExists("onboard");

    public void ToggleOnScreenKeyboard()
    {
        if (IsGnome)
        {
            bool open = IsOnScreenKeyboardOpen();
            ctx.Runner.Run("gsettings",
                ["set", "org.gnome.desktop.a11y.applications", "screen-keyboard-enabled", open ? "false" : "true"], 3000);
            return;
        }

        if (!ctx.Runner.CommandExists("onboard")) return;

        List<int> running = ProcFs.FindByName(ctx.Root, "onboard");
        if (running.Count > 0)
        {
            foreach (int pid in running)
            {
                try { System.Diagnostics.Process.GetProcessById(pid).Kill(); } catch { }
            }
        }
        else
        {
            ctx.Runner.StartDetached("onboard", []);
        }
    }

    public bool IsOnScreenKeyboardOpen()
    {
        if (IsGnome)
        {
            CommandResult r = ctx.Runner.Run("gsettings",
                ["get", "org.gnome.desktop.a11y.applications", "screen-keyboard-enabled"], 3000);
            return r.Success && r.StdOut.Trim() == "true";
        }
        return ProcFs.IsRunning(ctx.Root, "onboard");
    }

    public void Dispose()
    {
        lock (gate)
        {
            monitor?.Dispose();
            monitor = null;
            subscriptions.Clear();
            uinput?.Dispose();
            uinput = null;
        }
    }
}
