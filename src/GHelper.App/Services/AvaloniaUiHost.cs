using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using GHelper.App.ViewModels;
using GHelper.Platform;

namespace GHelper.App.Services;

/// <summary>
/// <see cref="IUiHost"/> on top of Avalonia: what GHelper.Core calls instead of Program.settingsForm / toast /
/// MessageBox / Invoke. Thread safe: every member may be called from any thread.
/// </summary>
public sealed class AvaloniaUiHost : IUiHost
{
    private readonly MainViewModel _viewModel;
    private readonly Func<WindowCoordinator?> _windows;
    private readonly HashSet<string> _pendingAreas = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pendingLock = new();
    private ToastWindow? _toast;

    public AvaloniaUiHost(MainViewModel viewModel, Func<WindowCoordinator?> windows)
    {
        _viewModel = viewModel;
        _windows = windows;
    }

    /// <summary>Raised on the UI thread for every (coalesced) refresh request, before the backend is asked.</summary>
    public event Action<string>? AreaRefreshed;

    public void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    public void Toast(string text, string? iconKey = null)
    {
        if (UiSettings.DisableOsd) return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _toast ??= new ToastWindow();
                _toast.ShowToast(text, iconKey);
            }
            catch (Exception ex)
            {
                AppLog.Write("Toast: " + ex.Message);
            }
        });
    }

    public bool Confirm(string title, string text) => RunDialog(title, text, confirm: true);

    public void Alert(string title, string text) => RunDialog(title, text, confirm: false);

    private bool RunDialog(string title, string text, bool confirm)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return MessageDialog.Run(_windows()?.Main, title, text, confirm);

        return Dispatcher.UIThread.InvokeAsync(() => MessageDialog.Run(_windows()?.Main, title, text, confirm))
            .GetAwaiter().GetResult();
    }

    public void ToggleSettings(bool checkForFocus = true, bool trayClick = false) =>
        Post(() => _windows()?.Toggle(checkForFocus, trayClick));

    /// <summary>Coalesces bursts: a refresh of the same area queued twice is executed once.</summary>
    public void Refresh(string area)
    {
        if (string.IsNullOrEmpty(area)) return;

        bool schedule;
        lock (_pendingLock)
        {
            schedule = _pendingAreas.Count == 0;
            _pendingAreas.Add(area);
        }

        if (!schedule) return;

        Dispatcher.UIThread.Post(() =>
        {
            string[] areas;
            lock (_pendingLock)
            {
                areas = _pendingAreas.ToArray();
                _pendingAreas.Clear();
            }

            foreach (var a in areas)
            {
                try
                {
                    AreaRefreshed?.Invoke(a);
                    _viewModel.Backend.RefreshArea(a, _viewModel);
                }
                catch (Exception ex)
                {
                    AppLog.Write($"Refresh({a}): {ex.Message}");
                }
            }
        }, DispatcherPriority.Background);
    }

    public void Quit()
    {
        Post(() =>
        {
            _windows()?.Main.AllowClose();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        });
    }
}
