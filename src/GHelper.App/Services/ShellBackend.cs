using GHelper.App.ViewModels;
using GHelper.Platform;

namespace GHelper.App.Services;

/// <summary>
/// Backend used until the application logic (wave 2) attaches its own: only the gestures that need nothing but the
/// shell work (Quit). Everything else is ignored, so the UI is fully clickable without hardware.
/// </summary>
public sealed class ShellBackend : IMainBackend
{
    private readonly IUiHost _host;

    public ShellBackend(IUiHost host) => _host = host;

    public void Execute(MainAction action)
    {
        switch (action)
        {
            case MainAction.Quit:
                _host.Quit();
                break;
            default:
                AppLog.Write("UI action (no backend attached): " + action);
                break;
        }
    }

    public void SetValue(MainValue value, int newValue) =>
        AppLog.Write($"UI value (no backend attached): {value} = {newValue}");

    public void RefreshArea(string area, MainViewModel viewModel) { }
}
