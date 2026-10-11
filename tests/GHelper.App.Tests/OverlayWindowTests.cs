using System.Drawing;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using GHelper.App.Views.Overlay;
using GHelper.Overlay;
using Xunit;

namespace GHelper.App.Tests;

/// <summary>
/// Overlay: layout computation (pure), placement rules, and headless renders of the overlay window and the settings
/// window to PNG (see <c>MainWindowSnapshotTests.ArtifactDir</c>) for visual review.
/// </summary>
public class OverlayWindowTests
{
    // ---- layout / placement (no UI) ---------------------------------------------------------------------

    [Theory]
    [InlineData(OverlayMode.Light, false, false, OverlayLayout.BaseLightWidth)]
    [InlineData(OverlayMode.Default, false, false, OverlayLayout.BaseWidth)]
    [InlineData(OverlayMode.Full, false, false, OverlayLayout.BaseFullWidth)]
    [InlineData(OverlayMode.Complete, false, false, OverlayLayout.BaseCompleteWidth)]
    public void Preset_layout_width_matches_legacy_constants(OverlayMode mode, bool battery, bool names, int baseWidth)
    {
        var frame = OverlayFrame.Sample(mode, names, battery);
        var layout = frame.Layout;

        // each column is truncated to whole pixels on its own, so the sum can be a few pixels below base * scale
        int expected = (int)(baseWidth * OverlayLayout.BaseScale);
        Assert.InRange(layout.Width, expected - 20, expected);
        Assert.Equal(OverlayLayout.InitialSize(mode, 100, battery, names).Height, layout.Height);
    }

    [Fact]
    public void Battery_and_name_columns_widen_the_window()
    {
        var plain = OverlayFrame.Sample(OverlayMode.Complete).Layout;
        var withBattery = OverlayFrame.Sample(OverlayMode.Complete, battery: true).Layout;
        var withNames = OverlayFrame.Sample(OverlayMode.Complete, names: true).Layout;

        Assert.True(withBattery.Width > plain.Width);
        Assert.True(withNames.Width > plain.Width);
        Assert.True(withBattery.BatteryX > withBattery.MemBarX);
        Assert.True(withNames.LeftX > withNames.NameX);
    }

    [Fact]
    public void Scale_percent_scales_the_layout()
    {
        var normal = OverlayFrame.Sample(OverlayMode.Default, scale: OverlayLayout.GetScale(100)).Layout;
        var half = OverlayFrame.Sample(OverlayMode.Default, scale: OverlayLayout.GetScale(50)).Layout;

        Assert.InRange(half.Width, normal.Width / 2 - 3, normal.Width / 2 + 3);
        Assert.InRange(half.Height, normal.Height / 2 - 2, normal.Height / 2 + 2);
    }

    [Fact]
    public void Frame_orders_the_power_history_oldest_first()
    {
        var ring = new float[] { 3, 4, 5, 0, 1, 2 };
        var ordered = OverlayFrame.Ordered(ring, head: 3);
        Assert.Equal(new float[] { 0, 1, 2, 3, 4, 5 }, ordered);
    }

    [Fact]
    public void Chart_points_stack_cpu_and_gpu_over_the_base_line()
    {
        var cpu = new float[] { 10, 20, 10 };
        var gpu = new float[] { 0, 30, 10 };
        var b = new Avalonia.Point[3];
        var c = new Avalonia.Point[3];
        var g = new Avalonia.Point[3];
        OverlayRenderer.BuildChart(cpu, gpu, 100, 10, 40, 50, b, c, g);

        Assert.All(b, p => Assert.Equal(60, p.Y));
        Assert.Equal(100, b[0].X);
        Assert.Equal(140, b[2].X);
        // peak is 50 W: cpu 20 W = 20 px, cpu+gpu = 50 W = the whole height
        Assert.Equal(40, c[1].Y, 3);
        Assert.Equal(10, g[1].Y, 3);
        Assert.Equal(c[0].Y, g[0].Y); // no gpu power: gpu line sits on the cpu line
    }

