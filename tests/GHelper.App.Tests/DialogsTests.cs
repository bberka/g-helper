using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GHelper.App.Controls;
using GHelper.App.Services;
using GHelper.App.ViewModels;
using GHelper.App.Views;
using GHelper.Platform;
using Xunit;

namespace GHelper.App.Tests;

/// <summary>
/// Confirm / Alert are an overlay inside the owning window (backdrop + card), not a window of their own, and stay
/// synchronous for Core callers.
/// </summary>
public class DialogsTests
{
    public DialogsTests()
    {
        SideWindowEnv.Ensure();
        WindowSizing.Install();
    }

    private static readonly List<Window> OpenedWindows = new();
    private static bool _tracking;

    /// <summary>Records every window that becomes visible (a dialog that is a window shows up here).</summary>
    private static void TrackWindows()
    {
        if (_tracking) return;
        _tracking = true;
        Visual.IsVisibleProperty.Changed.AddClassHandler<Window>((w, e) =>
        {
            if (e.NewValue is true) lock (OpenedWindows) OpenedWindows.Add(w);
        });
    }

    private static Window[] Opened()
    {
        lock (OpenedWindows) return OpenedWindows.ToArray();
    }

    private static (MainWindow Main, WindowCoordinator Coordinator, AvaloniaUiHost Host) Open(ThemeVariant? theme = null)
    {
        if (theme is not null) Application.Current!.RequestedThemeVariant = theme;
        var vm = MainViewModel.CreateSample();
        var main = new MainWindow { DataContext = vm };
        main.Show();
        Dispatcher.UIThread.RunJobs();

        var coordinator = new WindowCoordinator(main);
        var host = new AvaloniaUiHost(vm, () => coordinator);
        return (main, coordinator, host);
    }

    private static T? Find<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void Confirm_is_an_overlay_inside_the_window_and_returns_yes()
    {
        TrackWindows();
        var (main, _, host) = Open();
        int windowsBefore = Opened().Length;
        bool seen = false;
        string? title = null, message = null;

        Dispatcher.UIThread.Post(() =>
        {
            var card = Find<Border>(main, InAppDialog.CardName);
            seen = card is not null;
            if (card?.Child is StackPanel sp)
            {
                title = (sp.Children[0] as TextBlock)?.Text;
                message = (sp.Children[1] as TextBlock)?.Text;
            }

            // no new window: the card is part of the main window's visual tree, the backdrop covers its content
            Assert.Empty(main.OwnedWindows);
            Assert.Equal(windowsBefore, Opened().Length);
            Assert.NotNull(Find<Grid>(main, InAppDialog.OverlayName));
            Click(Find<Button>(main, InAppDialog.YesName)!);
        });

        bool result = host.Confirm("Ultimate mode", "Changing GPU mode to Ultimate requires reboot. Continue?");

        Assert.True(result);
        Assert.True(seen);
        Assert.Equal("Ultimate mode", title);
        Assert.Equal("Changing GPU mode to Ultimate requires reboot. Continue?", message);
        Assert.Null(Find<Border>(main, InAppDialog.CardName)); // gone again
        Assert.Equal(windowsBefore, Opened().Length);
        main.Close();
    }

    [AvaloniaFact]
    public void No_button_and_escape_answer_false_enter_answers_true()
    {
        var (main, _, host) = Open();

        Dispatcher.UIThread.Post(() => Click(Find<Button>(main, InAppDialog.NoName)!));
        Assert.False(host.Confirm("t", "m"));

        Dispatcher.UIThread.Post(() => main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null));
        Assert.False(host.Confirm("t", "m"));

