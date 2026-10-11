using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using GHelper.App.Services;
using GHelper.App.Views.Fans;

namespace GHelper.App.Views.Extra;

/// <summary>
/// "Extra settings" window (legacy Extra form): hotkey remapping, laptop backlight behaviour, power user options,
/// Asus services. Layout in ExtraWindow.axaml, state and rules in <see cref="ExtraViewModel"/>.
/// </summary>
public partial class ExtraWindow : SideWindow
{
    [ModuleInitializer]
    internal static void RegisterWindow() => WindowRegistry.Register("extra", arg => new ExtraWindow(arg));

    private readonly ExtraViewModel _vm;
    private AvaloniaUiHostSubscription? _subscription;

    /// <summary>Designer / loader constructor.</summary>
    public ExtraWindow() : this(0, null)
    {
    }

    /// <param name="page">1 = services page: toggle the Asus services right after opening (launch argument "services").</param>
    public ExtraWindow(int page) : this(page, null)
    {
    }

    internal ExtraWindow(int page, IExtraBackend? backend)
    {
        InitializeComponent();

        _vm = new ExtraViewModel(backend ?? new CoreExtraBackend());
        DataContext = _vm;

        PictureHelp.PointerPressed += (_, e) => Tap(e, _vm.HelpCommand);
        PictureLog.PointerPressed += (_, e) => Tap(e, _vm.LogCommand);
        PictureScan.PointerPressed += (_, e) => Tap(e, _vm.ScanCommand);

        Opened += async (_, _) =>
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is not null)
                MaxHeight = Math.Max(300, screen.WorkingArea.Height / screen.Scaling);

            _subscription = AvaloniaUiHostSubscription.Create(_vm.OnAreaRefreshed);

            // legacy Keyboard_Shown: InitServices; "--services" additionally toggles them (Program.cs)
            if (page == 1) await _vm.ToggleServicesAsync();
            else await _vm.InitServicesAsync();
        };

        Closed += (_, _) =>
        {
            _subscription?.Dispose();
            _subscription = null;
        };
    }

    internal ExtraViewModel ViewModel => _vm;

    private static void Tap(PointerPressedEventArgs e, System.Windows.Input.ICommand command)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        if (command.CanExecute(null)) command.Execute(null);
        e.Handled = true;
    }
}