    [Fact]
    public void Drag_is_clamped_to_the_screen_with_a_margin()
    {
        var screen = new Rectangle(0, 0, 1920, 1080);
        var window = new Size(400, 100);

        Assert.Equal(new Point(5, 5), OverlayGeometry.ClampDrag(screen, new Point(-50, -20), window));
        Assert.Equal(new Point(1920 - 400 - 5, 1080 - 100 - 5), OverlayGeometry.ClampDrag(screen, new Point(5000, 5000), window));
        Assert.Equal(new Point(300, 200), OverlayGeometry.ClampDrag(screen, new Point(300, 200), window));
    }

    [Fact]
    public void Drag_on_a_secondary_screen_stays_on_it()
    {
        var screen = new Rectangle(1920, 0, 1280, 720);
        var p = OverlayGeometry.ClampDrag(screen, new Point(1000, 10), new Size(300, 80));
        Assert.Equal(new Point(1925, 10), p);
    }

    [Fact]
    public void Click_tolerance_separates_click_from_drag()
    {
        Assert.True(OverlayGeometry.IsClick(new Point(100, 100), new Point(105, 95)));
        Assert.False(OverlayGeometry.IsClick(new Point(100, 100), new Point(106, 100)));
    }

    [Fact]
    public void Resize_keeps_the_edge_nearest_to_the_screen_corner()
    {
        var screen = new Rectangle(0, 0, 1920, 1080);
        var oldSize = new Size(400, 100);
        var newSize = new Size(500, 130);

        // top left: stays
        Assert.Equal(new Point(10, 10), OverlayGeometry.KeepNearestEdge(screen, new Point(10, 10), oldSize, newSize));

        // bottom right: right and bottom edge stay, window grows left / up
        var loc = new Point(1920 - 10 - 400, 1080 - 10 - 100);
        var moved = OverlayGeometry.KeepNearestEdge(screen, loc, oldSize, newSize);
        Assert.Equal(1920 - 10, moved.X + newSize.Width);
        Assert.Equal(1080 - 10, moved.Y + newSize.Height);
    }

    [Fact]
    public void Wheel_steps_the_scale_within_limits()
    {
        Assert.Equal(110, OverlayGeometry.ScaleAfterWheel(100, 120));
        Assert.Equal(90, OverlayGeometry.ScaleAfterWheel(100, -120));
        Assert.Equal(OverlaySettings.MaxScalePercent, OverlayGeometry.ScaleAfterWheel(300, 1));
        Assert.Equal(OverlaySettings.MinScalePercent, OverlayGeometry.ScaleAfterWheel(40, -1));
    }

    [Fact]
    public void Saved_position_round_trips_through_the_anchor()
    {
        var screen = new Rectangle(0, 0, 2560, 1440);
        var size = new Size(480, 90);
        var location = new Point(2560 - 480 - 25, 1440 - 90 - 40);

        var (anchor, ox, oy) = OverlayLayout.ToAnchor(screen, location, size);
        Assert.Equal(3, anchor);
        Assert.Equal(new Point(25, 40).X, ox);
        Assert.Equal(location, OverlayLayout.RestorePosition(screen, size, anchor, ox, oy));

        // after a size change the window keeps its distance to the pinned corner
        var grown = new Size(600, 110);
        var restored = OverlayLayout.RestorePosition(screen, grown, anchor, ox, oy);
        Assert.Equal(2560 - 25, restored.X + grown.Width);
    }

    [Fact]
    public void Dips_follow_the_display_scaling()
    {
        var (w, h) = OverlayGeometry.ToDips(new Size(500, 100), 1.25);
        Assert.Equal(400, w);
        Assert.Equal(80, h);
    }

    // ---- window renders ---------------------------------------------------------------------------------

