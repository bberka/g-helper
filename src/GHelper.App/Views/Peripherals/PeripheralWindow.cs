using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using GHelper.App.Services;

namespace GHelper.App.Views.Peripherals;

/// <summary>
/// Common frame of the mouse / keyboard / headset windows: fixed size fitted to the content (capability driven sections
/// come and go: <see cref="WindowSizing"/> re-fits it and <see cref="SideWindow"/> keeps it lined up with the main
/// window), never taller than the work area (the content scrolls then - legacy MaximumSize = main window height),
/// closes when the device goes away, releases the device listeners when closed.
/// </summary>
public abstract class PeripheralWindow : SideWindow
{
    private PeripheralViewModelBase? _viewModel;

    protected PeripheralWindow()
    {
        // no Width / Height: both follow the content (WindowSizing fits them before the window is shown)
        Closed += (_, _) => _viewModel?.Dispose();
    }

    /// <summary>Wire the window to its view model: title, close requests, device event lifetime.</summary>
    protected void Attach(PeripheralViewModelBase viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Title = viewModel.Title;
        viewModel.CloseRequested += (_, _) => Dispatcher.UIThread.Post(Close);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (_viewModel is { } vm)
        {
            vm.Start();
            if (vm.CloseWasRequested) Dispatcher.UIThread.Post(Close);
        }
    }
}
