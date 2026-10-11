using GHelper.Peripherals;
using Xunit;

namespace GHelper.Tests.Peripherals;

public class HidPathTests : IDisposable
{
    private const string WinMouse = @"\\?\hid#vid_0b05&pid_1a1a&mi_00#8&2e1b4a7&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private const string WinVendor = @"\\?\hid#vid_0b05&pid_1a1a&mi_02#8&1f0c3d2&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private const string WinOmniCol03 = @"\\?\hid#vid_0b05&pid_1ace&mi_02&col03#7&abc&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private readonly string sysfs = Path.Combine(Path.GetTempPath(), "ghelper-hidpath-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(sysfs, true); } catch { }
    }

    [Fact]
    public void WindowsPathMatchesInterfaceTokenBySubstring()
    {
        Assert.True(HidPath.Matches(WinMouse, "mi_00"));
        Assert.False(HidPath.Matches(WinMouse, "mi_02"));
        Assert.True(HidPath.Matches(WinVendor, "mi_02"));
    }

    [Fact]
    public void WindowsPathMatchesCollectionToken()
    {
        Assert.True(HidPath.Matches(WinOmniCol03, "mi_02&col03"));
        Assert.False(HidPath.Matches(WinOmniCol03, "mi_02&col02"));
        Assert.True(HidPath.Matches(WinOmniCol03, "col03"));
    }

    [Fact]
    public void FullPathMatchesItself()
    {
        Assert.True(HidPath.Matches(WinVendor, WinVendor));
        Assert.False(HidPath.Matches(WinVendor, WinMouse));
    }

    [Fact]
    public void ParseWindowsPathExtractsInterfaceAndCollection()
    {
        var info = HidPath.ParseWindowsPath(WinOmniCol03);
        Assert.NotNull(info);
        Assert.Equal(2, info!.Value.Interface);
        Assert.Equal(3, info.Value.Collection);

        var plain = HidPath.ParseWindowsPath(WinMouse);
        Assert.Equal(0, plain!.Value.Interface);
        Assert.Null(plain.Value.Collection);

        Assert.Null(HidPath.ParseWindowsPath("/dev/hidraw0"));
    }

    [Fact]
    public void HidrawNodeResolvesUsbInterfaceNumberFromSysfs()
    {
        // /sys/class/hidraw/hidraw4/device/../bInterfaceNumber
        string device = Path.Combine(sysfs, "class", "hidraw", "hidraw4", "device");
        Directory.CreateDirectory(device);
        File.WriteAllText(Path.Combine(sysfs, "class", "hidraw", "hidraw4", "bInterfaceNumber"), "02\n");

        var info = HidPath.ResolveHidraw("/dev/hidraw4", sysfs);
        Assert.NotNull(info);
        Assert.Equal(2, info!.Value.Interface);
        Assert.Null(HidPath.ResolveHidraw("/dev/hidraw9", sysfs));
        Assert.Null(HidPath.ResolveHidraw("/dev/input/event3", sysfs));
    }

    [Fact]
    public void LegacyTokensMatchHidrawNodeThroughResolver()
    {
        var saved = HidPath.InterfaceResolver;
        try
        {
            HidPath.InterfaceResolver = path => path switch
            {
                "/dev/hidraw1" => new HidInterfaceInfo(0, null),
                "/dev/hidraw2" => new HidInterfaceInfo(2, null),
                _ => null,
            };

            Assert.True(HidPath.Matches("/dev/hidraw1", "mi_00"));
            Assert.False(HidPath.Matches("/dev/hidraw1", "mi_02"));
            Assert.True(HidPath.Matches("/dev/hidraw2", "mi_02"));
            // hidraw exposes every collection of an interface on one node
            Assert.True(HidPath.Matches("/dev/hidraw2", "mi_02&col03"));
            // a bare collection token cannot be verified without a collection number
            Assert.False(HidPath.Matches("/dev/hidraw2", "col02"));
            Assert.False(HidPath.Matches("/dev/hidraw7", "mi_00"));
        }
        finally
        {
            HidPath.InterfaceResolver = saved;
        }
    }

    [Fact]
    public void StoredHidrawPathDoesNotMatchLongerNode()
    {
        Assert.True(HidPath.Matches("/dev/hidraw1", "/dev/hidraw1"));
        Assert.False(HidPath.Matches("/dev/hidraw13", "/dev/hidraw1"));
    }

    [Fact]
    public void EmptyTokenMatchesEverything()
    {
        Assert.True(HidPath.Matches("/dev/hidraw0", ""));
    }
}
