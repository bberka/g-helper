using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GHelper.Peripherals;

namespace GHelper.App.Views.Mouse;

/// <summary>
/// Device icon of the "Peripheral 1/2/3" buttons of the main window. Core only knows the legacy resource key
/// (<see cref="IPeripheral.IconKey"/>, e.g. "icons8_maus_48", "dock_48"); this maps it to the Avalonia asset. The icons
/// are black glyphs meant to be tinted with the foreground colour (TintedImage / RButton.Icon).
/// </summary>
public static class PeripheralIcons
{
    private const string Folder = "avares://GHelper/Assets/Icons/";

    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["icons8_maus_48"] = Folder + "icons8-maus-48.png",
        ["icons8_keyboard_48"] = Folder + "icons8-keyboard-48.png",
        ["icons8_headphones_48"] = Folder + "icons8-headphones-48.png",
        ["dock_48"] = Folder + "dock-48.png",
    };

    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Asset uri for a legacy icon key; null/unknown keys fall back to the generic icon of the device type.</summary>
    public static string UriFor(string? iconKey, PeripheralType type = PeripheralType.Mouse)
    {
        if (iconKey is not null)
        {
            if (Known.TryGetValue(iconKey, out var uri)) return uri;

            // other legacy resource names follow the same "icons8_x_48" -> "icons8-x-48.png" convention
            string guess = Folder + iconKey.Replace('_', '-') + ".png";
            try
            {
                if (AssetLoader.Exists(new Uri(guess))) return guess;
            }
            catch
            {
                // no Avalonia runtime (plain unit tests): fall through to the type icon
            }
        }

        return Known[DefaultKey(type)];
    }

    public static string UriFor(IPeripheral peripheral) => UriFor(peripheral.IconKey(), peripheral.DeviceType());

    /// <summary>Loaded (and cached) bitmap of an asset uri; null when the asset cannot be opened.</summary>
    public static Bitmap? Load(string uri)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(uri, out var bitmap)) return bitmap;
            try
            {
                using var stream = AssetLoader.Open(new Uri(uri));
                bitmap = new Bitmap(stream);
                Cache[uri] = bitmap;
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }

    public static Bitmap? Load(IPeripheral peripheral) => Load(UriFor(peripheral));

    private static string DefaultKey(PeripheralType type) => type switch
    {
        PeripheralType.Keyboard => "icons8_keyboard_48",
        PeripheralType.Headset => "icons8_headphones_48",
        _ => "icons8_maus_48",
    };
}
