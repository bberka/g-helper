using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using GHelper.App.ViewModels;
using GHelper.App.Views;
using Xunit;

namespace GHelper.App.Tests;

/// <summary>
/// Renders the main window with sample data to PNG files (artifacts folder next to the test binaries, or
/// GHELPER_UI_SNAPSHOT_DIR) so the layout can be inspected, and asserts the basics: it lays out, has a sane size and is
/// not blank. The images are reviewed by eye during development; they are not golden files.
/// </summary>
public class MainWindowSnapshotTests
{
    internal static string ArtifactDir
    {
        get
        {
            string? dir = Environment.GetEnvironmentVariable("GHELPER_UI_SNAPSHOT_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(AppContext.BaseDirectory, "artifacts");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    internal static WriteableBitmap Render(Window window, string name)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame rendered");
        frame.Save(Path.Combine(ArtifactDir, name + ".png"));
        return frame;
    }

    private static MainWindow CreateWindow(ThemeVariant theme, bool ally = false)
    {
        Application.Current!.RequestedThemeVariant = theme;
        var window = new MainWindow { DataContext = MainViewModel.CreateSample(ally) };
        return window;
    }

    private static bool HasVariance(WriteableBitmap bmp)
    {
        using var fb = bmp.Lock();
        int w = fb.Size.Width, h = fb.Size.Height;
        var seen = new HashSet<int>();
        unsafe
        {
            var p = (int*)fb.Address;
            int stride = fb.RowBytes / 4;
            for (int y = 0; y < h; y += 3)
                for (int x = 0; x < w; x += 3)
                    seen.Add(p[y * stride + x]);
        }
        return seen.Count > 20;
    }

    [AvaloniaFact]
    public void MainWindow_renders_dark()
    {
        var window = CreateWindow(ThemeVariant.Dark);
        var frame = Render(window, "main-dark");

        Assert.Equal(425, window.Width);
        Assert.InRange(frame.PixelSize.Height, 500, 1200);
        Assert.True(HasVariance(frame), "rendered window is blank");
        window.Close();
    }

    [AvaloniaFact]
    public void MainWindow_renders_light()
    {
        var window = CreateWindow(ThemeVariant.Light);
        var frame = Render(window, "main-light");

        Assert.InRange(frame.PixelSize.Height, 500, 1200);
        Assert.True(HasVariance(frame));
        window.Close();
    }

    [AvaloniaFact]
    public void MainWindow_renders_ally_layout()
    {
        var window = CreateWindow(ThemeVariant.Dark, ally: true);
        var frame = Render(window, "main-ally-dark");

        // controller panel + AutoTDP strip make the window taller than the laptop layout
        Assert.True(HasVariance(frame));
        window.Close();
    }

    [AvaloniaFact]
    public void Peripherals_and_rear_light_panels_render_when_enabled()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var vm = MainViewModel.CreateSample();
        vm.PeripheralsVisible = true;
        foreach (var p in vm.Peripherals) p.IsVisible = true;
        vm.Peripherals[0].Text = "ROG Harpe Ace\n85%";
        vm.Peripherals[1].Text = "ROG Strix Scope";
        vm.Peripherals[2].Text = "ROG Delta";
        vm.RearSectionVisible = true;
        vm.RearModes.Add(new ChoiceItem(0, "Static"));
        vm.RearModes.Add(new ChoiceItem(1, "Breathe"));
        vm.SelectedRearMode = vm.RearModes[0];
        vm.RearSwatch = Avalonia.Media.Colors.Orange;
        vm.Armoury.IsVisible = true;
        vm.EnergySaver.IsVisible = true;
        vm.Donate.Badge = 3;
        vm.VisualiseMode(AsusACPI.PerformanceTurbo, AsusACPI.PerformanceTurbo);
        vm.VisualiseScreen(true, true, 60, 120, 0, false, 2, -1, false, false, 1, -1);

        var window = new MainWindow { DataContext = vm };
        var frame = Render(window, "main-dark-full");
        Assert.True(HasVariance(frame));
        window.Close();
    }
}
