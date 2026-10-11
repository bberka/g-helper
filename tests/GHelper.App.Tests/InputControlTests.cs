using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using GHelper.App.Controls;
using Xunit;

namespace GHelper.App.Tests;

public class InputControlTests
{
    [AvaloniaFact]
    public void Numeric_with_unit_formats_text()
    {
        var n = new NumericUpDownWithUnit { Minimum = 0, Maximum = 100, Unit = "W", Value = 45 };
        var w = new Window { Content = n };
        w.Show();

        Assert.Equal("45 (W)", n.Text);

        n.Value = 50;
        Assert.Equal("50 (W)", n.Text);

        n.UnitFirst = true;
        Assert.Equal("(W) 50", n.Text);

        n.OffText = "Off";
        n.Value = 0;
        Assert.Equal("Off", n.Text);
        w.Close();
    }

    [AvaloniaFact]
    public void Slider_snaps_to_step_and_clamps()
    {
        var s = new RSlider { Minimum = 40, Maximum = 100, Step = 5 };
        s.Value = 83;
        Assert.Equal(85, s.Value);
        s.Value = 120;
        Assert.Equal(100, s.Value);
        s.Value = 10;
        Assert.Equal(40, s.Value);
    }

    [AvaloniaFact]
    public void Slider_keyboard_steps_and_jumps_between_supported_values()
    {
        var s = new RSlider { Minimum = 40, Maximum = 100, Step = 5, Value = 80, Width = 300, Height = 20 };
        var w = new Window { Content = s };
        w.Show();
        s.Focus();

        s.SupportedValues = new List<int> { 60, 65, 70, 75, 80, 100 };
        w.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Assert.Equal(100, s.Value);
        w.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        Assert.Equal(80, s.Value);
        w.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        Assert.Equal(75, s.Value);

        s.SupportedValues = new List<int>();
        w.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        Assert.Equal(70, s.Value);
        w.Close();
    }

    [AvaloniaFact]
    public void Slider_exposes_accessible_name_and_slider_role()
    {
        var s = new RSlider { Minimum = 40, Maximum = 100, Value = 80 };
        AutomationProperties.SetName(s, "Battery Charge Limit: 80%");
        var w = new Window { Content = s };
        w.Show();

        var peer = ControlAutomationPeer.CreatePeerForElement(s);
        Assert.Equal("Battery Charge Limit: 80%", peer.GetName());
        Assert.Equal(AutomationControlType.Slider, peer.GetAutomationControlType());
        w.Close();
    }

    [AvaloniaFact]
    public void Activated_button_is_announced_as_active()
    {
        var b = new RButton { Content = "Turbo" };
        b.Activated = true;
        Assert.Equal("Active", AutomationProperties.GetHelpText(b));
        b.Activated = false;
        Assert.Null(AutomationProperties.GetHelpText(b));
    }

    [AvaloniaFact]
    public void Button_state_drives_the_control()
    {
        var state = new GHelper.App.ViewModels.ButtonState("Eco") { BorderColor = Avalonia.Media.Colors.Green };
        var b = new RButton { State = state };
        Assert.Equal("Eco", b.Content);
        Assert.False(b.Activated);

        state.Activated = true;
        state.Text = "Eco on";
        state.IsEnabled = false;
        state.FillColor = Avalonia.Media.Colors.Red;
        Assert.True(b.Activated);
        Assert.Equal("Eco on", b.Content);
        Assert.False(b.IsEnabled);
        Assert.Equal(Avalonia.Media.Colors.Red, b.FillColor);
        Assert.Equal(Avalonia.Media.Colors.Green, b.BorderColor);
    }
}
