using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GHelper.App.Controls;
using GHelper.App.Services;
using GHelper.App.ViewModels;
using GHelper.App.Views;
using GHelper.App.Views.Extra;
using GHelper.App.Views.Fans;
using GHelper.App.Views.Headset;
using GHelper.App.Views.Keyboard;
using GHelper.App.Views.Mouse;
using GHelper.App.Views.Overlay;
using GHelper.App.Views.Updates;
using GHelper.App.Tests.Peripherals;
using GHelper.Peripherals.Mouse;
using Xunit;

namespace GHelper.App.Tests;

/// <summary>
/// Every window of the app must be fixed size (tiling window managers and Windows snap must not resize it):
/// Width / Height set, Min == Max, not resizable / maximizable / minimizable, SizeToContent off.
/// What a tiling WM does with it cannot be exercised headless; these tests pin the properties the native layer
/// turns into WM_NORMAL_HINTS min == max (X11) and a frame without sizing / maximize / minimize (Win32).
/// </summary>
public class WindowSizingTests
{
    public WindowSizingTests()
    {
        SideWindowEnv.Ensure();
        WindowSizing.Install();
    }

    private static Window Create(string name)
    {
        switch (name)
        {
            case "main": return new MainWindow { DataContext = MainViewModel.CreateSample() };
            case "fans": return new FansWindow(0, SampleFansBackend.Create());
            case "extra": return new ExtraWindow(0, SampleExtraBackend.Create());
            case "matrix": return new MatrixWindow(SideWindowEnv.MatrixControl().Control);
            case "slash": return new SlashWindow(SideWindowEnv.SlashControl().Control);
            case "handheld": return new HandheldWindow(new NoActions());
            case "overlay":
                return new OverlayConfigWindow(new MemoryOverlayStore(), () => { }, () => { }, () => { });
            case "updates":
                return new UpdatesWindow(new GHelper.UpdatesController(), () => new UpdatesTarget("1", "X", false),
                    () => "SN", _ => { }, autoLoad: false);
            case "mouse": TestPlatform.Ensure(); return new MouseWindow(new FakeMouse());
            case "keyboard": TestPlatform.Ensure(); return new KeyboardWindow(new GHelper.Peripherals.Keyboard.Models.StrixScopeII { TestMode = true });
            case "headset": TestPlatform.Ensure(); return new HeadsetWindow(new FakeHeadset());
            default: throw new ArgumentException(name);
        }
    }

    private sealed class NoActions : IControllerActions
    {
        public void SetDeadzones() { }
        public void ApplyMode() { }
        public void DisableController(bool disabled) { }
        public void SetVibrationMode() { }
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertFixed(Window w, string name)
    {
        Assert.True(WindowSizing.IsFixed(w), name + " not under the policy");

        Assert.False(double.IsNaN(w.Width) || double.IsInfinity(w.Width), name + " Width");
        Assert.False(double.IsNaN(w.Height) || double.IsInfinity(w.Height), name + " Height");
        Assert.True(w.Width > 100 && w.Height > 100, $"{name} {w.Width}x{w.Height}");

        Assert.Equal(w.Width, w.MinWidth);
        Assert.Equal(w.Width, w.MaxWidth);
        Assert.Equal(w.Height, w.MinHeight);
        Assert.Equal(w.Height, w.MaxHeight);

        Assert.False(w.CanResize, name + " CanResize");
        Assert.False(w.CanMaximize, name + " CanMaximize");
        Assert.False(w.CanMinimize, name + " CanMinimize");
        Assert.Equal(SizeToContent.Manual, w.SizeToContent);
        Assert.Equal(WindowState.Normal, w.WindowState);

        // the native window really has that client size
        Assert.Equal(w.Width, w.ClientSize.Width);
        Assert.Equal(w.Height, w.ClientSize.Height);
    }

    [Fact]
    public void Every_side_window_is_registered()
    {
        foreach (var name in new[] { "fans", "extra", "matrix", "slash", "handheld", "overlay", "updates", "mouse", "keyboard", "headset" })
            Assert.True(WindowRegistry.IsRegistered(name), name);
    }