    private static void RenderOverlay(OverlayFrame frame, string name)
    {
        var window = new HardwareOverlayWindow(new OverlayModel(), new MemoryOverlayStore());
        window.Preview(frame);
        MainWindowSnapshotTests.Render(window, name);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(OverlayMode.Light)]
    [InlineData(OverlayMode.Default)]
    [InlineData(OverlayMode.Full)]
    [InlineData(OverlayMode.Complete)]
    public void Overlay_window_renders_each_preset(OverlayMode mode)
    {
        var frame = OverlayFrame.Sample(mode, names: mode == OverlayMode.Complete, battery: mode != OverlayMode.Light);
        var window = new HardwareOverlayWindow(new OverlayModel(), new MemoryOverlayStore());
        window.Preview(frame);
        var bmp = MainWindowSnapshotTests.Render(window, "overlay-" + mode.ToString().ToLowerInvariant());

        Assert.Equal(frame.Layout.Width, bmp.PixelSize.Width);
        Assert.Equal(frame.Layout.Height, bmp.PixelSize.Height);
        Assert.True(CountOpaque(bmp) > 200, "overlay drew nothing");
        window.Close();
    }

    [AvaloniaFact]
    public void Overlay_window_renders_charging_battery_scaled_up()
    {
        var sample = OverlayFrame.Sample(OverlayMode.Full, battery: true, scale: OverlayLayout.GetScale(150));
        var frame = new OverlayFrame
        {
            Scale = sample.Scale, Blocks = sample.Blocks, BatteryVisible = true, Fps = 61, GpuTemp = sample.GpuTemp,
            CpuTemp = sample.CpuTemp, GpuFan = sample.GpuFan, CpuFan = sample.CpuFan, GpuPower = sample.GpuPower,
            CpuPower = sample.CpuPower, GpuUsage = 100, CpuUsage = 3, BatteryLevel = "92%", BatteryRate = "+45.0",
            BatteryPercent = 92, BatteryCharging = true, CpuHistory = sample.CpuHistory, GpuHistory = sample.GpuHistory,
            GpuColor = Color.FromArgb(255, 255, 160, 0), CpuColor = Color.FromArgb(255, 200, 80, 255), BackgroundAlpha = 200,
        };
        RenderOverlay(frame, "overlay-full-charging-150");
    }

    private static int CountOpaque(Avalonia.Media.Imaging.WriteableBitmap bmp)
    {
        using var fb = bmp.Lock();
        int count = 0;
        unsafe
        {
            var p = (int*)fb.Address;
            int stride = fb.RowBytes / 4;
            for (int y = 0; y < fb.Size.Height; y++)
                for (int x = 0; x < fb.Size.Width; x++)
                    if (((uint)p[y * stride + x] >> 24) > 200) count++;
        }
        return count;
    }

    // ---- settings window --------------------------------------------------------------------------------

    private sealed class Recorder
    {
        public int Refreshes, Toggles, GameOnlyToggles;
    }

    private static (OverlayConfigWindow window, MemoryOverlayStore store, Recorder rec) CreateConfig(Action<MemoryOverlayStore>? setup = null)
    {
        var store = new MemoryOverlayStore();
        setup?.Invoke(store);
        var rec = new Recorder();
        var window = new OverlayConfigWindow(store, () => rec.Refreshes++, () => rec.Toggles++, () => rec.GameOnlyToggles++);
        return (window, store, rec);
    }

    [AvaloniaTheory]
    [InlineData("dark")]
    [InlineData("light")]
    public void Config_window_renders(string theme)
    {
        Avalonia.Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var (window, _, _) = CreateConfig(s =>
        {
            s.Set("overlay", 1);
            s.Set("overlay_mode", 2);
            s.Set("overlay_scale_percent", 125);
            s.Set("overlay_alpha", 180);
            s.Set("overlay_color_cpu", "FF8000");
        });

        var bmp = MainWindowSnapshotTests.Render(window, "overlay-config-" + theme);
        Assert.InRange(bmp.PixelSize.Width, 440, 470);
        Assert.InRange(bmp.PixelSize.Height, 250, 600);
        window.Close();
    }

