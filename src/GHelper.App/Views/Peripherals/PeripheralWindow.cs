using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using GHelper.App.Services;

namespace GHelper.App.Views.Peripherals;

/// <summary>
/// Common frame of the mouse / keyboard / headset windows: sized to the content (capability driven sections come and
/// go), never taller than the work area (the content scrolls then - legacy MaximumSize = main window height), closes
/// when the device goes away, releases the device listeners when closed.
/// </summary>
public abstract class PeripheralWindow : SideWindow
{
    private PeripheralViewModelBase? _viewModel;
    private bool _positioning;

    protected PeripheralWindow()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
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
        LimitHeightToScreen();
        base.OnOpened(e);

        if (_viewModel is { } vm)
        {
            vm.Start();
            if (vm.CloseWasRequested) Dispatcher.UIThread.Post(Close);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // sections appearing later (button bindings arrive after the first read) change the size: stay next to main
        if (change.Property == ClientSizeProperty && IsVisible && !_positioning)
        {
            _positioning = true;
            Dispatcher.UIThread.Post(() =>
            {
                try { AttachToMain(); }
                finally { _positioning = false; }
            }, DispatcherPriority.Loaded);
        }
    }

    private void LimitHeightToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        double scale = screen.Scaling;
        MaxHeight = Math.Max(300, screen.WorkingArea.Height / scale - 2.0 * WindowPlacement.Margin / scale);
    }
}
