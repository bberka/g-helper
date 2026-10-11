using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using GHelper.App.Services;
using GHelper.App.Views.Mouse;
using GHelper.App.Views.Peripherals;
using GHelper.Peripherals;
using GHelper.Peripherals.Headset.Models;
using GHelper.Peripherals.Keyboard.Models;
using GHelper.Peripherals.Mouse.Models;
using Xunit;

namespace GHelper.App.Tests.Peripherals;

public class PeripheralIconTests
{
    [Fact]
    public void Core_icon_keys_map_to_assets()
    {
        Assert.EndsWith("icons8-maus-48.png", PeripheralIcons.UriFor(new Chakram()));
        Assert.EndsWith("dock-48.png", PeripheralIcons.UriFor(new BulwarkDock()));
        Assert.EndsWith("icons8-keyboard-48.png", PeripheralIcons.UriFor(new Azoth()));
        Assert.EndsWith("icons8-headphones-48.png", PeripheralIcons.UriFor(new DeltaII()));

        // unknown keys fall back to the icon of the device type
        Assert.EndsWith("icons8-headphones-48.png", PeripheralIcons.UriFor("does_not_exist", PeripheralType.Headset));
        Assert.EndsWith("icons8-maus-48.png", PeripheralIcons.UriFor((string?)null));
    }

    [AvaloniaFact]
    public void Mapped_assets_exist_and_load()
    {
        foreach (var uri in new[]
        {
            PeripheralIcons.UriFor(new Chakram()),
            PeripheralIcons.UriFor(new BulwarkDock()),
            PeripheralIcons.UriFor(new Azoth()),
            PeripheralIcons.UriFor(new DeltaII()),
        })
        {
            Assert.True(AssetLoader.Exists(new Uri(uri)), uri);
            Assert.NotNull(PeripheralIcons.Load(uri));
        }
    }

    [Fact]
    public void Window_names_are_registered()
    {
        // module initializers of the three windows
        Assert.True(WindowRegistry.IsRegistered("mouse"));
        Assert.True(WindowRegistry.IsRegistered("keyboard"));
        Assert.True(WindowRegistry.IsRegistered("headset"));
    }

    [Fact]
    public void Selection_orders_ready_devices_first_when_more_than_three()
    {
        TestPlatform.Ensure();
        var a = new FakeMouse(); // not synchronised: not ready
        var b = new FakeMouse(); b.SynchronizeDevice();
        var c = new FakeMouse();
        var d = new FakeMouse(); d.SynchronizeDevice();

        var ordered = PeripheralSelection.Order(new List<IPeripheral> { a, b, c, d });
        Assert.Same(b, ordered[0]);
        Assert.Same(d, ordered[1]);

        // three or fewer keep the connection order
        var small = PeripheralSelection.Order(new List<IPeripheral> { a, b });
        Assert.Same(a, small[0]);

        // resolving: exact index, not ready -> null, out of range -> first ready of the type
        Assert.Same(b, PeripheralSelection.Resolve<FakeMouse>(ordered, 0));
        Assert.Null(PeripheralSelection.Resolve<FakeMouse>(ordered, 2));
        Assert.Same(b, PeripheralSelection.Resolve<FakeMouse>(ordered, 9));
    }
}
