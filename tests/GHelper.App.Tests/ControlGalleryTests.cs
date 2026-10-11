using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using GHelper.App.Controls;
using GHelper.App.Services;
using GHelper.App.Theme;
using GHelper.App.ViewModels;
using Xunit;

namespace GHelper.App.Tests;

/// <summary>Renders every custom control (states, themes) into one image per theme for visual review.</summary>
public class ControlGalleryTests
{
    private static Window BuildGallery()
    {
        var curve = new FanCurveChart
        {
            Title = "CPU fan curve, RPM/°C",
            Height = 190,
            XMin = 20, XMax = 100, YMin = 0, YMax = 100,
            XLabel = v => v.ToString("0"),
            YLabel = v => v.ToString("0") + "%",
        };
        curve.SetPoints(new (double, double)[] { (30, 0), (40, 10), (50, 20), (60, 35), (70, 50), (80, 70), (90, 90), (100, 100) });

        var slider = new RSlider { Minimum = 40, Maximum = 100, Step = 5, Value = 80, Height = 20 };
        slider.SupportedValues = new List<int> { 60, 65, 70, 75, 80, 100 };

        var combo = new RComboBox { ItemsSource = new[] { "Static", "Breathe", "Rainbow" }, SelectedIndex = 1 };
        var numeric = new NumericUpDownWithUnit { Minimum = 0, Maximum = 100, Value = 45, Unit = "W", Increment = 1, Width = 140 };
        var offNumeric = new NumericUpDownWithUnit { Minimum = 0, Maximum = 100, Value = 0, OffText = "Off", Width = 140 };
        var text = new RTextBox { Text = "hello", PlaceholderText = "placeholder", Width = 140, VerticalAlignment = VerticalAlignment.Center };
        var empty = new RTextBox { PlaceholderText = "placeholder", Width = 140, VerticalAlignment = VerticalAlignment.Center };
        var track = new RTrackBar { Minimum = 0, Maximum = 10, Value = 4, Width = 200, VerticalAlignment = VerticalAlignment.Center };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new RButton { Content = "Normal", Width = 90, Height = 32 },
                new RButton { Content = "Secondary", Secondary = true, Width = 90, Height = 32 },
                new RButton { Content = "Active", Activated = true, BorderColor = AppColors.Eco, Width = 90, Height = 32 },
                new RButton { Content = "Disabled", IsEnabled = false, Width = 90, Height = 32 },
                new RColorButton { Content = "Color", SwatchColor = Colors.Red, SwatchColor2 = Colors.DodgerBlue, Width = 110, Height = 32 },
                new RBadgeButton { Content = "Donate", Badge = 4, BorderColor = AppColors.Turbo, Secondary = true, Width = 90, Height = 32 },
            },
        };

        var flags = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                Flag("FN-Lock", null),
                Flag("FN-Lock", AppColors.Standard),
                Flag("Overlay", AppColors.Eco),
                Flag("ArmouryCrate", AppColors.Turbo),
            },
        };

        var root = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children =
            {
                buttons,
                flags,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { combo, numeric, offNumeric } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { text, empty, track } },
                new RCheckBox { Content = "Run on startup", IsChecked = true, ShowBorder = true, Width = 200 },
                slider,
                curve,
            },
        };

        return new Window { Width = 620, SizeToContent = SizeToContent.Height, Content = root, Title = "Gallery" };
    }

    private static RButton Flag(string text, Color? fill)
    {
        var b = new RButton { Content = text, Secondary = true, Borderless = true, Width = 90, Height = 20, FillColor = fill };
        b.Classes.Add("flag");
        return b;
    }

    [AvaloniaFact]
    public void Gallery_dark()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var w = BuildGallery();
        var frame = MainWindowSnapshotTests.Render(w, "gallery-dark");
        Assert.True(frame.PixelSize.Height > 300);
        w.Close();
    }

    [AvaloniaFact]
    public void Gallery_light()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var w = BuildGallery();
        MainWindowSnapshotTests.Render(w, "gallery-light");
        w.Close();
    }

    [AvaloniaFact]
    public void Toast_renders_with_and_without_icon()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var toast = new ToastWindow();
        toast.ShowToast("Brightness 60%", "brightnessUp");
        MainWindowSnapshotTests.Render(toast, "toast-icon");
        toast.ShowToast("Balanced");
        MainWindowSnapshotTests.Render(toast, "toast-text");
        toast.Close();
    }

    [AvaloniaFact]
    public void ColorPicker_renders()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var picker = new ColorPickerWindow(Color.FromRgb(255, 64, 0), allowRandom: true);
        MainWindowSnapshotTests.Render(picker, "colorpicker-dark");
        picker.Close();
    }
}
