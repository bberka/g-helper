using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using GHelper.App.ViewModels;

namespace GHelper.App.Services;

/// <summary>
/// System tray icon: icon follows the GPU mode (and the black/white option + taskbar theme), tooltip shows the
/// sensor summary, left click toggles the main window next to the taskbar, right click opens the context menu
/// (performance modes, GPU modes, overlay, quit).
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly Application _app;
    private readonly MainViewModel _vm;
    private readonly Action<bool, bool> _toggle;
    private readonly TrayIcon _icon;
    private readonly Dictionary<string, WindowIcon> _icons = new();
    private string? _currentIcon;
    private bool _rebuildQueued;
    private bool _osDark;

    public TrayService(Application app, MainViewModel vm, Action<bool, bool> toggleSettings)
    {
        _app = app;
        _vm = vm;
        _toggle = toggleSettings;

        _osDark = ReadOsDark();
        _icon = new TrayIcon
        {
            ToolTipText = "G-Helper",
            IsVisible = true,
        };
        _icon.Clicked += OnClicked;

        TrayIcon.SetIcons(app, new TrayIcons { _icon });

        _vm.PropertyChanged += OnVmChanged;
        foreach (var b in new[] { _vm.Eco, _vm.Standard, _vm.Optimized, _vm.Ultimate })
            b.PropertyChanged += OnGpuButtonChanged;

        if (app.PlatformSettings is { } ps)
            ps.ColorValuesChanged += OnPlatformColorsChanged;

        Rebuild();
        UpdateIcon();
        UpdateTip();
    }

    public TrayIcon Icon => _icon;

    private void OnClicked(object? sender, EventArgs e)
    {
        // Avalonia raises Clicked for the primary button only; the context menu belongs to the secondary button.
        _toggle(true, true);
    }

    private bool ReadOsDark()
    {
        try
        {
            return _app.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
        }
        catch
        {
            return false;
        }
    }

    private void OnPlatformColorsChanged(object? sender, PlatformColorValues e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _osDark = ReadOsDark();
            UpdateIcon();
        });
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.GpuMode):
            case nameof(MainViewModel.BwTrayIcon):
                UpdateIcon();
                QueueRebuild();
                break;
            case nameof(MainViewModel.TrayToolTip):
                UpdateTip();
                break;
            case nameof(MainViewModel.CurrentMode):
            case nameof(MainViewModel.TrayModes):
            case nameof(MainViewModel.IsGpuSection):
            case nameof(MainViewModel.IsMuxGpu):
            case nameof(MainViewModel.OverlayEnabled):
            case nameof(MainViewModel.OverlayGameOnly):
                QueueRebuild();
                break;
        }
    }

    private void OnGpuButtonChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ButtonState.Activated) or nameof(ButtonState.IsVisible))
            QueueRebuild();
    }

    private void QueueRebuild()
    {
        if (_rebuildQueued) return;
        _rebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildQueued = false;
            Rebuild();
        });
    }

    /// <summary>Rebuild the context menu from the current view model (legacy SetContextMenu).</summary>
    public void Rebuild()
    {
        var menu = new NativeMenu();
        foreach (var entry in TrayMenuModel.Build(_vm, _vm.Backend))
        {
            if (entry.IsSeparator)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
                continue;
            }

            var item = new NativeMenuItem(entry.Text) { IsEnabled = entry.IsEnabled };
            if (entry.IsChecked is { } isChecked)
            {
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsChecked = isChecked;
            }
            if (entry.Invoke is { } invoke)
                item.Click += (_, _) => invoke();
            menu.Items.Add(item);
        }
        // Avalonia has no tray mouse-move event (legacy NotifyIcon.MouseMove refreshed the sensors for the tooltip):
        // refresh when the menu opens; the tooltip is also refreshed on every tray click / window show.
        // Not inline: a cold sensor read can take seconds and would hold the menu back while the UI thread is busy.
        menu.Opening += (_, _) => Dispatcher.UIThread.Post(() => _vm.Backend.RefreshArea("sensors", _vm), DispatcherPriority.Background);

        _icon.Menu = menu;
    }

    private void UpdateIcon()
    {
        string name = TrayMenuModel.IconName(_vm.GpuMode, _vm.BwTrayIcon, _osDark);
        if (name == _currentIcon) return;
        _currentIcon = name;

        if (!_icons.TryGetValue(name, out var icon))
        {
            try
            {
                using var stream = AssetLoader.Open(new Uri($"avares://GHelper/Assets/Tray/{name}.png"));
                icon = new WindowIcon(new Bitmap(stream));
                _icons[name] = icon;
            }
            catch (Exception ex)
            {
                AppLog.Write("Tray icon " + name + ": " + ex.Message);
                return;
            }
        }
        _icon.Icon = icon;
    }

    private void UpdateTip() => _icon.ToolTipText = string.IsNullOrWhiteSpace(_vm.TrayToolTip) ? "G-Helper" : _vm.TrayToolTip;

    public void Dispose()
    {
        _vm.PropertyChanged -= OnVmChanged;
        _icon.Clicked -= OnClicked;
        _icon.IsVisible = false;
        _icon.Dispose();
    }
}
