using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using GHelper.Platform;
using GHelper.Platform.Linux;
using GHelper.Platform.Windows;

namespace GHelper.App;

/// <summary>
/// Process entry point. Order matters: command line first (so <c>--version</c> never creates anything), then the
/// platform services are registered - before anything touches AppConfig or Logger, both resolve the config directory
/// through <see cref="PlatformServices"/> - and only then Avalonia is started.
/// </summary>
public static class Entry
{
    /// <summary>
    /// The legacy "charge" action (restore battery limit + keyboard backlight and exit, no UI), run by the scheduled
    /// logon task. Returns the process exit code. Defaults to <see cref="RunHeadlessCharge"/>; tests may replace it.
    /// </summary>
    public static Func<LaunchOptions, int>? HeadlessCharge { get; set; }

    [STAThread]
    public static int Main(string[] args)
    {
        var options = LaunchOptions.Parse(args);

        if (options.ShowVersion)
        {
            ConsoleAttach.Attach();
            Console.WriteLine(AppVersion.Current);
            return 0;
        }

        LaunchOptions.Current = options;

        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Write("Unhandled: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Write("Unobserved: " + e.Exception);
            e.SetObserved();
        };

        RegisterPlatform();
        ApplyLanguage();

        if (options.IsHeadless)
        {
            try
            {
                return (HeadlessCharge ?? RunHeadlessCharge).Invoke(options);
            }
            catch (Exception ex)
            {
                AppLog.Write("Charge: " + ex);
                return 1;
            }
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            AppLog.Write("Fatal: " + ex);
            return 1;
        }
    }

    /// <summary>
    /// Core's one shot start-up task (no windows, no tray): battery limit + keyboard backlight. <c>Program.Ui</c> is the
    /// null host here, so the final <c>Ui.Quit()</c> of the charge routine is a no-op and the process just returns.
    /// </summary>
    public static int RunHeadlessCharge(LaunchOptions options)
    {
        Program.Charge();
        return 0;
    }

    /// <summary>Also used by the designer and the headless tests.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // WM_CLASS (instance and class) of every window on X11 / XWayland: tiling window manager rules match it
            // (packaging/linux/README.md: for_window [class="ghelper"] floating enable)
            .With(new X11PlatformOptions { WmClass = WindowClass })
            .LogToTrace();

    /// <summary>X11 WM_CLASS / app id of all G-Helper windows.</summary>
    public const string WindowClass = "ghelper";

    /// <summary>WindowsPlatform on Windows, LinuxPlatform on everything else (macOS is out of scope but gets the generic Linux code paths).</summary>
    public static void RegisterPlatform()
    {
        if (PlatformServices.IsRegistered) return;
        IPlatform platform = OperatingSystem.IsWindows() ? new WindowsPlatform() : new LinuxPlatform();
        PlatformServices.Register(platform);
    }

    /// <summary>Legacy: config key "language" overrides the OS UI language. Never fails start-up.</summary>
    private static void ApplyLanguage()
    {
        try
        {
            string? language = AppConfig.GetString("language");
            CultureInfo culture;
            if (!string.IsNullOrEmpty(language))
            {
                culture = CultureInfo.GetCultureInfo(language);
            }
            else
            {
                culture = CultureInfo.CurrentUICulture;
                if (culture.ToString() == "kr") culture = CultureInfo.GetCultureInfo("ko");
            }

            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (Exception ex)
        {
            AppLog.Write("Language: " + ex.Message);
        }
    }
}

/// <summary>Version of the executable (Directory.Build.props &lt;Version&gt;).</summary>
public static class AppVersion
{
    public static string Current
    {
        get
        {
            var asm = typeof(AppVersion).Assembly;
            string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info))
            {
                int plus = info.IndexOf('+');
                return plus >= 0 ? info[..plus] : info;
            }
            return asm.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }
}

/// <summary>
/// Logger facade for the shell: the Core logger resolves its directory through the platform services, so it must
/// never take the UI down when they are not usable (design time, tests, unfinished platform).
/// </summary>
public static class AppLog
{
    public static void Write(string message)
    {
        try
        {
            if (PlatformServices.IsRegistered) Logger.WriteLine(message);
            else System.Diagnostics.Debug.WriteLine(message);
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine(message);
        }
    }
}
