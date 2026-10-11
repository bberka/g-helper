using Avalonia.Controls;

namespace GHelper.App.Services;

/// <summary>
/// Name -> side window factory. Each window file registers itself from a module initializer, e.g.
/// <code>[ModuleInitializer] internal static void Register() =&gt; WindowRegistry.Register("fans", arg =&gt; new FansWindow(arg));</code>
/// <see cref="AvaloniaUiHost.ShowWindow"/> (IUiHost.ShowWindow) shows the single instance of the named window.
/// Well known names: fans, extra, matrix, slash, updates, handheld, overlay, mouse, keyboard, headset.
/// </summary>
public static class WindowRegistry
{
    private static readonly Dictionary<string, Func<int, Window>> Factories = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Window> Instances = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string name, Func<int, Window> factory)
    {
        lock (Factories) Factories[name] = factory;
    }

    public static bool IsRegistered(string name)
    {
        lock (Factories) return Factories.ContainsKey(name);
    }

    /// <summary>Must run on the UI thread. Re-uses an open instance, otherwise creates one.</summary>
    public static Window? Show(string name, int arg = 0)
    {
        Func<int, Window>? factory;
        lock (Factories) Factories.TryGetValue(name, out factory);
        if (factory is null) return null;

        if (Instances.TryGetValue(name, out var existing) && existing.IsVisible)
        {
            existing.Activate();
            return existing;
        }

        var window = factory(arg);
        Instances[name] = window;
        window.Closed += (_, _) => { if (Instances.TryGetValue(name, out var w) && ReferenceEquals(w, window)) Instances.Remove(name); };
        window.Show();
        return window;
    }

    /// <summary>The open instance of a window or null.</summary>
    public static Window? Get(string name) =>
        Instances.TryGetValue(name, out var w) && w.IsVisible ? w : null;
}
