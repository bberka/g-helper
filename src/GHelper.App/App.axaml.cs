using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GHelper.App.Backend;
using GHelper.App.Services;
using GHelper.App.Theme;
using GHelper.App.ViewModels;
using GHelper.App.Views;
using GHelper.Platform;

namespace GHelper.App;

/// <summary>
/// Application object. Builds the shell: view model, main window, window coordinator, tray icon and the
/// <see cref="IUiHost"/> that Core talks to, then starts Core (<c>Program.Start</c>). The application logic is the
/// <see cref="MainBackend"/>; everything degrades to a no-op without hardware.
/// </summary>
public partial class App : Application
{
    /// <summary>Set once the shell is up (desktop lifetime only; null at design time / in tests).</summary>
    public static App? Instance { get; private set; }

    public MainViewModel ViewModel { get; private set; } = null!;
    public MainWindow? MainWindow { get; private set; }
    public WindowCoordinator? Windows { get; private set; }
    public AvaloniaUiHost? Host { get; private set; }
    public TrayService? Tray { get; private set; }
    public MainBackend? Backend { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Instance = this;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // every window of the app (colour picker, toast, dialogs ...) gets the fixed size policy when it is shown
        WindowSizing.Install();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            StartShell(desktop);

        base.OnFrameworkInitializationCompleted();
    }

    private void StartShell(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var options = LaunchOptions.Current;

        UiSettings.LoadFromConfig();
        AppTheme.Apply(UiSettings.UiMode, UiSettings.Theme);

        ViewModel = new MainViewModel();
        MainWindow = new MainWindow { DataContext = ViewModel };
        MainWindow.Topmost = UiSettings.Topmost;

        Windows = new WindowCoordinator(MainWindow);
        Host = new AvaloniaUiHost(ViewModel, () => Windows);

        // the application logic: Settings.cs behaviour on top of GHelper.Core
        Backend = new MainBackend(ViewModel, new AvaloniaMainShell(MainWindow));
        ViewModel.Backend = Backend;
        Host.ActionHandler = Backend;

        // legacy SettingsForm_VisibleChanged: sensors timer, peripheral battery, update check
        MainWindow.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Visual.IsVisibleProperty) Backend.OnWindowVisibilityChanged(MainWindow.IsVisible);
        };

        // Core talks to the UI through Program.Ui; it must be in place before Program.Start
        AttachToCore(Host);
        Backend.Initialize();

        Tray = new TrayService(this, ViewModel, (check, tray) => Host.ToggleSettings(check, tray));

        desktop.Exit += (_, _) => Shutdown();

        // Core start-up (hardware init, auto modes, event hooks) runs off the UI thread; results come back through
        // Program.Ui.Refresh / Post. Single instance handling is part of it (Program.Start).
        Dispatcher.UIThread.Post(() => Task.Run(() => RunCoreStart(options)), DispatcherPriority.Background);
    }

    /// <summary>The legacy first-argument action as Core's <c>Program.Start</c> expects it.</summary>
    internal static string[] StartArguments(LaunchOptions options) => options.Action switch
    {
        LaunchAction.Charge => new[] { "charge" },
        LaunchAction.Cpu => new[] { "cpu" },
        LaunchAction.Gpu => new[] { "gpu" },
        LaunchAction.Services => new[] { "services" },
        LaunchAction.Uv => new[] { "uv" },
        LaunchAction.Colors => new[] { "colors" },
        _ => Array.Empty<string>(),
    };

    private void RunCoreStart(LaunchOptions options)
    {
        bool running = true;

        try
        {
            // the overlay is a window: it has to be created on the UI thread
            AppHooks.CreateHardwareOverlay ??= () => Dispatcher.UIThread.Invoke(Views.Overlay.OverlayHost.Create);

            if (Program.hardwareOverlay is null)
                Program.hardwareOverlay = AppHooks.CreateHardwareOverlay?.Invoke();

            running = Program.Start(StartArguments(options));
        }
        catch (Exception ex)
        {
            // keep the shell alive: the window and the tray still work, so the user can see / report the problem
            AppLog.Write("Core start failed: " + ex);
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!running)
            {
                // already running, no ACPI, ...: Core asked to quit
                Host?.Quit();
                return;
            }

            // "--show": legacy only opened the window when started by hand / with an action argument
            if (options.OpensWindow && MainWindow is { IsVisible: false })
                Host?.ToggleSettings(false);
        });
    }

    private int _shutdown;

    /// <summary>Application exit (legacy Application.ApplicationExit + the cleanup of the Quit button). Runs once.</summary>
    private void Shutdown()
    {
        if (Interlocked.Exchange(ref _shutdown, 1) == 1) return;

        try { Tray?.Dispose(); } catch (Exception ex) { AppLog.Write("Tray dispose: " + ex.Message); }

        MainBackend.ReleaseDevices();

        try { Program.OnExit(); }
        catch (Exception ex) { AppLog.Write("Exit: " + ex.Message); }

        Backend?.Dispose();

        try { PlatformServices.Current.Process.ReleaseSingleInstance(); } catch { /* not registered / unsupported */ }
    }

    /// <summary>
    /// Give Core its UI (Program.Ui). Core's static initialisation reads the configuration, which needs working platform
    /// services, so this must never take the UI down.
    /// </summary>
    private static void AttachToCore(IUiHost host)
    {
        try
        {
            Program.Ui = host;
        }
        catch (Exception ex)
        {
            AppLog.Write("Core UI hook unavailable: " + ex.GetType().Name);
        }
    }
}