        Dispatcher.UIThread.Post(() => main.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null));
        Assert.True(host.Confirm("t", "m"));

        // Tab moves to "No", Enter then activates it
        Dispatcher.UIThread.Post(() =>
        {
            main.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            main.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        });
        Assert.False(host.Confirm("t", "m"));
        main.Close();
    }

    [AvaloniaFact]
    public void Alert_shows_ok_only_and_keys_do_not_reach_the_window_behind()
    {
        var (main, _, host) = Open();
        bool ok = false, noButton = false;

        Dispatcher.UIThread.Post(() =>
        {
            ok = Find<Button>(main, InAppDialog.OkName) is not null;
            noButton = Find<Button>(main, InAppDialog.NoName) is not null;

            // a key that would act on the window behind is swallowed while the card is up
            var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, Source = main };
            main.RaiseEvent(args);
            Assert.True(args.Handled);
            Assert.NotNull(Find<Button>(main, InAppDialog.OkName)); // still up

            Click(Find<Button>(main, InAppDialog.OkName)!);
        });

        host.Alert("Already running", "G-Helper is already running");
        Assert.True(ok);
        Assert.False(noButton);
        main.Close();
    }

    [AvaloniaFact]
    public void Dialog_opens_in_the_focused_side_window()
    {
        var (main, coordinator, host) = Open();

        var side = new SideWindow { Width = 320, Content = new StackPanel { Children = { new Border { Height = 300 } } } };
        coordinator.Register(side);
        side.Show();
        Dispatcher.UIThread.RunJobs();
        side.Activate();
        Dispatcher.UIThread.RunJobs();

        bool inSide = false, inMain = false;
        Dispatcher.UIThread.Post(() =>
        {
            inSide = Find<Border>(side, InAppDialog.CardName) is not null;
            inMain = Find<Border>(main, InAppDialog.CardName) is not null;
            var owner = coordinator.DialogOwner()!;
            Click(Find<Button>(owner, InAppDialog.YesName)!);
        });

        Assert.True(host.Confirm("t", "m"));
        Assert.True(inSide ^ inMain, "exactly one window hosts the dialog");
        Assert.Equal(coordinator.DialogOwner() == side, inSide);

        side.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void Closing_the_owner_resolves_a_pending_dialog()
    {
        var (main, coordinator, _) = Open();

        var side = new SideWindow { Width = 320, Content = new StackPanel { Children = { new Border { Height = 300 } } } };
        coordinator.Register(side);
        side.Show();
        side.Activate();
        Dispatcher.UIThread.RunJobs();

        // closing the window under the dialog must not strand the caller (Core would hang forever)
        bool hosted = false;
        Dispatcher.UIThread.Post(() =>
        {
            hosted = Find<Border>(side, InAppDialog.CardName) is not null;
            side.Close();
        });
        Assert.True(InAppDialog.TryRun(side, "t", "m", confirm: true, out bool answer));
        Assert.True(hosted);
        Assert.False(answer);
        main.Close();
    }

    [AvaloniaFact]
    public void Hiding_the_owner_moves_the_question_to_a_window()
    {
        TrackWindows();
        var (main, _, host) = Open();

        // the main window is hidden (tray click) while the question is open: the caller stays blocked, the question
        // is now a window the user can still answer
        Dispatcher.UIThread.Post(() => main.Hide());
        var answer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        answer.Tick += (_, _) =>
        {
            if (Opened().OfType<MessageDialog>().LastOrDefault(w => w.IsVisible) is not { } window) return;
            answer.Stop();
            Assert.Null(Find<Border>(main, InAppDialog.CardName));
            window.Close();
        };
        answer.Start();

        Assert.False(host.Confirm("t", "m"));
        answer.Stop();
    }

    [AvaloniaFact]
    public void Without_a_visible_window_the_old_window_dialog_is_the_fallback()
    {
        TrackWindows();
        var vm = MainViewModel.CreateSample();
        var main = new MainWindow { DataContext = vm }; // never shown: tray only
        var host = new AvaloniaUiHost(vm, () => new WindowCoordinator(main));

        Window? shown = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            shown = Opened().OfType<MessageDialog>().LastOrDefault(w => w.IsVisible);
            if (shown is null) return;
            timer.Stop();
            ((MessageDialog)shown).Close();
        };
        timer.Start();

        bool result = host.Confirm("t", "m");
        timer.Stop();

        Assert.False(result);
        Assert.NotNull(shown);
    }

    [AvaloniaFact]
    public void Calls_from_other_threads_block_until_answered()
    {
        var (main, _, host) = Open();

        // answer once the card exists; a dispatcher job, because the dialog runs a nested frame on the UI thread
        var answer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        answer.Tick += (_, _) =>
        {
            if (Find<Button>(main, InAppDialog.YesName) is not { } yes) return;
            answer.Stop();
            Click(yes);
        };
        answer.Start();

        var task = Task.Run(() => host.Confirm("t", "m"));

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        answer.Stop();
        Assert.True(task.IsCompleted);
        Assert.True(task.Result);
        main.Close();
    }

    [AvaloniaFact]
    public void Dialog_card_is_accessible_and_themed()
    {
        var (main, _, host) = Open();
        string? cardName = null, yesName = null;
        Avalonia.Media.Color? background = null;

        Dispatcher.UIThread.Post(() =>
        {
            var card = Find<Border>(main, InAppDialog.CardName)!;
            cardName = Avalonia.Automation.AutomationProperties.GetName(card);
            yesName = Avalonia.Automation.AutomationProperties.GetName(Find<Button>(main, InAppDialog.YesName)!);
            background = (card.Background as Avalonia.Media.ISolidColorBrush)?.Color;
            Click(Find<Button>(main, InAppDialog.NoName)!);
        });

        host.Confirm("Armoury Crate", "Armoury Crate is active, download official uninstaller app?");
        Assert.Equal("Armoury Crate", cardName);
        Assert.False(string.IsNullOrEmpty(yesName));
        Assert.NotNull(background);
        Assert.Equal(255, background!.Value.A);
        main.Close();
    }

    // ---- pictures ------------------------------------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData("dark")]
    [InlineData("light")]
    public void Render_confirmation_over_the_main_window(string theme)
    {
        var (main, _, host) = Open(theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        Avalonia.Media.Imaging.WriteableBitmap? frame = null;

        Dispatcher.UIThread.Post(() =>
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            frame = main.CaptureRenderedFrame();
            frame?.Save(Path.Combine(MainWindowSnapshotTests.ArtifactDir, $"dialog-confirm-{theme}.png"));
            Click(Find<Button>(main, InAppDialog.NoName)!);
        });

        host.Confirm(GHelper.Properties.Strings.AlertUltimateTitle, GHelper.Properties.Strings.AlertUltimateOn);

        Assert.NotNull(frame);
        Assert.Equal((int)main.Width, frame!.PixelSize.Width);
        main.Close();
    }
}
