using GHelper.Peripherals;

namespace GHelper.App.Views.Peripherals;

/// <summary>
/// Which connected device a "Peripheral 1/2/3" button stands for (legacy Settings.VisiblePeripherals and the click handler
/// index lookup). The main window and the peripheral windows must agree on this order.
/// </summary>
public static class PeripheralSelection
{
    /// <summary>Connected devices in button order; with more than three, ready devices come first.</summary>
    public static List<IPeripheral> Visible() => Order(PeripheralsProvider.AllPeripherals());

    public static List<IPeripheral> Order(List<IPeripheral> all) =>
        all.Count > 3 ? all.OrderByDescending(p => p.IsDeviceReady).ToList() : all;

    /// <summary>
    /// The device of type <typeparamref name="T"/> behind button <paramref name="index"/>. A device that is not ready
    /// yields null (legacy: the click does nothing). When the index does not point at a <typeparamref name="T"/> at all
    /// the first ready one of that type is used.
    /// </summary>
    public static T? Resolve<T>(int index, bool requireReady = true) where T : class, IPeripheral
        => Resolve<T>(Visible(), index, requireReady);

    public static T? Resolve<T>(List<IPeripheral> visible, int index, bool requireReady = true) where T : class, IPeripheral
    {
        IPeripheral? at = index >= 0 && index < visible.Count ? visible[index] : null;
        if (at is T exact) return !requireReady || exact.IsDeviceReady ? exact : null;
        return visible.OfType<T>().FirstOrDefault(p => !requireReady || p.IsDeviceReady);
    }
}
