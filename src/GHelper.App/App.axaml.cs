using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GHelper.App.Services;
using GHelper.App.Theme;
using GHelper.App.ViewModels;
using GHelper.App.Views;
using GHelper.Platform;

namespace GHelper.App;

/// <summary>
/// Application object. Builds the shell: view model, main window, window coordinator, tray icon and the
/// <see cref="IUiHost"/> that Core talks to. Nothing here depends on hardware; the application logic plugs in through
/// <see cref="MainViewModel.Backend"/> and <see cref="Host"/>.
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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Instance = this;
    }

    public override void OnFrameworkInitializationCompleted()
    {
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
        ViewModel.Backend = new ShellBackend(Host);

        // single instance: a second start asks the first to show its window and exits
        if (!AcquireSingleInstance())
        {
            desktop.Shutdown(0);
            return;
        }

        AttachToCore(Host);

        Tray = new TrayService(this, ViewModel, (check, tray) => Host.ToggleSettings(check, tray));

        desktop.Exit += (_, _) =>
        {
            Tray?.Dispose();
            try { PlatformServices.Current.Process.ReleaseSingleInstance(); } catch { /* not registered / unsupported */ }
        };

        // legacy: show the window when started by hand (not by the logon task) or with an action argument
        if (options.OpensWindow)
            Host.ToggleSettings(false);
    }

    private bool AcquireSingleInstance()
    {
        try
        {
            return PlatformServices.Current.Process.AcquireSingleInstance("GHelper", () => Host?.ToggleSettings(false));
        }
        catch (Exception ex)
        {
            AppLog.Write("Single instance check unavailable: " + ex.GetType().Name);
            return true;
        }
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
