using GHelper.Input;

namespace GHelper.Platform;

[Flags]
public enum HotkeyModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 }

/// <summary>Keyboard injection + global hotkeys + low level key tap (legacy KeyboardHook / KeyboardListener / OnScreenKeyboard).</summary>
public interface IInputService : IDisposable
{
    /// <summary>Simulate key press + release.</summary>
    void KeyPress(Keys key);
    void KeyKeyPress(Keys key, Keys key2);
    void KeyKeyKeyPress(Keys key, Keys key2, Keys key3, int sleep = 1, int interSleep = 0);
    void KeyKeyKeyKeyPress(Keys key, Keys key2, Keys key3, Keys key4, int sleep = 1);
    void MouseWheel(int delta);

    /// <summary>Register a global hotkey. Dispose the result to unregister.</summary>
    IDisposable RegisterHotkey(HotkeyModifiers modifiers, Keys key, Action handler);

    /// <summary>
    /// Passive observer of physical key presses (legacy low level hook, used for FN-key / M-key / Aura key handling).
    /// Handler gets (key, isDown). Dispose to stop.
    /// </summary>
    IDisposable ListenKeys(Action<Keys, bool> handler);

    // on-screen keyboard
    bool OnScreenKeyboardSupported { get; }
    void ToggleOnScreenKeyboard();
    bool IsOnScreenKeyboardOpen();
}
