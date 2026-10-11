using GHelper.Overlay;

namespace GHelper.App.Views.Overlay;

/// <summary>
/// The overlay_* config values the overlay window and the overlay settings window read and write. A seam so the
/// windows can be exercised in headless tests without the platform services AppConfig needs.
/// </summary>
public interface IOverlayConfigStore
{
    int GetInt(string key, int defaultValue);
    string? GetString(string key);
    void Set(string key, int value);
    void Set(string key, string value);
    void Remove(string key);
}

/// <summary>Production store: the legacy config.json through <c>AppConfig</c>.</summary>
public sealed class AppConfigOverlayStore : IOverlayConfigStore
{
    public int GetInt(string key, int defaultValue) => AppConfig.Get(key, defaultValue);
    public string? GetString(string key) => AppConfig.GetString(key);
    public void Set(string key, int value) => AppConfig.Set(key, value);
    public void Set(string key, string value) => AppConfig.Set(key, value);
    public void Remove(string key) => AppConfig.Remove(key);
}

/// <summary>In-memory store (tests, design time).</summary>
public sealed class MemoryOverlayStore : IOverlayConfigStore
{
    private readonly Dictionary<string, object> _values = new();

    public int GetInt(string key, int defaultValue) =>
        _values.TryGetValue(key, out var v) && v is int i ? i : defaultValue;

    public string? GetString(string key) => _values.TryGetValue(key, out var v) ? v as string : null;
    public void Set(string key, int value) => _values[key] = value;
    public void Set(string key, string value) => _values[key] = value;
    public void Remove(string key) => _values.Remove(key);
}

/// <summary>Block visibility / battery state per preset, computed against an <see cref="IOverlayConfigStore"/>.</summary>
public static class OverlayOptions
{
    public static bool BlockShown(IOverlayConfigStore store, string key, int mode)
    {
        bool def = OverlaySettings.BlockDefault(key, (OverlayMode)mode);
        return store.GetInt(OverlaySettings.ModeKey(key, mode), def ? 1 : 0) != 0;
    }

    /// <summary>-1 = only on battery, 0 = off, 1 = always.</summary>
    public static int BatteryState(IOverlayConfigStore store, int mode) =>
        store.GetInt(OverlaySettings.ModeKey("overlay_show_battery", mode), (OverlayMode)mode == OverlayMode.Light ? 0 : -1);
}
