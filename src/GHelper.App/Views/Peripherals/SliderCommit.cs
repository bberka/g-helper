using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using GHelper.App.Controls;

namespace GHelper.App.Views.Peripherals;

/// <summary>
/// The legacy peripheral windows applied a slider on MouseUp / KeyUp and only updated the caption while dragging.
/// <see cref="Attach"/> reproduces that: user driven value changes (pointer or arrow keys) are reported through
/// <c>changed</c> (caption), the release through <c>commit</c> (device write). Programmatic value changes - the view
/// refreshing from the device, or RangeBase coercing the value when Minimum/Maximum are bound after Value - are ignored,
/// so they never write to the hardware.
/// </summary>
public static class SliderCommit
{
    public static void Attach(RSlider slider, Action<int> changed, Action commit)
    {
        bool editing = false;

        slider.AddHandler(InputElement.PointerPressedEvent, (_, _) => editing = true,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        slider.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) editing = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        slider.PropertyChanged += (_, e) =>
        {
            if (editing && e.Property == RangeBase.ValueProperty) changed((int)Math.Round(slider.Value));
        };

        slider.ValueCommitted += (_, _) =>
        {
            if (!editing) return;
            editing = false;
            changed((int)Math.Round(slider.Value));
            commit();
        };
    }
}
