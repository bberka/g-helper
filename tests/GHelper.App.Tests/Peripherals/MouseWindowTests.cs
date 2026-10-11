using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GHelper.App.Controls;
using GHelper.App.Views.Mouse;
using GHelper.App.Views.Peripherals;
using GHelper.Peripherals.Mouse;
using Xunit;

namespace GHelper.App.Tests.Peripherals;

/// <summary>Renders the mouse window to PNG (artifacts folder) and checks the bindings reach the controls.</summary>
public class MouseWindowTests
{
    public MouseWindowTests()
    {
        TestPlatform.Ensure();
    }

    private static MouseWindow Open(FakeMouse mouse, ThemeVariant theme, string name)
    {
        Application.Current!.RequestedThemeVariant = theme;
        var window = new MouseWindow(mouse);
        MainWindowSnapshotTests.Render(window, name);
        return window;
    }

    [AvaloniaFact]
    public void Renders_full_featured_mouse_dark()
    {
        var mouse = new FakeMouse { SpeedShift = true };
        var window = Open(mouse, ThemeVariant.Dark, "mouse-full-dark");

        Assert.Equal("ROG Test Mouse", window.Title);

        // bound values reached the controls (Min/Max are bound before Value)
        var dpi = window.FindControl<RSlider>("DpiSlider")!;
        Assert.Equal(26000, dpi.Maximum);
        Assert.Equal(1600, dpi.Value);

        var numeric = window.FindControl<RNumericUpDown>("DpiNumeric")!;
        Assert.Equal(1600m, numeric.Value);

        var accel = window.FindControl<RSlider>("AccelerationSlider")!;
        Assert.Equal(4, accel.Value);

        var angle = window.FindControl<RSlider>("AngleSlider")!;
        Assert.Equal(-30, angle.Minimum);
        Assert.Equal(10, angle.Value);

        // the device was not written by binding the window
        Assert.DoesNotContain(mouse.Packets, p => p.Length > 2 && p[1] == 0x51);
        Assert.Equal(1600u, mouse.DpiSettings[1].DPI);

        window.Close();
    }

    [AvaloniaFact]
    public void Renders_light_theme()
    {
        var window = Open(new FakeMouse(), ThemeVariant.Light, "mouse-full-light");
        window.Close();
    }

    [AvaloniaFact]
    public void Renders_minimal_mouse_without_optional_sections()
    {
        var mouse = new FakeMouse
        {
            Name = "ROG Basic Mouse", Battery_ = false, Profiles = false, DpiColors = false, Rgb = false,
            AutoPowerOff = false, LowBattery = false, AngleSnap = false, AngleTune = false, Acceleration_ = false,
            Deceleration_ = false, LiftOff = false, Debounce_ = false, MotionSync_ = false, ZoneMode_ = false,
            Bindings = false, DpiCount = 2, ChangeDpiCount = false,
        };
        var window = Open(mouse, ThemeVariant.Dark, "mouse-minimal-dark");

        Assert.False(window.FindControl<StackPanel>("BindingsPanel")!.IsVisible);
        Assert.False(window.FindControl<Avalonia.Controls.Panel>("BottomButtons")!.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Dpi_slider_commit_writes_the_device()
    {
        var mouse = new FakeMouse();
        var window = Open(mouse, ThemeVariant.Dark, "mouse-dpi");
        var vm = window.ViewModel!;
        var slider = window.FindControl<RSlider>("DpiSlider")!;

        // programmatic change: no write
        vm.Dpi = 2000;
        Assert.Equal(1600u, mouse.DpiSettings[1].DPI);
        Assert.Equal(2000, slider.Value);

        window.Close();
    }

    [AvaloniaFact]
    public void Button_bindings_panel_and_rows_are_built()
    {
        var mouse = new FakeMouse { SpeedShift = true };
        var window = Open(mouse, ThemeVariant.Dark, "mouse-bindings");

        var panel = window.FindControl<StackPanel>("BindingsPanel")!;
        Assert.True(panel.IsVisible);
        var combos = window.GetVisualDescendants().OfType<BindingComboBox>().ToList();
        Assert.Equal(8, combos.Count);
        Assert.All(combos, c => Assert.NotNull(c.SelectedItem));
        window.Close();
    }
}
