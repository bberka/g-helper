using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using GHelper.App.Controls;
using GHelper.App.Services;
using GHelper.App.Theme;
using GHelper.App.ViewModels;
using GHelper.Overlay;
using GHelper.Properties;
using DColor = System.Drawing.Color;

namespace GHelper.App.Views.Overlay;

internal static class OverlayWindowRegistration
{
    [ModuleInitializer]
    internal static void Register() => WindowRegistry.Register("overlay", _ => new OverlayConfigWindow());
}

/// <summary>
/// Overlay settings (legacy OverlayConfig): enable / "only in games", the four presets, which blocks each preset shows,
/// size, background transparency and the CPU / GPU colours. Every change is written to the config at once and the live
/// overlay is told to re-read it.
/// </summary>
public sealed class OverlayConfigWindow : SideWindow
{
    private const string HotkeyText =
        "•  Ctrl + Shift + Alt + O - Toggle overlay\n" +
        "•  Ctrl + Shift + Alt + Mouse Drag - Move overlay\n" +
        "•  Ctrl + Shift + Alt + Mouse Click - Switch mode\n" +
        "•  Ctrl + Shift + Alt + Wheel - Resize overlay\n" +
        "•  Ctrl + Shift + Alt + Wheel Click - Reset size";

    private readonly IOverlayConfigStore _store;
    private readonly Action _refreshOverlay;
    private readonly Action _toggleOverlay;
    private readonly Action _toggleGameOnly;

