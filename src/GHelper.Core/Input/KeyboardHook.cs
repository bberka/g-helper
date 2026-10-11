using GHelper.Input;
using GHelper.Platform;

/// <summary>
/// Legacy global hotkey / key injection facade (the Win32 RegisterHotKey / keybd_event half now lives behind
/// <see cref="IInputService"/>). The static members inject keys, an instance owns a set of registered hotkeys.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private static IInputService Input => PlatformServices.Current.Input;

    public static void KeyPress(Keys key) => Input.KeyPress(key);

    public static void KeyKeyPress(Keys key, Keys key2) => Input.KeyKeyPress(key, key2);

    public static void KeyKeyKeyPress(Keys key, Keys key2, Keys key3, int sleep = 1, int interSleep = 0)
        => Input.KeyKeyKeyPress(key, key2, key3, sleep, interSleep);

    public static void KeyKeyKeyKeyPress(Keys key, Keys key2, Keys key3, Keys key4, int sleep = 1)
        => Input.KeyKeyKeyKeyPress(key, key2, key3, key4, sleep);

    private readonly List<IDisposable> _registrations = new();
    private readonly object _lock = new();

    /// <summary>
    /// Registers a hot key with the platform. The modifier value is passed through unchanged, so
    /// <see cref="ModifierKeys.NoRepeat"/> (0x4000, same as Win32 MOD_NOREPEAT) reaches the platform implementation.
    /// </summary>
    public void RegisterHotKey(ModifierKeys modifier, Keys key)
    {
        try
        {
            var registration = Input.RegisterHotkey((HotkeyModifiers)(int)modifier, key, () =>
            {
                KeyPressed?.Invoke(this, new KeyPressedEventArgs(modifier, key));
            });

            lock (_lock) _registrations.Add(registration);
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Couldn't register " + key + ": " + ex.Message);
        }
    }

    /// <summary>A hot key has been pressed.</summary>
    public event EventHandler<KeyPressedEventArgs>? KeyPressed;

    public void UnregisterAll()
    {
        IDisposable[] registrations;

        lock (_lock)
        {
            registrations = _registrations.ToArray();
            _registrations.Clear();
        }

        foreach (var registration in registrations)
        {
            try { registration.Dispose(); } catch { }
        }
    }

    public void Dispose()
    {
        UnregisterAll();
    }
}

/// <summary>
/// Event Args for the event that is fired after the hot key has been pressed.
/// </summary>
public class KeyPressedEventArgs : EventArgs
{
    private ModifierKeys _modifier;
    private Keys _key;

    public KeyPressedEventArgs(ModifierKeys modifier, Keys key)
    {
        _modifier = modifier;
        _key = key;
    }

    public ModifierKeys Modifier
    {
        get { return _modifier; }
    }

    public Keys Key
    {
        get { return _key; }
    }
}

/// <summary>
/// The enumeration of possible modifiers (same bits as the Win32 MOD_* flags and <see cref="HotkeyModifiers"/>).
/// </summary>
[Flags]
public enum ModifierKeys : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
    NoRepeat = 0x4000
}
