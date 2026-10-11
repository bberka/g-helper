using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GHelper.App.Controls;
using GHelper.App.Services;
using GHelper.App.Views;
using DrawingColor = System.Drawing.Color;

namespace GHelper.App.Backend;

/// <summary><see cref="IMainShell"/> on top of the real windows. UI thread only.</summary>
public sealed class AvaloniaMainShell : IMainShell
{
    private readonly MainWindow _main;
    private readonly Dictionary<string, Bitmap?> _icons = new(StringComparer.OrdinalIgnoreCase);

    public AvaloniaMainShell(MainWindow main) => _main = main;

    public void SetMinRate(int minRate) => _main.MinRate = minRate;

    public void ShowColorPicker(DrawingColor initial, bool allowRandom, Action<DrawingColor> changed)
    {
        var picker = new ColorPickerWindow(
            Avalonia.Media.Color.FromArgb(initial.A, initial.R, initial.G, initial.B), allowRandom, new ConfigColorStore());

        picker.ColorChanged += c => changed(DrawingColor.FromArgb(c.A, c.R, c.G, c.B));

        // the picker is a child of the main window; it stays interactive while colours apply live
        if (_main.IsVisible) _ = picker.ShowDialog(_main);
        else picker.Show();
    }

    public bool IsSideWindowOpen(string name) => WindowRegistry.Get(name) is not null;

    public void CloseSideWindow(string name) => WindowRegistry.Get(name)?.Close();

    public Bitmap? LoadIcon(string resourceKey)
    {
        if (_icons.TryGetValue(resourceKey, out var cached)) return cached;

        Bitmap? bitmap = null;
        try
        {
            // legacy resource keys use underscores: icons8_maus_48 -> Assets/Icons/icons8-maus-48.png
            string file = resourceKey.Replace('_', '-') + ".png";
            using var stream = AssetLoader.Open(new Uri("avares://GHelper/Assets/Icons/" + file));
            bitmap = new Bitmap(stream);
        }
        catch (Exception ex)
        {
            AppLog.Write("Icon " + resourceKey + ": " + ex.Message);
        }

        _icons[resourceKey] = bitmap;
        return bitmap;
    }

    /// <summary>The 14 editable custom colours of the picker live in the legacy "aura_color_custom" key.</summary>
    private sealed class ConfigColorStore : IColorPickerStore
    {
        private const string Key = "aura_color_custom";

        public string? LoadCustomColors() => AppConfig.GetString(Key, "");

        public void SaveCustomColors(string value) => AppConfig.Set(Key, value);
    }
}
