using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GHelper.App.Controls;
using GHelper.App.Services;
using GHelper.App.Views.Peripherals;
using GHelper.Peripherals.Mouse;

namespace GHelper.App.Views.Mouse;

/// <summary>
/// Settings window of an ASUS mouse (legacy AsusMouseSettings). Layout in MouseWindow.axaml, state and device logic in
/// <see cref="MouseViewModel"/>; this class wires the commit events of the sliders and the dialogs.
/// Registered as "mouse"; the factory argument is the index of the device among the connected peripherals.
/// </summary>
public partial class MouseWindow : PeripheralWindow
{
    private static readonly FilePickerFileType ProfileFileType = new("G-Helper Mouse Profile V1 (*.gmp1)")
    {
        Patterns = new[] { "*.gmp1" },
    };

    [ModuleInitializer]
    internal static void Register() => WindowRegistry.Register("mouse", index => new MouseWindow(index));

    /// <summary>Design time / XAML loader.</summary>
    public MouseWindow()
    {
        InitializeComponent();
    }

    public MouseWindow(int index) : this(PeripheralSelection.Resolve<AsusMouse>(index))
    {
    }

    /// <summary>Null = no usable device (legacy: the click did nothing): the window closes again right away.</summary>
    public MouseWindow(AsusMouse? mouse)
    {
        InitializeComponent();

        if (mouse is null)
        {
            Opened += (_, _) => Close();
            return;
        }

        var vm = new MouseViewModel(mouse);
        Attach(vm);
        WireView(vm);
    }

    public MouseViewModel? ViewModel => DataContext as MouseViewModel;

    private void WireView(MouseViewModel vm)
    {
        // DPI: slider drag only updates the box, the device is written on release; the box writes when edited
        SliderCommit.Attach(DpiSlider, v => vm.Dpi = v, vm.CommitDpi);
        DpiNumeric.ValueChanged += (_, e) =>
        {
            if (e.NewValue is not { } value) return;
            vm.Dpi = (int)value;
            if (DpiNumeric.IsKeyboardFocusWithin || DpiNumeric.IsPointerOver) vm.CommitDpi();
        };
        DpiNumeric.LostFocus += (_, _) => vm.CommitDpi();

        SliderCommit.Attach(ZoneDpiSlider, v => vm.ZoneModeDpi = v, vm.CommitZoneModeDpi);
        ZoneDpiNumeric.ValueChanged += (_, e) =>
        {
            if (e.NewValue is not { } value) return;
            vm.ZoneModeDpi = (int)value;
            if (ZoneDpiNumeric.IsKeyboardFocusWithin || ZoneDpiNumeric.IsPointerOver) vm.CommitZoneModeDpi();
        };
        ZoneDpiNumeric.LostFocus += (_, _) => vm.CommitZoneModeDpi();

        SliderCommit.Attach(AngleSlider, v => vm.AngleAdjustment = v, vm.CommitAngleAdjustment);
        SliderCommit.Attach(DebounceSlider, v => vm.ButtonDebounce = v, vm.CommitButtonDebounce);
        SliderCommit.Attach(AccelerationSlider, v => vm.Acceleration = v, vm.CommitAcceleration);
        SliderCommit.Attach(DecelerationSlider, v => vm.Deceleration = v, vm.CommitDeceleration);
        SliderCommit.Attach(BrightnessSlider, v => vm.Brightness = v, vm.CommitBrightness);
        SliderCommit.Attach(LowBatterySlider, v => vm.LowBatteryWarning = v, vm.CommitLowBatteryWarning);

        DpiColorButton.Click += async (_, _) =>
            await PickColorAsync(vm.DpiColor, vm.SetDpiColor);
        LightingColorButton.Click += async (_, _) =>
            await PickColorAsync(vm.CurrentLightingColor, vm.SetLightingColor);

        vm.PickImportData = PickImportAsync;
        vm.SaveExportData = SaveExportAsync;
        ImportButton.Click += async (_, _) => await vm.ImportAsync();
        ExportButton.Click += async (_, _) => await vm.ExportAsync();
    }

    private async Task PickColorAsync(Avalonia.Media.Color initial, Action<Avalonia.Media.Color> apply)
    {
        var dialog = new ColorPickerWindow(initial, false, ConfigColorPickerStore.Instance);
        dialog.ColorChanged += apply;
        await dialog.ShowDialog(this);
    }

    private async Task<byte[]?> PickImportAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[] { ProfileFileType },
        });
        if (files.Count == 0) return null;

        await using var stream = await files[0].OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private async Task SaveExportAsync(byte[] data)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            FileTypeChoices = new[] { ProfileFileType },
            DefaultExtension = "gmp1",
            SuggestedFileName = (ViewModel?.Title ?? "mouse") + ".gmp1",
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        try { stream.SetLength(0); } catch { /* not seekable: new file */ }
        await stream.WriteAsync(data);
    }
}
