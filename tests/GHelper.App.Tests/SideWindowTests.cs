using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using GHelper.AnimeMatrix;
using GHelper.App.Views;
using SkiaSharp;
using Xunit;

namespace GHelper.App.Tests;

/// <summary>
/// Headless runs of the AniMe Matrix editor, Slash settings and ROG Ally controller windows against fake USB
/// transports: they must lay out, render (PNGs land in the snapshot artifact folder) and write the legacy config keys
/// / device reports when the user operates them.
/// </summary>
public class SideWindowTests
{
    private static string WritePicture()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(240, 120, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawCircle(120, 60, 50, paint);
            paint.Color = SKColors.Gray;
            canvas.DrawRect(10, 10, 60, 40, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        string path = Path.Combine(Path.GetTempPath(), "ghelper-picture-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private static int LitLeds(AnimeMatrixDevice device)
    {
        var led = device.LedSnapshot();
        int lit = 0;
        foreach (var v in led) if (v > 0) lit++;
        return lit;
    }

    private static MatrixWindow OpenMatrix(AniMatrixControl control, ThemeVariant theme, string snapshot)
    {
        Application.Current!.RequestedThemeVariant = theme;
        MatrixWindow.FontDownloader = () => Task.FromResult(false);

        var window = new MatrixWindow(control);
        MainWindowSnapshotTests.Render(window, snapshot);
        return window;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T Find<T>(Window window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException(name + " not found");

    // ------------------------------------------------------------------ matrix

    [AvaloniaFact]
    public void Matrix_picture_mode_renders_preview_and_settings()
    {
        var (control, _, device) = SideWindowEnv.MatrixControl();
        string picture = WritePicture();
        AppConfig.Set("matrix_picture", picture);
        AppConfig.Set("matrix_running", (int)MatrixMode.Picture);
        AppConfig.Set("matrix_zoom", 100);
        AppConfig.Set("matrix_x", 0);
        AppConfig.Set("matrix_y", 0);
        control.SetMatrixPicture(picture);

        Assert.True(LitLeds(device) > 20, "picture should light LEDs");

        var window = OpenMatrix(control, ThemeVariant.Dark, "matrix-picture-dark");
        try
        {
            Assert.Equal(450, window.Width);
            Assert.True(window.Bounds.Height > 400, "window height " + window.Bounds.Height);
            Assert.True(Find<StackPanel>(window, "PanelPictureSettings").IsVisible);
            Assert.False(Find<StackPanel>(window, "PanelTextSettings").IsVisible);
            Assert.True(Find<Controls.RButton>(window, "ButtonPictureMode").Activated);
            Assert.NotNull(window.LoadedPicture);

            var preview = Find<MatrixPreview>(window, "Preview");
            Assert.Same(device, preview.Device);
            Assert.True(preview.Bounds.Width > 300);
            // 3:1 LED cells like the legacy preview bitmap
            Assert.InRange(preview.CellWidth / preview.CellHeight, 2.9, 3.1);
        }
        finally
        {
            window.Close();
            control.Dispose();
            File.Delete(picture);
        }
    }

    [AvaloniaFact]
    public void Matrix_text_mode_renders_text_on_the_preview_light_theme()
    {
        var (control, _, device) = SideWindowEnv.MatrixControl();
        AppConfig.Set("matrix_text", "G-Helper");
        AppConfig.Set("matrix_text2", "");
        AppConfig.Set("matrix_text_running", 0);
        AppConfig.Set("matrix_running", (int)MatrixMode.Text);
        AppConfig.Set("matrix_text_x", 0);
        AppConfig.Set("matrix_text_y", 0);
        AppConfig.Set("matrix_text2_x", 0);
        AppConfig.Set("matrix_text2_y", 0);
        AppConfig.Set("matrix_flip", 0);

        device.PresentText();
        Assert.True(LitLeds(device) > 20, "text should light LEDs");

        var window = OpenMatrix(control, ThemeVariant.Light, "matrix-text-light");
        try
        {
            Assert.True(Find<StackPanel>(window, "PanelTextSettings").IsVisible);
            Assert.False(Find<StackPanel>(window, "PanelPictureSettings").IsVisible);
            Assert.False(Find<Controls.RButton>(window, "ButtonPicture").IsVisible);
            Assert.True(Find<Controls.RButton>(window, "ButtonTextMode").Activated);
            Assert.Equal("G-Helper", Find<TextBox>(window, "TextMatrix").Text);
        }
        finally
        {
            window.Close();
            control.Dispose();
        }
    }

    [AvaloniaFact]
    public void Matrix_clock_and_audio_modes_show_their_panels()
    {
        var (control, _, device) = SideWindowEnv.MatrixControl();
        AppConfig.Set("matrix_running", (int)MatrixMode.Clock);
        AppConfig.Set("matrix_clock_battery", 0);
        AppConfig.Set("matrix_flip", 0);
        AppConfig.Set("matrix_time", "HH:mm");
        AppConfig.Set("matrix_date", "yy.MM.dd");
        device.PresentClock();

        var window = OpenMatrix(control, ThemeVariant.Dark, "matrix-clock-dark");
        try
        {
            Assert.True(Find<StackPanel>(window, "PanelClockSettings").IsVisible);
            Assert.True(Find<Controls.RButton>(window, "ButtonClockMode").Activated);
            Assert.Equal("HH:mm", Find<TextBox>(window, "TextClockTime").Text);

            // switching to audio through the tab: config, device and panels follow
            Click(Find<Controls.RButton>(window, "ButtonAudioMode"));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal((int)MatrixMode.Audio, AppConfig.Get("matrix_running", -1));
            Assert.True(Find<StackPanel>(window, "PanelAudioSettings").IsVisible);
            Assert.False(Find<StackPanel>(window, "PanelClockSettings").IsVisible);
            Assert.False(Find<StackPanel>(window, "PanelButtons").IsVisible);
            MainWindowSnapshotTests.Render(window, "matrix-audio-dark");
        }
        finally
        {
            window.Close();
            control.Dispose();
        }
    }

    [AvaloniaFact]
    public void Matrix_power_options_store_config()
    {
        var (control, _, _) = SideWindowEnv.MatrixControl();
        AppConfig.Set("matrix_running", (int)MatrixMode.Clock);

        var window = OpenMatrix(control, ThemeVariant.Dark, "matrix-power-dark");
        try
        {
            Find<CheckBox>(window, "CheckAutoOff").IsChecked = true;
            Find<CheckBox>(window, "CheckLidOff").IsChecked = true;

            Assert.True(AppConfig.Is("matrix_auto"));
            Assert.True(AppConfig.Is("matrix_lid"));

            Find<CheckBox>(window, "CheckAutoOff").IsChecked = false;
            Assert.False(AppConfig.Is("matrix_auto"));
        }
        finally
        {
            window.Close();
            control.Dispose();
        }
    }

    [AvaloniaFact]
    public void Matrix_dragging_the_preview_pans_the_picture()
    {
        var (control, _, device) = SideWindowEnv.MatrixControl();
        string picture = WritePicture();
        AppConfig.Set("matrix_picture", picture);
        AppConfig.Set("matrix_running", (int)MatrixMode.Picture);
        AppConfig.Set("matrix_rotation", 0);
        AppConfig.Set("matrix_x", 0);
        AppConfig.Set("matrix_y", 0);
        control.SetMatrixPicture(picture);

        var window = OpenMatrix(control, ThemeVariant.Dark, "matrix-drag-dark");
        try
        {
            var preview = Find<MatrixPreview>(window, "Preview");
            var origin = preview.TranslatePoint(new Point(preview.Bounds.Width / 2, preview.Bounds.Height / 2), window)!.Value;

            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(origin + new Point(-24, 0));
            window.MouseMove(origin + new Point(-48, 0));
            window.MouseUp(origin + new Point(-48, 0), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            // moving left (cols < 0) pans the picture: x = round(-cols * 3) > 0
            Assert.True(AppConfig.Get("matrix_x", 0) > 0, "matrix_x = " + AppConfig.Get("matrix_x", 0));
            Assert.True(LitLeds(device) > 0);
        }
        finally
        {
            window.Close();
            control.Dispose();
            File.Delete(picture);
        }
    }

    // ------------------------------------------------------------------ slash

    [AvaloniaFact]
    public void Slash_window_renders_and_writes_power_flags()
    {
        var (control, usb, _) = SideWindowEnv.SlashControl();
        AppConfig.Set("matrix_auto", 0);
        AppConfig.Set("matrix_lid", 0);
        AppConfig.Set("matrix_interval", 2);
        AppConfig.Set("slash_dim", 30);

        // record 0xA0..0xA8: byte 8 = flag, bytes 6..11 not all zero (what the real device returns)
        usb.OnGet = request =>
        {
            var reply = new byte[128];
            Array.Copy(request, reply, Math.Min(request.Length, reply.Length));
            reply[6] = 0x01;
            reply[8] = request.Length > 5 && request[5] == 0xA0 ? (byte)1 : (byte)0;
            return reply;
        };

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new SlashWindow(control);
        MainWindowSnapshotTests.Render(window, "slash-dark");
        try
        {
            Assert.Equal(400, window.Width);
            Assert.True(window.Bounds.Height > 250, "window height " + window.Bounds.Height);
            Assert.True(Find<CheckBox>(window, "CheckBoot").IsChecked);
            Assert.False(Find<CheckBox>(window, "CheckLowBattery").IsChecked);
            Assert.Equal(2.0, Find<Controls.RSlider>(window, "SliderInterval").Value);
            Assert.Equal(2, Find<ComboBox>(window, "ComboDim").SelectedIndex); // 30 %

            int before = usb.SetCount;
            Find<CheckBox>(window, "CheckAutoOff").IsChecked = true;

            Assert.True(AppConfig.Is("matrix_auto"));
            Assert.True(usb.SetCount > before, "lighting-on-battery report was not sent");

            before = usb.SetCount;
            Find<ComboBox>(window, "ComboDim").SelectedIndex = 4;
            Assert.Equal(50, AppConfig.Get("slash_dim", 0));
            Assert.True(usb.SetCount > before, "power saving level was not sent");
        }
        finally
        {
            window.Close();
            control.Dispose();
        }
    }

    [AvaloniaFact]
    public void Slash_interval_slider_commits_with_the_keyboard()
    {
        var (control, _, _) = SideWindowEnv.SlashControl();
        AppConfig.Set("matrix_interval", 0);
        AppConfig.Set("matrix_running", 0);

        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var window = new SlashWindow(control);
        MainWindowSnapshotTests.Render(window, "slash-light");
        try
        {
            var slider = Find<Controls.RSlider>(window, "SliderInterval");
            string off = Find<TextBlock>(window, "LabelInterval").Text!;

            slider.Focus();
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1.0, slider.Value);
            Assert.Equal(1, AppConfig.Get("matrix_interval", 0));
            Assert.NotEqual(off, Find<TextBlock>(window, "LabelInterval").Text);
        }
        finally
        {
            window.Close();
            control.Dispose();
        }
    }

    // ------------------------------------------------------------------ handheld

    private sealed class RecordingActions : IControllerActions
    {
        public int Deadzones, Apply, Vibration;
        public List<bool> Disabled { get; } = new();

        public void SetDeadzones() => Deadzones++;
        public void ApplyMode() => Apply++;
        public void DisableController(bool disabled) => Disabled.Add(disabled);
        public void SetVibrationMode() => Vibration++;
    }

    private static HandheldWindow OpenHandheld(RecordingActions actions, ThemeVariant theme, string snapshot)
    {
        SideWindowEnv.Ensure();
        Application.Current!.RequestedThemeVariant = theme;
        var window = new HandheldWindow(actions);
        MainWindowSnapshotTests.Render(window, snapshot);
        return window;
    }

    [AvaloniaFact]
    public void Handheld_window_renders_with_saved_bindings()
    {
        SideWindowEnv.Ensure();
        AppConfig.Set("bind_a", "01-01");
        AppConfig.Set("ls_min", 10);
        AppConfig.Set("ls_max", 90);
        AppConfig.Set("rt_max", 80);
        AppConfig.Set("vibra", 70);

        var actions = new RecordingActions();
        var window = OpenHandheld(actions, ThemeVariant.Dark, "handheld-dark");
        try
        {
            Assert.Equal("10 - 90%", Find<TextBlock>(window, "LabelLS").Text);
            Assert.Equal("0 - 80%", Find<TextBlock>(window, "LabelRT").Text);
            Assert.Equal("70%", Find<TextBlock>(window, "LabelVibra").Text);

            Assert.True(Find<Controls.RButton>(window, "ButtonA").Activated);
            Assert.False(Find<Controls.RButton>(window, "ButtonB").Activated);
            Assert.False(Find<Border>(window, "PanelBinding").IsVisible);

            // nothing is sent to the controller just by opening the window
            Assert.Equal(0, actions.Deadzones + actions.Apply + actions.Vibration);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Handheld_binding_editor_loads_and_saves_a_button()
    {
        SideWindowEnv.Ensure();
        AppConfig.Remove("bind_x");
        AppConfig.Remove("bind2_x");
        AppConfig.Remove("turbo_x");

        var actions = new RecordingActions();
        var window = OpenHandheld(actions, ThemeVariant.Dark, "handheld-binding-dark");
        try
        {
            Click(Find<Controls.RButton>(window, "ButtonX"));
            Dispatcher.UIThread.RunJobs();

            Assert.True(Find<Border>(window, "PanelBinding").IsVisible);
            Assert.Equal("x", window.ActiveBinding);
            Assert.EndsWith(": X", Find<TextBlock>(window, "LabelBinding").Text);

            // pick the first real binding of the list (not the "----------" placeholder / group headers)
            var combo = Find<ComboBox>(window, "ComboPrimary");
            var items = combo.ItemsSource!.Cast<object>().ToList();
            int target = items.FindIndex(i => i.ToString() == "A");
            Assert.True(target > 0, "binding list lacks the A entry");

            combo.SelectedIndex = target;
            string saved = AppConfig.GetString("bind_x", "");
            Assert.NotEqual("", saved);
            Assert.True(Find<Controls.RButton>(window, "ButtonX").Activated);
            Assert.Equal(1, actions.Apply);

            Find<ComboBox>(window, "ComboTurboPrimary").SelectedIndex = 3;
            Assert.Equal(150, AppConfig.Get("turbo_x", 0));
            Assert.Equal(2, actions.Apply);

            MainWindowSnapshotTests.Render(window, "handheld-binding-dark");

            // choosing "Off" again removes the key
            Find<ComboBox>(window, "ComboTurboPrimary").SelectedIndex = 0;
            Assert.Equal(0, AppConfig.Get("turbo_x", 0));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Handheld_sliders_commit_and_reset_restores_defaults()
    {
        SideWindowEnv.Ensure();
        AppConfig.Set("ls_min", 0);
        AppConfig.Set("ls_max", 100);
        AppConfig.Set("vibra", 100);

        var actions = new RecordingActions();
        var window = OpenHandheld(actions, ThemeVariant.Light, "handheld-light");
        try
        {
            var slider = Find<Controls.RSlider>(window, "TrackLSMax");
            slider.Focus();
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(99.0, slider.Value);
            Assert.Equal(99, AppConfig.Get("ls_max", 0));
            Assert.Equal("0 - 99%", Find<TextBlock>(window, "LabelLS").Text);
            Assert.Equal(1, actions.Deadzones);

            Click(Find<Controls.RButton>(window, "ButtonReset"));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(100.0, slider.Value);
            Assert.Equal("0 - 100%", Find<TextBlock>(window, "LabelLS").Text);
            Assert.Equal(100, AppConfig.Get("ls_max", 100));
            Assert.Equal(2, actions.Deadzones);

            var disable = Find<CheckBox>(window, "CheckController");
            disable.IsChecked = true;
            Assert.True(AppConfig.Is("controller_disabled"));
            Assert.Equal(new[] { true }, actions.Disabled);
        }
        finally
        {
            window.Close();
        }
    }

    // ------------------------------------------------------------------ registry

    [AvaloniaFact]
    public void Windows_register_themselves_with_the_registry()
    {
        // module initializers run when the assembly loads; touching a window type guarantees it
        _ = typeof(MatrixWindow);

        Assert.True(Services.WindowRegistry.IsRegistered("matrix"));
        Assert.True(Services.WindowRegistry.IsRegistered("slash"));
        Assert.True(Services.WindowRegistry.IsRegistered("handheld"));
    }
}