    [AvaloniaFact]
    public void Config_window_reflects_the_stored_options()
    {
        var (window, store, _) = CreateConfig(s =>
        {
            s.Set("overlay_mode", 3);
            s.Set(OverlaySettings.ModeKey("overlay_show_fps", 3), 0);
            s.Set(OverlaySettings.ModeKey("overlay_show_battery", 3), 1);
        });
        window.Show();

        var checks = All<CheckBox>(window).ToList();
        Assert.Contains(checks, c => c.Content as string == "FPS" && c.IsChecked == false);
        Assert.Contains(checks, c => c.Content as string == "RAM" && c.IsChecked == true); // complete preset default
        Assert.Contains(checks, c => c.IsThreeState && c.IsChecked == true);
        Assert.Equal(3, store.GetInt("overlay_mode", -1));
        window.Close();
    }

    [AvaloniaFact]
    public void Config_window_writes_changes_and_refreshes_the_overlay()
    {
        var (window, store, rec) = CreateConfig();
        window.Show();

        var fps = All<CheckBox>(window).First(c => c.Content as string == "FPS");
        Assert.True(fps.IsChecked); // default preset shows fps
        fps.IsChecked = false;
        Assert.Equal(0, store.GetInt(OverlaySettings.ModeKey("overlay_show_fps", 0), -1));
        Assert.Equal(1, rec.Refreshes);

        var full = All<Button>(window).First(b => b.Content as string == GHelper.Properties.Strings.OverlayModeFull);
        full.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, store.GetInt("overlay_mode", -1));
        Assert.Equal(2, rec.Refreshes);

        // switching preset shows that preset's own values: usage is on in Full
        var load = All<CheckBox>(window).First(c => c.Content as string == GHelper.Properties.Strings.OverlayLoad);
        Assert.True(load.IsChecked);
        window.Close();
    }

    [AvaloniaFact]
    public void Config_window_enable_and_game_only_delegate_to_the_toggles()
    {
        var (window, store, rec) = CreateConfig();
        window.Show();

        var enable = All<CheckBox>(window).First(c => c.Content as string == GHelper.Properties.Strings.Overlay);
        enable.IsChecked = true;
        Assert.Equal(1, rec.Toggles);
        Assert.Equal(0, store.GetInt("overlay", 0)); // the toggle callback owns the config write

        var gameOnly = All<CheckBox>(window).First(c => c.Content as string == GHelper.Properties.Strings.OverlayOnlyInGames);
        gameOnly.IsChecked = true;
        Assert.Equal(1, rec.GameOnlyToggles);
        window.Close();
    }

    [AvaloniaFact]
    public void Config_window_reset_removes_overrides()
    {
        var (window, store, rec) = CreateConfig(s =>
        {
            s.Set("overlay_scale_percent", 200);
            s.Set("overlay_alpha", 10);
            s.Set("overlay_color_gpu", "112233");
            s.Set(OverlaySettings.ModeKey("overlay_show_chart", 1), 0);
        });
        window.Show();

        var reset = All<Button>(window).First(b => b.Content as string == GHelper.Properties.Strings.Reset);
        reset.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(-1, store.GetInt("overlay_scale_percent", -1));
        Assert.Equal(-1, store.GetInt("overlay_alpha", -1));
        Assert.Null(store.GetString("overlay_color_gpu"));
        Assert.Equal(-1, store.GetInt(OverlaySettings.ModeKey("overlay_show_chart", 1), -1));
        Assert.True(rec.Refreshes >= 1);
        window.Close();
    }

    private static IEnumerable<T> All<T>(Control root) where T : Control
    {
        return root.GetVisualDescendantsAndSelfCompat().OfType<T>();
    }
}

internal static class VisualExtensionsCompat
{
    public static IEnumerable<Avalonia.Visual> GetVisualDescendantsAndSelfCompat(this Avalonia.Visual root)
    {
        yield return root;
        foreach (var d in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root)) yield return d;
    }
}