    [AvaloniaTheory]
    [InlineData("main")]
    [InlineData("fans")]
    [InlineData("extra")]
    [InlineData("matrix")]
    [InlineData("slash")]
    [InlineData("handheld")]
    [InlineData("overlay")]
    [InlineData("updates")]
    [InlineData("mouse")]
    [InlineData("keyboard")]
    [InlineData("headset")]
    public void Window_is_fixed_size_after_show(string name)
    {
        var window = Create(name);
        try
        {
            window.Show();
            Pump();
            AssertFixed(window, name);

            // and stays that way through more layout passes
            double w = window.Width, h = window.Height;
            window.InvalidateMeasure();
            Pump();
            Assert.Equal(w, window.Width);
            Assert.Equal(h, window.Height);
            AssertFixed(window, name);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Main_window_fits_without_scrolling_when_the_screen_is_big_enough()
    {
        var window = new MainWindow { DataContext = MainViewModel.CreateSample() };
        window.Show();
        Pump();

        var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 1,
            $"extent {scroll.Extent.Height} viewport {scroll.Viewport.Height} window {window.Height}");
        window.Close();
    }

    [AvaloniaFact]
    public void Main_window_is_clamped_to_the_work_area()
    {
        var window = new MainWindow { DataContext = MainViewModel.CreateSample() };
        window.Show();
        Pump();

        var screen = window.Screens.Primary!;
        Assert.True(window.Height <= screen.WorkingArea.Height / screen.Scaling);
        window.Close();
    }

    // ---- behaviour of the policy on a plain side window ---------------------------------------------------

    private static (SideWindow Window, StackPanel Panel) Plain(double width = 320, double content = 200, bool autoHeight = true)
    {
        var panel = new StackPanel { Children = { new Border { Height = content } } };
        var window = new SideWindow { Width = width, Content = panel };
        if (autoHeight) window.SizeToContent = SizeToContent.Height;
        return (window, panel);
    }

    [AvaloniaFact]
    public void Size_follows_the_content_when_it_legitimately_changes()
    {
        var (window, panel) = Plain();
        window.Show();
        Pump();

        Assert.Equal(200, window.Height);
        AssertFixed(window, "plain");

        panel.Children.Add(new Border { Height = 80 });
        Pump();
        Pump();
        Assert.Equal(280, window.Height);
        AssertFixed(window, "grown");

        panel.Children.RemoveAt(1);
        Pump();
        Pump();
        Assert.Equal(200, window.Height);
        AssertFixed(window, "shrunk");

        // a hidden section does not count
        var section = new Border { Height = 60 };
        panel.Children.Add(section);
        Pump();
        Pump();
        Assert.Equal(260, window.Height);
        section.IsVisible = false;
        Pump();
        Pump();
        Assert.Equal(200, window.Height);
        window.Close();
    }

    [AvaloniaFact]
    public void Content_taller_than_the_work_area_is_clamped()
    {
        var (window, _) = Plain(content: 5000);
        window.Show();
        Pump();

        var screen = window.Screens.Primary!;
        Assert.True(window.Height < 5000);
        Assert.True(window.Height <= screen.WorkingArea.Height / screen.Scaling);
        AssertFixed(window, "tall");
        window.Close();
    }

    [AvaloniaFact]
    public void Attempts_to_loosen_the_window_are_reverted()
    {
        var (window, _) = Plain();
        window.Show();
        Pump();
        double h = window.Height;

        window.MaxHeight = 4000;
        window.MinHeight = 10;
        window.MaxWidth = 4000;
        window.CanResize = true;
        window.CanMaximize = true;
        window.WindowState = WindowState.Maximized;
        window.SizeToContent = SizeToContent.Height;
        Pump();

        Assert.Equal(h, window.Height);
        Assert.Equal(h, window.MaxHeight);
        Assert.Equal(h, window.MinHeight);
        Assert.Equal(320, window.MaxWidth);
        AssertFixed(window, "reverted");
        window.Close();
    }

    [AvaloniaFact]
    public void Assigning_width_and_height_moves_the_fixed_size()
    {
        var (window, _) = Plain(autoHeight: false);
        window.Height = 150;
        window.Show();
        Pump();
        AssertFixed(window, "explicit");
        Assert.Equal(150, window.Height);

        window.Width = 400;
        window.Height = 260;
        Pump();
        Assert.Equal(400, window.MinWidth);
        Assert.Equal(400, window.MaxWidth);
        Assert.Equal(260, window.MinHeight);
        Assert.Equal(260, window.MaxHeight);
        AssertFixed(window, "resized");
        window.Close();
    }

    [AvaloniaFact]
    public void Own_minimum_size_is_respected()
    {
        var (window, _) = Plain(width: 200, content: 120);
        window.MinWidth = 260;
        window.MinHeight = 180;
        window.Show();
        Pump();

        Assert.Equal(260, window.Width);
        Assert.Equal(180, window.Height);
        AssertFixed(window, "min");
        window.Close();
    }

    [AvaloniaFact]
    public void Install_covers_windows_that_do_not_opt_in()
    {
        var plain = new Window
        {
            Width = 280,
            SizeToContent = SizeToContent.Height,
            Content = new Border { Height = 140 },
        };
        plain.Show();
        Pump();

        AssertFixed(plain, "plain Window");
        Assert.Equal(140, plain.Height);
        plain.Close();
    }

    [AvaloniaFact]
    public void Toast_and_color_picker_are_fixed()
    {
        var toast = new ToastWindow();
        toast.ShowToast("Performance mode: Turbo", "eco");
        Pump();
        Assert.Equal(toast.Width, toast.MinWidth);
        Assert.Equal(toast.Width, toast.MaxWidth);
        Assert.Equal(toast.Height, toast.MinHeight);
        Assert.Equal(toast.Height, toast.MaxHeight);
        Assert.False(toast.CanResize);

        // a longer text widens it: the limits follow
        toast.ShowToast("A much longer message than before", null);
        Pump();
        Assert.Equal(toast.Width, toast.MaxWidth);
        Assert.Equal(toast.Width, toast.MinWidth);
        toast.Close();

        var picker = new ColorPickerWindow(Avalonia.Media.Colors.Red);
        picker.Show();
        Pump();
        AssertFixed(picker, "colour picker");
        picker.Close();
    }

    [AvaloniaFact]
    public void Window_Fit_is_idempotent_and_does_not_change_a_settled_window()
    {
        var (window, _) = Plain();
        window.Show();
        Pump();

        var before = new Size(window.Width, window.Height);
        for (int i = 0; i < 5; i++) WindowSizing.Fit(window);
        Pump();
        Assert.Equal(before, new Size(window.Width, window.Height));
        window.Close();
    }

    [Fact]
    public void ToolkitInternalWindowsAreLeftAlone()
    {
        // e.g. Avalonia.Win32's tray menu popup: it sizes itself, locking it broke the tray context menu
        Assert.True(WindowSizing.IsToolkitInternal(typeof(Avalonia.Controls.Button)));
        Assert.False(WindowSizing.IsToolkitInternal(typeof(Avalonia.Controls.Window)));
        Assert.False(WindowSizing.IsToolkitInternal(typeof(WindowSizingTests)));
        Assert.False(WindowSizing.IsToolkitInternal(typeof(GHelper.App.Views.MainWindow)));
    }
}