    private readonly RCheckBox _checkEnable = new();
    private readonly RCheckBox _checkGameOnly = new();
    private readonly RButton[] _modeButtons = new RButton[4]; // indexed by OverlayMode value
    private readonly (RCheckBox box, string key)[] _blocks;
    private readonly RCheckBox _checkBattery = new() { IsThreeState = true };
    private readonly RSlider _scale = new();
    private readonly RSlider _alpha = new();
    private readonly TextBlock _scaleText = new() { HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _alphaText = new() { HorizontalAlignment = HorizontalAlignment.Right };
    private readonly RColorButton _cpuColor = new();
    private readonly RColorButton _gpuColor = new();

    private int _mode;
    private bool _updating;

    public OverlayConfigWindow() : this(
        new AppConfigOverlayStore(),
        refreshOverlay: () => GHelper.Program.hardwareOverlay?.RefreshSettings(),
        toggleOverlay: () => ExecuteMain(MainAction.OverlayToggleOnly),
        toggleGameOnly: () => App.Instance?.ViewModel.Backend.SetValue(MainValue.OverlayGameOnly,
            AppConfig.IsOverlayGameOnly() ? 0 : 1))
    {
    }

    /// <param name="refreshOverlay">Tells the running overlay to re-read the options (legacy RefreshSettings).</param>
    /// <param name="toggleOverlay">Switch the overlay on / off (legacy settingsForm.ToggleOverlay).</param>
    /// <param name="toggleGameOnly">Flip "only in games" (legacy settingsForm.ToggleOverlayGameOnly).</param>
    internal OverlayConfigWindow(IOverlayConfigStore store, Action refreshOverlay, Action toggleOverlay, Action toggleGameOnly)
    {
        _store = store;
        _refreshOverlay = refreshOverlay;
        _toggleOverlay = toggleOverlay;
        _toggleGameOnly = toggleGameOnly;

        Title = Strings.Overlay;
        Width = 460;
        SizeToContent = SizeToContent.Height;

        _blocks = new[]
        {
            (Check("FPS"), "overlay_show_fps"),
            (Check(Strings.OverlayTemperatures), "overlay_show_temp"),
            (Check(Strings.FanSpeed), "overlay_show_fans"),
            (Check(Strings.OverlayChart), "overlay_show_chart"),
            (Check(Strings.Power), "overlay_show_power"),
            (Check(Strings.OverlayLoad), "overlay_show_usage"),
            (Check("RAM"), "overlay_show_ram"),
            (Check(Strings.OverlayLabels), "overlay_names"),
        };

        Content = BuildContent();
        LoadState();
        WireEvents();
    }

    private static void ExecuteMain(MainAction action) => App.Instance?.ViewModel.Backend.Execute(action);

    private static RCheckBox Check(string text) => new() { Content = text, Margin = new Thickness(0, 2) };

    private Control BuildContent()
    {
        _checkEnable.Content = Strings.Overlay;
        _checkGameOnly.Content = Strings.OverlayOnlyInGames;
        _checkBattery.Content = Strings.Battery;
        _checkBattery.Margin = new Thickness(0, 2);

        var modes = new UniformGrid { Columns = 4, Margin = new Thickness(0, 2) };
        void AddMode(OverlayMode mode, string text, Color border)
        {
            var button = new RButton { Content = text, Height = UiMetrics.LargeButtonHeight, Margin = new Thickness(0, 0, 6, 0), BorderColor = border };
            button.Click += (_, _) => SelectMode((int)mode);
            _modeButtons[(int)mode] = button;
            modes.Children.Add(button);
        }
        AddMode(OverlayMode.Light, Strings.OverlayModeLight, AppColors.Eco);
        AddMode(OverlayMode.Default, Strings.Default, AppColors.Standard);
        AddMode(OverlayMode.Full, Strings.OverlayModeFull, AppColors.Turbo);
        AddMode(OverlayMode.Complete, Strings.OverlayModeComplete, AppColors.Custom);

        // three columns: fps temp fans / chart power load / ram battery labels
        var blockGrid = new UniformGrid { Columns = 3 };
        foreach (var index in new[] { 0, 1, 2, 3, 4, 5 }) blockGrid.Children.Add(_blocks[index].box);
        blockGrid.Children.Add(_blocks[6].box);
        blockGrid.Children.Add(_checkBattery);
        blockGrid.Children.Add(_blocks[7].box);

        _scale.Minimum = OverlaySettings.MinScalePercent;
        _scale.Maximum = OverlaySettings.MaxScalePercent;
        _scale.Step = 5;
        _alpha.Minimum = 0;
        _alpha.Maximum = 255;
        _alpha.Step = 1;

        var sliders = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,*"), Margin = new Thickness(0, 6, 0, 2) };
        sliders.Children.Add(SliderBlock(Strings.OverlaySize, _scaleText, _scale, 0));
        sliders.Children.Add(SliderBlock(Strings.OverlayTransparency, _alphaText, _alpha, 2));

        _cpuColor.Content = "CPU " + Strings.Color;
        _gpuColor.Content = "GPU " + Strings.Color;
        var reset = new RButton { Content = Strings.Reset };
        reset.Click += (_, _) => Reset();
        foreach (var b in new RButton[] { _cpuColor, _gpuColor })
        {
            b.Margin = new Thickness(0, 0, 6, 0);
        }
        var colors = new UniformGrid { Columns = 3, Margin = new Thickness(0, 4) };
        colors.Children.Add(_cpuColor);
        colors.Children.Add(_gpuColor);
        colors.Children.Add(reset);

        var toggles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        toggles.Children.Add(_checkEnable);
        Grid.SetColumn(_checkGameOnly, 1);
        toggles.Children.Add(_checkGameOnly);

        var hotkeys = new TextBlock
        {
            Text = HotkeyText,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, DejaVu Sans Mono, Liberation Mono, monospace"),
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0),
        };
        hotkeys.Classes.Add("tip");

        return new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 6,
            Children = { toggles, modes, blockGrid, sliders, colors, hotkeys },
        };
    }

    private static Control SliderBlock(string title, TextBlock valueText, RSlider slider, int column)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(valueText, 1);
        header.Children.Add(valueText);

        var panel = new StackPanel { Spacing = 4, Children = { header, slider } };
        Grid.SetColumn(panel, column);
        return panel;
    }

    // ---- state ----------------------------------------------------------------------------------------

    private bool Enabled => _store.GetInt("overlay", 0) == 1;
    private bool GameOnly => _store.GetInt("overlay_game_only", 0) == 1;

    private int Mode => Math.Clamp(_store.GetInt("overlay_mode", 0), 0, _modeButtons.Length - 1);

    private DColor CpuColor => OverlaySettings.ParseColor(_store.GetString("overlay_color_cpu"), OverlaySettings.DefaultCpuColor);
    private DColor GpuColor => OverlaySettings.ParseColor(_store.GetString("overlay_color_gpu"), OverlaySettings.DefaultGpuColor);

    private static Color ToColor(DColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private string AlphaText() => Math.Round(_alpha.Value * 100.0 / 255) + "%";

    private void LoadState()
    {
        _updating = true;

        _checkEnable.IsChecked = Enabled;
        _checkGameOnly.IsChecked = GameOnly;
        _mode = Mode;
        VisualiseBlocks();

        _scale.Value = Math.Clamp(_store.GetInt("overlay_scale_percent", 100), OverlaySettings.MinScalePercent, OverlaySettings.MaxScalePercent);
        _alpha.Value = Math.Clamp(_store.GetInt("overlay_alpha", OverlaySettings.DefaultAlpha), 0, 255);
        _scaleText.Text = (int)_scale.Value + "%";
        _alphaText.Text = AlphaText();
        AutomationProperties.SetName(_scale, Strings.OverlaySize);
        AutomationProperties.SetName(_alpha, Strings.OverlayTransparency);

        _cpuColor.SwatchColor = ToColor(CpuColor);
        _gpuColor.SwatchColor = ToColor(GpuColor);

        _updating = false;
    }

    private void VisualiseBlocks()
    {
        bool was = _updating;
        _updating = true;

        for (int i = 0; i < _modeButtons.Length; i++) _modeButtons[i].Activated = i == _mode;
        foreach (var (box, key) in _blocks) box.IsChecked = OverlayOptions.BlockShown(_store, key, _mode);

        int battery = OverlayOptions.BatteryState(_store, _mode);
        _checkBattery.IsChecked = battery < 0 ? null : battery != 0; // null = only on battery

        _updating = was;
    }

    private void WireEvents()
    {
        _checkEnable.IsCheckedChanged += (_, _) =>
        {
            if (!_updating && (_checkEnable.IsChecked == true) != Enabled) _toggleOverlay();
        };
        _checkGameOnly.IsCheckedChanged += (_, _) =>
        {
            if (!_updating && (_checkGameOnly.IsChecked == true) != GameOnly) _toggleGameOnly();
        };

        foreach (var (box, key) in _blocks)
        {
            var captured = (box, key);
            box.IsCheckedChanged += (_, _) =>
            {
                if (_updating) return;
                _store.Set(OverlaySettings.ModeKey(captured.key, _mode), captured.box.IsChecked == true ? 1 : 0);
                _refreshOverlay();
            };
        }

        _checkBattery.IsCheckedChanged += (_, _) =>
        {
            if (_updating) return;
            int state = _checkBattery.IsChecked is null ? -1 : _checkBattery.IsChecked == true ? 1 : 0;
            _store.Set(OverlaySettings.ModeKey("overlay_show_battery", _mode), state);
            _refreshOverlay();
        };

        _scale.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty || _updating) return;
            _scaleText.Text = (int)_scale.Value + "%";
            _store.Set("overlay_scale_percent", (int)_scale.Value);
            _refreshOverlay();
        };

        _alpha.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty || _updating) return;
            _alphaText.Text = AlphaText();
            _store.Set("overlay_alpha", (int)_alpha.Value);
            _refreshOverlay();
        };

        _cpuColor.Click += (_, _) => PickColor("overlay_color_cpu", _cpuColor, CpuColor);
        _gpuColor.Click += (_, _) => PickColor("overlay_color_gpu", _gpuColor, GpuColor);
    }

    private void SelectMode(int mode)
    {
        _mode = mode;
        _store.Set("overlay_mode", mode);
        VisualiseBlocks();
        _refreshOverlay();
    }

    private void Reset()
    {
        for (int m = 0; m < _modeButtons.Length; m++)
        {
            foreach (var (_, key) in _blocks) _store.Remove(OverlaySettings.ModeKey(key, m));
            _store.Remove(OverlaySettings.ModeKey("overlay_show_battery", m));
        }
        _store.Remove("overlay_scale_percent");
        _store.Remove("overlay_alpha");
        _store.Remove("overlay_color_cpu");
        _store.Remove("overlay_color_gpu");

        _updating = true;
        VisualiseBlocks();
        _scale.Value = 100;
        _alpha.Value = OverlaySettings.DefaultAlpha;
        _scaleText.Text = "100%";
        _alphaText.Text = AlphaText();
        _cpuColor.SwatchColor = ToColor(OverlaySettings.DefaultCpuColor);
        _gpuColor.SwatchColor = ToColor(OverlaySettings.DefaultGpuColor);
        _updating = false;

        _refreshOverlay();
    }

    private async void PickColor(string key, RColorButton button, DColor initial)
    {
        var picker = new ColorPickerWindow(ToColor(initial), false, new ConfigPickerStore(_store));
        picker.ColorChanged += c =>
        {
            _store.Set(key, $"{c.R:X2}{c.G:X2}{c.B:X2}");
            button.SwatchColor = c;
            _refreshOverlay();
        };

        try { await picker.ShowDialog(this); }
        catch (Exception ex) { AppLog.Write("Overlay colour picker: " + ex.Message); }
    }

    /// <summary>Custom swatches are shared with the keyboard lighting picker (legacy "aura_color_custom").</summary>
    private sealed class ConfigPickerStore : IColorPickerStore
    {
        private readonly IOverlayConfigStore _store;
        public ConfigPickerStore(IOverlayConfigStore store) => _store = store;
        public string? LoadCustomColors() => _store.GetString("aura_color_custom");
        public void SaveCustomColors(string value) => _store.Set("aura_color_custom", value);
    }
}
