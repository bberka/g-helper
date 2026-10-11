using System.ComponentModel;
using Avalonia.Controls;
using GHelper.App.Services;
using GHelper.App.ViewModels;
using GHelper.Properties;

namespace GHelper.App.Views;

/// <summary>
/// Settings window (legacy SettingsForm). Layout lives in MainWindow.axaml, state in <see cref="MainViewModel"/>.
/// The code behind only holds view concerns: hover tips, click targets that are not buttons, close-to-hide.
/// </summary>
public partial class MainWindow : Window
{
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        // legacy SettingsForm_FormClosing: the X button hides the window instead of exiting the app
        Closing += OnClosing;

        // tips under the GPU / screen rows (legacy *_MouseHover handlers)
        Hover(ButtonEco, v => { if (Vm is { } m) m.GpuTip = v; }, Strings.EcoGPUTooltip);
        Hover(ButtonStandard, v => { if (Vm is { } m) m.GpuTip = v; }, Strings.StandardGPUTooltip);
        Hover(ButtonUltimate, v => { if (Vm is { } m) m.GpuTip = v; }, Strings.UltimateGPUTooltip);
        Hover(ButtonOptimized, v => { if (Vm is { } m) m.GpuTip = v; }, Strings.OptimizedGPUTooltip);
        Hover(ButtonXgm, v => { if (Vm is { } m) m.GpuTip = v; }, "XGMobile toggle works only in Standard mode");

        Hover(ButtonScreenAuto, v => { if (Vm is { } m) m.ScreenTip = v; },
            () => Strings.AutoRefreshTooltip.Replace("60", MinRate.ToString()));
        Hover(Button60Hz, v => { if (Vm is { } m) m.ScreenTip = v; },
            () => Strings.MinRefreshTooltip.Replace("60", MinRate.ToString()));
        Hover(Button120Hz, v => { if (Vm is { } m) m.ScreenTip = v; }, Strings.MaxRefreshTooltip);
        Hover(ButtonFhd, v => { if (Vm is { } m) m.ScreenTip = v; },
            () => "Switch to " + ((Vm?.Fhd.Text == "FHD") ? "UHD" : "FHD") + " Mode");

        // text that behaves like a link
        Tap(CpuFanLabel, MainAction.ToggleFanUnits);
        Tap(GpuFanLabel, MainAction.ToggleFanUnits);
        Tap(GpuErrorIcon, MainAction.GpuErrorClick);
        Tap(VisualHintLabel, MainAction.DismissVisualHint);
        Tap(BacklightHintLabel, MainAction.BacklightHintClick);
        Tap(BatteryStatusLabel, MainAction.BatteryStatusClick);
        Tap(ChargeLabel, MainAction.BatteryReport);
        Tap(VersionLabel, MainAction.VersionClick);

        // hover texts of the battery row and the peripheral buttons
        HoverAction(ChargeLabel, MainAction.ChargeHoverEnter, MainAction.ChargeHoverLeave);
        HoverAction(ButtonBatteryFull, MainAction.BatteryFullHoverEnter, MainAction.BatteryFullHoverLeave);
        HoverAction(ButtonPeripheral1, MainAction.PeripheralHover1, null);
        HoverAction(ButtonPeripheral2, MainAction.PeripheralHover2, null);
        HoverAction(ButtonPeripheral3, MainAction.PeripheralHover3, null);

        // the second keyboard colour has its own swatch
        ButtonKeyboardColor.Swatch2Click += (_, _) => Vm?.Backend.Execute(MainAction.KeyboardColor2);

        DataContextChanged += (_, _) => AttachViewModel();
    }

    /// <summary>The legacy ScreenControl.MIN_RATE (60 unless the panel reports otherwise); set by the logic owner.</summary>
    public int MinRate { get; set; } = 60;

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>True once the app is really quitting; until then closing only hides.</summary>
    public void AllowClose() => _allowClose = true;

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;

        // legacy HideAll: the side windows close with the settings window
        if (App.Instance?.Windows is { } windows) windows.HideAll();
        else Hide();
    }

    private MainViewModel? _attached;

    private void AttachViewModel()
    {
        if (_attached is not null)
        {
            _attached.PropertyChanged -= OnVmPropertyChanged;
            _attached.DonateMenuRequested -= ShowDonateMenu;
        }
        _attached = Vm;
        if (_attached is null) return;
        _attached.PropertyChanged += OnVmPropertyChanged;
        _attached.DonateMenuRequested += ShowDonateMenu;
        SyncSliderValues();
        SyncVersionStyle();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.BatterySupportedValues):
                SyncSliderValues();
                break;
            case nameof(MainViewModel.VersionIsUpdate):
                SyncVersionStyle();
                break;
        }
    }

    private void SyncSliderValues()
    {
        if (Vm is null) return;
        SliderBattery.SupportedValues = Vm.BatterySupportedValues;
        SliderBattery.InvalidateVisual();
    }

    private void SyncVersionStyle() => VersionLabel.Classes.Set("update", Vm?.VersionIsUpdate == true);

    private ContextMenu? _donateMenu;

    /// <summary>Legacy DonateControl context menu (right click on the donate button).</summary>
    private void ShowDonateMenu()
    {
        if (_donateMenu is null)
        {
            var already = new MenuItem { Header = "❤ Already donated" };
            already.Click += (_, _) => Vm?.Backend.Execute(MainAction.DonateAlreadyDonated);

            var notInterested = new MenuItem { Header = "Not interested" };
            notInterested.Click += (_, _) => Vm?.Backend.Execute(MainAction.DonateNotInterested);

            _donateMenu = new ContextMenu
            {
                ItemsSource = new Control[] { already, notInterested, new Separator(), new MenuItem { Header = "Cancel" } },
            };
        }

        _donateMenu.Open(ButtonDonate);
    }

    private void HoverAction(Control c, MainAction enter, MainAction? leave)
    {
        c.PointerEntered += (_, _) => Vm?.Backend.Execute(enter);
        if (leave is { } l) c.PointerExited += (_, _) => Vm?.Backend.Execute(l);
    }

    private static void Hover(Control c, Action<string> set, string text) => Hover(c, set, () => text);

    private static void Hover(Control c, Action<string> set, Func<string> text)
    {
        c.PointerEntered += (_, _) => set(text());
        c.PointerExited += (_, _) => set("");
    }

    private void Tap(Control c, MainAction action)
    {
        c.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed) return;
            Vm?.Backend.Execute(action);
            if (action == MainAction.DismissVisualHint && Vm is { } m) m.VisualHintVisible = false;
        };
    }
}
