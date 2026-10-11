using GHelper.Peripherals;

namespace GHelper.App.Backend;

// Peripheral buttons below the keyboard section (legacy VisualizePeripherals / ButtonPeripheral_Click).
public sealed partial class MainBackend
{
    /// <summary>Registry names of the peripheral settings windows by device type.</summary>
    public static string WindowName(PeripheralType type) => type switch
    {
        PeripheralType.Mouse => "mouse",
        PeripheralType.Keyboard => "keyboard",
        _ => "headset",
    };

    /// <summary>The (up to three) devices shown as buttons: when more are connected, the ready ones win.</summary>
    public static List<IPeripheral> VisiblePeripherals()
    {
        var all = PeripheralsProvider.AllPeripherals();
        return all.Count > 3 ? all.OrderByDescending(p => p.IsDeviceReady).ToList() : all;
    }

    private void RefreshPeripherals()
    {
        // keyboard title carries the "+" for Aura sync
        _vm.SetKeyboardTitle(Properties.Strings.LaptopKeyboard, PeripheralsProvider.IsAuraSync);

        if (!PeripheralsProvider.IsAnyPeripheralConnect())
        {
            _vm.PeripheralsVisible = false;
            return;
        }

        var list = VisiblePeripherals();
        bool showPercentSetting = AppConfig.Is("mouse_battery");

        for (int i = 0; i < _vm.Peripherals.Length; i++)
        {
            var button = _vm.Peripherals[i];

            if (i >= list.Count)
            {
                button.IsVisible = false;
                continue;
            }

            var device = list[i];
            string name = device.GetDisplayName();
            bool ready = device.IsDeviceReady;
            bool hasBattery = device.HasBattery();
            bool showPercent = showPercentSetting && ready && hasBattery;

            button.Text = showPercent ? name + "\n" + device.Battery + "%" : name;

            // legacy composed charge bars / a "not connected" badge into the icon; here it goes into the tooltip
            string state = !ready ? "not ready" : (hasBattery ? device.Battery + "%" + (device.Charging ? " (charging)" : "") : "");
            button.ToolTip = state.Length > 0 ? name + ": " + state : name;
            button.AccessibleName = button.ToolTip;
            button.IsEnabled = true;

            var icon = _shell.LoadIcon(device.IconKey());
            if (icon is not null) button.Icon = icon;

            button.IsVisible = true;
        }

        _vm.PeripheralsVisible = true;
    }

    private void OpenPeripheral(int buttonIndex)
    {
        // legacy: a click first closes a settings window that is already open
        foreach (var type in new[] { PeripheralType.Mouse, PeripheralType.Keyboard, PeripheralType.Headset })
        {
            string open = WindowName(type);
            if (_shell.IsSideWindowOpen(open))
            {
                _shell.CloseSideWindow(open);
                return;
            }
        }

        var list = VisiblePeripherals();
        if (buttonIndex >= list.Count) return; // device went away in this very moment

        var device = list[buttonIndex];
        if (!device.IsDeviceReady) return;

        // the window argument is the index of the device in PeripheralsProvider.AllPeripherals()
        int index = PeripheralsProvider.AllPeripherals().IndexOf(device);
        Program.Ui.ShowWindow(WindowName(device.DeviceType()), Math.Max(0, index));
    }

    private void PeripheralHover(int buttonIndex)
    {
        var list = VisiblePeripherals();
        if (buttonIndex >= list.Count) return;

        var device = list[buttonIndex];

        // refresh the battery on hover when the device is marked "not ready"
        if (!device.IsDeviceReady) Parallel(device.ReadBattery);
    }
}
