using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using GHelper.App.Controls;
using GHelper.App.Services;
using GHelper.App.Views.Peripherals;
using GHelper.Peripherals.Headset;

namespace GHelper.App.Views.Headset;

/// <summary>
/// Settings window of an ASUS headset (legacy AsusHeadsetSettings). Registered as "headset"; the factory argument is
/// the index of the device among the connected peripherals.
/// </summary>
public partial class HeadsetWindow : PeripheralWindow
{
    [ModuleInitializer]
    internal static void Register() => WindowRegistry.Register("headset", index => new HeadsetWindow(index));

    public HeadsetWindow()
    {
        InitializeComponent();
    }

    public HeadsetWindow(int index) : this(PeripheralSelection.Resolve<AsusHeadset>(index))
    {
    }

    /// <summary>Null = no usable device: the window closes again right away (legacy: the click did nothing).</summary>
    public HeadsetWindow(AsusHeadset? headset)
    {
        InitializeComponent();

        if (headset is null)
        {
            Opened += (_, _) => Close();
            return;
        }

        var vm = new HeadsetViewModel(headset);
        Attach(vm);
        WireView(vm);
    }

    public HeadsetViewModel? ViewModel => DataContext as HeadsetViewModel;

    private void WireView(HeadsetViewModel vm)
    {
        BuildBands(vm);

        SliderCommit.Attach(BrightnessSlider, v => vm.Brightness = v, vm.CommitBrightness);
        SliderCommit.Attach(SidetoneSlider, v => vm.Sidetone = v, vm.CommitSidetone);
        SliderCommit.Attach(LowBatterySlider, v => vm.LowBatteryWarning = v, vm.CommitLowBatteryWarning);

        LightingColorButton.Click += async (_, _) =>
        {
            var dialog = new ColorPickerWindow(vm.LightingColor, false, ConfigColorPickerStore.Instance);
            dialog.ColorChanged += vm.SetLightingColor;
            await dialog.ShowDialog(this);
        };
    }

    // ten vertical sliders with their frequency under them
    private void BuildBands(HeadsetViewModel vm)
    {
        foreach (EqualizerBand band in vm.Bands)
        {
            var slider = new RSlider
            {
                Vertical = true,
                Minimum = 0,
                Maximum = band.Max,
                Step = 1,
                Width = 28,
                Height = double.NaN,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            };
            slider.Bind(RangeBase.ValueProperty, new Binding(nameof(EqualizerBand.Value)) { Source = band, Mode = BindingMode.OneWay });
            Avalonia.Automation.AutomationProperties.SetName(slider, band.Label);

            int index = band.Index;
            SliderCommit.Attach(slider, v => band.Value = v, () => vm.CommitBand(index));

            var label = new TextBlock { Text = band.Label, HorizontalAlignment = HorizontalAlignment.Center };

            var column = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            column.Children.Add(slider);
            Grid.SetRow(label, 1);
            column.Children.Add(label);
            BandsGrid.Children.Add(column);
        }
    }
}
