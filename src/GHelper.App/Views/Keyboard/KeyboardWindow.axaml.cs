using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using GHelper.App.Controls;
using GHelper.App.Services;
using GHelper.App.Views.Peripherals;
using GHelper.Peripherals.Keyboard;

namespace GHelper.App.Views.Keyboard;

/// <summary>
/// Settings window of an external ASUS keyboard (legacy AsusKeyboardSettings). Registered as "keyboard"; the factory
/// argument is the index of the device among the connected peripherals.
/// </summary>
public partial class KeyboardWindow : PeripheralWindow
{
    private const double BaseWidth = 460;

    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };

    [ModuleInitializer]
    internal static void Register() => WindowRegistry.Register("keyboard", index => new KeyboardWindow(index));

    public KeyboardWindow()
    {
        InitializeComponent();
    }

    public KeyboardWindow(int index) : this(PeripheralSelection.Resolve<AsusKeyboard>(index))
    {
    }

    /// <summary>Null = no usable device: the window closes again right away (legacy: the click did nothing).</summary>
    public KeyboardWindow(AsusKeyboard? keyboard)
    {
        InitializeComponent();

        if (keyboard is null)
        {
            Opened += (_, _) => Close();
            return;
        }

        var vm = new KeyboardViewModel(keyboard);
        Attach(vm);
        WireView(vm);

        Closed += (_, _) => _previewTimer.Stop();
    }

    public KeyboardViewModel? ViewModel => DataContext as KeyboardViewModel;

    private void WireView(KeyboardViewModel vm)
    {
        // grid wider than the settings column: the window grows with it
        RootPanel.Width = Math.Max(BaseWidth, vm.GridWidth);

        BuildKeyGrid(vm);
        BuildPalette(vm);

        SliderCommit.Attach(BrightnessSlider, v => vm.Brightness = v, vm.CommitBrightness);
        SliderCommit.Attach(LowBatterySlider, v => vm.LowBatteryWarning = v, vm.CommitLowBatteryWarning);
        SliderCommit.Attach(OledBrightnessSlider, v => vm.OledBrightness = v, vm.CommitOledBrightness);

        Color1Button.Click += async (_, _) => await PickColorAsync(vm.Color1, c => vm.SetColor(1, c));
        Color2Button.Click += async (_, _) => await PickColorAsync(vm.Color2, c => vm.SetColor(2, c));
        Color3Button.Click += async (_, _) => await PickColorAsync(vm.Color3, c => vm.SetColor(3, c));
        PaintColorButton.Click += async (_, _) => await PickColorAsync(vm.PaintColor, vm.SetPaintColor);

        // animated preview of the effect on the key borders
        _previewTimer.Tick += (_, _) => vm.PreviewTick();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(KeyboardViewModel.AnimatedPreview)) UpdatePreviewTimer(vm);
        };
        Opened += (_, _) => UpdatePreviewTimer(vm);

        vm.ReopenRequested += (_, _) => Dispatcher.UIThread.Post(() => Reopen(vm));
    }

    private void UpdatePreviewTimer(KeyboardViewModel vm)
    {
        if (vm.AnimatedPreview) _previewTimer.Start();
        else _previewTimer.Stop();
    }

    private void BuildKeyGrid(KeyboardViewModel vm)
    {
        if (!vm.GridVisible) return;

        var canvas = new Canvas { Width = vm.GridWidth, Height = vm.GridHeight };
        foreach (KeyTile tile in vm.Tiles)
        {
            var view = new KeyTileView(tile);
            view.Clicked += vm.ClickKey;
            Canvas.SetLeft(view, tile.X);
            Canvas.SetTop(view, tile.Y);
            canvas.Children.Add(view);
        }

        KeysHost.Child = canvas;
    }

    private void BuildPalette(KeyboardViewModel vm)
    {
        foreach (Color color in KeyboardViewModel.PaletteColors)
        {
            Color swatchColor = color;
            var swatch = new Border
            {
                Width = 20,
                Height = 24,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            swatch.Bind(Border.BorderBrushProperty, swatch.GetResourceObservable("BorderMainBrush"));
            Avalonia.Automation.AutomationProperties.SetName(swatch, ColorMath.ToHex(color));
            swatch.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(swatch).Properties.IsLeftButtonPressed) return;
                vm.SetPaintColor(swatchColor);
            };
            PalettePanel.Children.Add(swatch);
        }
    }

    private async Task PickColorAsync(Color initial, Action<Color> apply)
    {
        var dialog = new ColorPickerWindow(initial, false, ConfigColorPickerStore.Instance);
        dialog.ColorChanged += apply;
        await dialog.ShowDialog(this);
    }

    // test mode: another layout was chosen, rebuild the window for it
    private void Reopen(KeyboardViewModel vm)
    {
        AsusKeyboard keyboard = vm.Keyboard;
        int index = Math.Max(0, PeripheralSelection.Visible().IndexOf(keyboard));
        Close();
        WindowRegistry.Show("keyboard", index);
    }
}
