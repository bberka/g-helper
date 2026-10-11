using System.Collections.Concurrent;
using GHelper.Platform.Linux;
using Xunit;

namespace GHelper.Tests.Linux;

public class LinuxEventTests
{
    private const string Devices = """
        I: Bus=0019 Vendor=0000 Product=0001 Version=0000
        N: Name="Power Button"
        P: Phys=PNP0C0C/button/input0
        H: Handlers=kbd event0
        B: PROP=0

        I: Bus=0019 Vendor=0001 Product=0001 Version=0000
        N: Name="Asus WMI hotkeys"
        P: Phys=asus-nb-wmi/input0
        S: Sysfs=/devices/platform/asus-nb-wmi/input/input12
        H: Handlers=rfkill kbd event7
        B: PROP=0

        I: Bus=0003 Vendor=0b05 Product=19b6 Version=0110
        N: Name="Asus Keyboard"
        P: Phys=usb-0000:07:00.3-3/input2
        H: Handlers=sysrq kbd event9 leds
        B: PROP=0

        I: Bus=0011 Vendor=0001 Product=0001 Version=ab41
        N: Name="AT Translated Set 2 keyboard"
        H: Handlers=sysrq kbd event3 leds
        """;

    [Fact]
    public void InputDeviceListIsParsed()
    {
        var devices = InputDeviceScanner.Parse(Devices.Split('\n'));

        Assert.Equal(4, devices.Count);
        InputDeviceInfo wmi = devices.Single(d => d.Name == "Asus WMI hotkeys");
        Assert.Equal("/dev/input/event7", wmi.EventNode);
        Assert.True(wmi.IsKeyboard);

        InputDeviceInfo hid = devices.Single(d => d.Name == "Asus Keyboard");
        Assert.Equal(0x0B05, hid.Vendor);
        Assert.Equal(0x19B6, hid.Product);
        Assert.Equal("/dev/input/event9", hid.EventNode);

        Assert.Equal(2, devices.Count(AtkEventSource.IsAsusDevice));
    }

    [Fact]
    public void WmiScancodeIsTheAtkId()
    {
        // MSC_SCAN 0x38 followed by KEY_PROG3 (ROG key)
        Assert.Equal(0x38, AtkEventTranslator.Translate(true, 0x38, 202));
        Assert.Equal(0xAE, AtkEventTranslator.Translate(true, 0xAE, 0x1d6));
        Assert.Equal(0x38, AtkEventTranslator.Translate(true, AtkEventTranslator.WmiArmouryScan, 202));
    }

    [Fact]
    public void HidVendorUsageIsTheAtkId()
    {
        Assert.Equal(0x38, AtkEventTranslator.Translate(false, unchecked((int)0xFF310038), 148));
        Assert.Equal(0xA7, AtkEventTranslator.Translate(false, unchecked((int)0xFF3100A7), 187));
        // ordinary keyboard usage page: never an ASUS key
        Assert.Equal(-1, AtkEventTranslator.Translate(false, 0x0007006C, 183));
    }

    [Fact]
    public void KeyCodeTableIsUsedWithoutScancode()
    {
        Assert.Equal(0x38, AtkEventTranslator.Translate(true, null, 202));
        Assert.Equal(0xAE, AtkEventTranslator.Translate(true, null, 0x1d6));
        Assert.Equal(0x38, AtkEventTranslator.Translate(false, null, 148));
        Assert.Equal(-1, AtkEventTranslator.Translate(true, null, 30)); // KEY_A
    }

    [Fact]
    public void DesktopHandledKeysAreRecognised()
    {
        Assert.True(AtkEventTranslator.IsDesktopHandled(0xC4)); // keyboard backlight up
        Assert.True(AtkEventTranslator.IsDesktopHandled(0x32)); // mute
        Assert.False(AtkEventTranslator.IsDesktopHandled(0x38)); // ROG key
        Assert.False(AtkEventTranslator.IsDesktopHandled(0xAE)); // fan mode
    }

    private static byte[] Encode(params (ushort type, ushort code, int value)[] events)
    {
        using var ms = new MemoryStream();
        foreach (var (type, code, value) in events)
            ms.Write(EvdevReader.Encode(type, code, value));
        return ms.ToArray();
    }

    [Fact]
    public async Task EventSourceRaisesAtkIdsFromEvdevNodes()
    {
        using var fs = new FakeSysfs();
        fs.File("/proc/bus/input/devices", Devices);

        const ushort msc = EvCodes.EV_MSC, scan = EvCodes.MSC_SCAN, key = EvCodes.EV_KEY, syn = EvCodes.EV_SYN;
        fs.Bytes("/dev/input/event7", Encode(
            (msc, scan, 0x38), (key, 202, 1), (syn, 0, 0), (key, 202, 0), (syn, 0, 0),     // ROG key press + release
            (msc, scan, 0xAE), (key, 0x1d6, 1), (syn, 0, 0),                                // fan mode key
            (msc, scan, 0xC4), (key, 230, 1), (syn, 0, 0),                                  // kbd backlight: desktop handled
            (msc, scan, 0x30), (key, 115, 1), (syn, 0, 0)));                                // volume up: desktop handled
        fs.Bytes("/dev/input/event9", Encode(
            (msc, scan, unchecked((int)0xFF3100A5)), (key, 185, 1), (syn, 0, 0),            // Ally left back
            (msc, scan, 0x0007002C), (key, 57, 1), (syn, 0, 0)));                           // space bar

        var received = new ConcurrentBag<int>();
        using var source = new AtkEventSource(fs.Root, received.Add, forwardDesktopHandled: false, rescanMs: 60000);
        source.Start();

        // the fake nodes are regular files: the reader drains them and stops at EOF
        for (int i = 0; i < 100 && received.Count < 3; i++) await Task.Delay(50);

        Assert.Contains(0x38, received);
        Assert.Contains(0xAE, received);
        Assert.Contains(0xA5, received);
        Assert.DoesNotContain(0xC4, received);
        Assert.DoesNotContain(0x30, received);
        Assert.Equal(3, received.Count);
    }

    [Fact]
    public async Task EventSourceCanForwardDesktopHandledKeys()
    {
        using var fs = new FakeSysfs();
        fs.File("/proc/bus/input/devices", Devices);
        fs.Bytes("/dev/input/event7", Encode(
            (EvCodes.EV_MSC, EvCodes.MSC_SCAN, 0xC4), (EvCodes.EV_KEY, 230, 1), (EvCodes.EV_SYN, 0, 0)));

        var received = new ConcurrentBag<int>();
        using var source = new AtkEventSource(fs.Root, received.Add, forwardDesktopHandled: true, rescanMs: 60000);
        source.Start();

        for (int i = 0; i < 100 && received.IsEmpty; i++) await Task.Delay(50);
        Assert.Contains(0xC4, received);
    }

    [Fact]
    public void BackendEventListenerIsOptional()
    {
        using var fs = new FakeSysfs();
        using var backend = new LinuxAsusBackend(new LinuxContext(fs.Root, new FakeRunner()),
            new LinuxAsusBackendOptions { EnableEventListener = false });

        // no devices, no listener: must simply do nothing
        backend.StartEventListener();
        backend.StartEventListener();
    }
}
