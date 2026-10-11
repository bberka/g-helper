using Avalonia.Threading;
using GHelper.App.ViewModels;
using GHelper.Properties;

namespace GHelper.App.Views.Peripherals;

/// <summary>
/// Shared plumbing of the three peripheral view models: UI thread marshalling (injectable so the logic runs inline in
/// tests), the "device is being re-read, do not write back" flag, the close request and the battery readout.
/// </summary>
public abstract class PeripheralViewModelBase : ObservableObject, IDisposable
{
    private bool _batteryVisible;
    private string _batteryText = "";
    private bool _batteryCharging;
    private int _loading;
    private bool _disposed;

    protected PeripheralViewModelBase(Action<Action>? post = null, Action<Action>? background = null)
    {
        Post = post ?? DefaultPost;
        Background = background ?? DefaultBackground;
    }

    /// <summary>Marshals device events (raised on USB threads) to the UI thread.</summary>
    protected Action<Action> Post { get; }

    /// <summary>Runs device IO that should not block the UI (legacy Task.Run).</summary>
    protected Action<Action> Background { get; }

    public abstract string Title { get; }

    /// <summary>The device went away or stopped answering: the window should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>True when closing was requested before the window could subscribe (device not ready while loading).</summary>
    public bool CloseWasRequested { get; private set; }

    protected void RequestClose()
    {
        if (_disposed) return;
        CloseWasRequested = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True while the view is refreshed from the device: setters must not write back then.</summary>
    protected bool Loading => _loading > 0;

    protected void BeginLoading() => _loading++;

    protected void EndLoading() => _loading--;

    protected bool IsDisposed => _disposed;

    public bool BatteryVisible { get => _batteryVisible; protected set => Set(ref _batteryVisible, value); }

    public string BatteryText { get => _batteryText; protected set => Set(ref _batteryText, value); }

    public bool BatteryCharging
    {
        get => _batteryCharging;
        protected set
        {
            if (Set(ref _batteryCharging, value)) Raise(nameof(BatteryNotCharging));
        }
    }

    public bool BatteryNotCharging => !_batteryCharging;

    /// <summary>"(Charging)" next to the percentage.</summary>
    public string ChargingText { get; } = "(" + Strings.Charging + ")";

    /// <summary>Subscribe to device events (legacy: the Shown handler). Idempotent.</summary>
    public virtual void Start() { }

    public virtual void Dispose() => _disposed = true;

    private static void DefaultPost(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private static void DefaultBackground(Action action) => Task.Run(() =>
    {
        try { action(); }
        catch (Exception ex) { Logger.WriteLine("Peripheral IO failed: " + ex.Message); }
    });
}
