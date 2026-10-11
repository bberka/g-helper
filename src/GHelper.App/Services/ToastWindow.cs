using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using GHelper.App.Controls;

namespace GHelper.App.Services;

/// <summary>
/// Small transient on-screen message (legacy ToastForm / OSDNativeForm layered window): dark translucent rounded
/// box with an optional icon and big bold text, centred horizontally 300 px above the bottom of the screen, always on
/// top, transparent, never takes focus and lets mouse clicks fall through.
/// </summary>
public sealed class ToastWindow : Window
{
    private static readonly Dictionary<string, string> IconFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["brightnessup"] = "brightness-up.png",
        ["brightnessdown"] = "brightness-down.png",
        ["backlightup"] = "backlight-up.png",
        ["backlightdown"] = "backlight-down.png",
        ["touchpad"] = "icons8-touchpad-96.png",
        ["microphone"] = "icons8-microphone-96.png",
        ["microphonemute"] = "icons8-mute-unmute-96.png",
        ["fnlock"] = "icons8-function-mac-96.png",
        ["battery"] = "icons8-charged-battery-96.png",
        ["charger"] = "icons8-charging-battery-96.png",
        ["controller"] = "icons8-controller-96.png",
    };

    private const double IconSize = 64;
    private const double Height100 = 100;

    private readonly Border _box;
    private readonly TextBlock _text;
    private readonly TintedImage _icon;
    private readonly DispatcherTimer _timer;
    private bool _styled;

    public ToastWindow()
    {
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        Focusable = false;
        SizeToContent = SizeToContent.Manual;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Height = Height100;
        Title = "G-Helper OSD";

        _icon = new TintedImage
        {
            Width = IconSize,
            Height = IconSize,
            Foreground = Brushes.White,
            Margin = new Thickness(18, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
        };

        _text = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 36,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(_icon);
        Grid.SetColumn(_text, 1);
        grid.Children.Add(_text);

        _box = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            CornerRadius = new CornerRadius(10),
            Child = grid,
            IsHitTestVisible = false,
        };
        Content = _box;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Hide();
        };
    }

    public TimeSpan Duration
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public string Text => _text.Text ?? "";

    /// <summary>Resolve an icon key ("eco", "brightnessUp", "icons8-fan-48") to an asset.</summary>
    public static Bitmap? LoadIcon(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        string normalized = key.Replace("-", "").Replace("_", "").Replace(" ", "");
        string file = IconFiles.TryGetValue(normalized, out var mapped) ? mapped : key;
        if (!file.Contains('.')) file += ".png";

        foreach (var folder in new[] { "Icons", "Tray" })
        {
            var uri = new Uri($"avares://GHelper/Assets/{folder}/{file}");
            try
            {
                if (AssetLoader.Exists(uri))
                {
                    using var stream = AssetLoader.Open(uri);
                    return new Bitmap(stream);
                }
            }
            catch
            {
                // fall through
            }
        }
        return null;
    }

    /// <summary>Text + icon, restarts the 2 s display timer (legacy RunToast).</summary>
    public void ShowToast(string text, string? iconKey = null)
    {
        _timer.Stop();

        _text.Text = text;
        var bmp = LoadIcon(iconKey);
        _icon.Source = bmp;
        _icon.IsVisible = bmp is not null;
        _text.Margin = bmp is not null ? new Thickness(8, 0, 18, 0) : new Thickness(18, 0);

        Width = Math.Max(300, 100 + text.Length * 22);
        Height = Height100;

        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is not null)
        {
            double scale = screen.Scaling;
            var area = screen.Bounds;
            int x = area.X + (int)((area.Width - Width * scale) / 2);
            int y = area.Bottom - (int)((300 + Height) * scale);
            Position = new PixelPoint(x, y);
        }

        if (!IsVisible) Show();
        ApplyNativeStyle();
        _timer.Start();
    }

    /// <summary>No focus, no taskbar button, mouse transparent (Win32: WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW).</summary>
    private void ApplyNativeStyle()
    {
        if (_styled) return;
        _styled = true;
        try
        {
            if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle)
                Win32.MakeClickThroughNoActivate(handle.Handle);
        }
        catch (Exception ex)
        {
            AppLog.Write("Toast style: " + ex.Message);
        }
    }

    private static class Win32
    {
        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TRANSPARENT = 0x20;
        private const long WS_EX_TOOLWINDOW = 0x80;
        private const long WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

        public static void MakeClickThroughNoActivate(nint hwnd)
        {
            long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            style |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)style);
        }
    }
}
