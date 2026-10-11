using GHelper.Input;

namespace GHelper.Platform.Linux;

/// <summary>Windows virtual key (GHelper.Input.Keys) &lt;-&gt; Linux evdev KEY_* code.</summary>
public static class LinuxKeyMap
{
    // Keys.LWin etc are Windows names; evdev numbers are from linux/input-event-codes.h
    private static readonly Dictionary<Keys, int> ToEvdev = Build();
    private static readonly Dictionary<int, Keys> FromEvdev = ToEvdev
        .GroupBy(p => p.Value)
        .ToDictionary(g => g.Key, g => g.First().Key);

    public const int KEY_LEFTCTRL = 29, KEY_RIGHTCTRL = 97, KEY_LEFTSHIFT = 42, KEY_RIGHTSHIFT = 54,
        KEY_LEFTALT = 56, KEY_RIGHTALT = 100, KEY_LEFTMETA = 125, KEY_RIGHTMETA = 126;

    public static bool TryGetEvdev(Keys key, out int code) => ToEvdev.TryGetValue(key, out code);

    public static bool TryGetKey(int evdevCode, out Keys key) => FromEvdev.TryGetValue(evdevCode, out key);

    public static HotkeyModifiers ModifierOf(int evdevCode) => evdevCode switch
    {
        KEY_LEFTCTRL or KEY_RIGHTCTRL => HotkeyModifiers.Control,
        KEY_LEFTSHIFT or KEY_RIGHTSHIFT => HotkeyModifiers.Shift,
        KEY_LEFTALT or KEY_RIGHTALT => HotkeyModifiers.Alt,
        KEY_LEFTMETA or KEY_RIGHTMETA => HotkeyModifiers.Win,
        _ => HotkeyModifiers.None,
    };

    private static Dictionary<Keys, int> Build()
    {
        var map = new Dictionary<Keys, int>
        {
            [Keys.Escape] = 1,
            [Keys.D1] = 2, [Keys.D2] = 3, [Keys.D3] = 4, [Keys.D4] = 5, [Keys.D5] = 6,
            [Keys.D6] = 7, [Keys.D7] = 8, [Keys.D8] = 9, [Keys.D9] = 10, [Keys.D0] = 11,
            [Keys.OemMinus] = 12, [Keys.Oemplus] = 13, [Keys.Back] = 14, [Keys.Tab] = 15,
            [Keys.Q] = 16, [Keys.W] = 17, [Keys.E] = 18, [Keys.R] = 19, [Keys.T] = 20, [Keys.Y] = 21,
            [Keys.U] = 22, [Keys.I] = 23, [Keys.O] = 24, [Keys.P] = 25,
            [Keys.OemOpenBrackets] = 26, [Keys.OemCloseBrackets] = 27, [Keys.Enter] = 28,
            [Keys.LControlKey] = 29,
            [Keys.A] = 30, [Keys.S] = 31, [Keys.D] = 32, [Keys.F] = 33, [Keys.G] = 34, [Keys.H] = 35,
            [Keys.J] = 36, [Keys.K] = 37, [Keys.L] = 38,
            [Keys.OemSemicolon] = 39, [Keys.OemQuotes] = 40, [Keys.Oemtilde] = 41,
            [Keys.LShiftKey] = 42, [Keys.OemPipe] = 43,
            [Keys.Z] = 44, [Keys.X] = 45, [Keys.C] = 46, [Keys.V] = 47, [Keys.B] = 48, [Keys.N] = 49, [Keys.M] = 50,
            [Keys.Oemcomma] = 51, [Keys.OemPeriod] = 52, [Keys.OemQuestion] = 53,
            [Keys.RShiftKey] = 54, [Keys.Multiply] = 55, [Keys.LMenu] = 56, [Keys.Space] = 57, [Keys.CapsLock] = 58,
            [Keys.F1] = 59, [Keys.F2] = 60, [Keys.F3] = 61, [Keys.F4] = 62, [Keys.F5] = 63, [Keys.F6] = 64,
            [Keys.F7] = 65, [Keys.F8] = 66, [Keys.F9] = 67, [Keys.F10] = 68,
            [Keys.NumLock] = 69, [Keys.Scroll] = 70,
            [Keys.NumPad7] = 71, [Keys.NumPad8] = 72, [Keys.NumPad9] = 73, [Keys.Subtract] = 74,
            [Keys.NumPad4] = 75, [Keys.NumPad5] = 76, [Keys.NumPad6] = 77, [Keys.Add] = 78,
            [Keys.NumPad1] = 79, [Keys.NumPad2] = 80, [Keys.NumPad3] = 81, [Keys.NumPad0] = 82, [Keys.Decimal] = 83,
            [Keys.OemBackslash] = 86, [Keys.F11] = 87, [Keys.F12] = 88,
            [Keys.RControlKey] = 97, [Keys.Divide] = 98, [Keys.PrintScreen] = 99, [Keys.RMenu] = 100,
            [Keys.Home] = 102, [Keys.Up] = 103, [Keys.PageUp] = 104, [Keys.Left] = 105, [Keys.Right] = 106,
            [Keys.End] = 107, [Keys.Down] = 108, [Keys.PageDown] = 109, [Keys.Insert] = 110, [Keys.Delete] = 111,
            [Keys.VolumeMute] = 113, [Keys.VolumeDown] = 114, [Keys.VolumeUp] = 115,
            [Keys.Pause] = 119,
            [Keys.LWin] = 125, [Keys.RWin] = 126, [Keys.Apps] = 127,
            [Keys.Sleep] = 142,
            [Keys.LaunchMail] = 155, [Keys.BrowserFavorites] = 156, [Keys.BrowserBack] = 158, [Keys.BrowserForward] = 159,
            [Keys.MediaNextTrack] = 163, [Keys.MediaPlayPause] = 164, [Keys.MediaPreviousTrack] = 165, [Keys.MediaStop] = 166,
            [Keys.BrowserHome] = 172, [Keys.BrowserRefresh] = 173, [Keys.BrowserSearch] = 217,
            [Keys.LaunchApplication2] = 140, // calculator
            [Keys.SelectMedia] = 226,
            [Keys.F13] = 183, [Keys.F14] = 184, [Keys.F15] = 185, [Keys.F16] = 186, [Keys.F17] = 187, [Keys.F18] = 188,
            [Keys.F19] = 189, [Keys.F20] = 190, [Keys.F21] = 191, [Keys.F22] = 192, [Keys.F23] = 193, [Keys.F24] = 194,
        };
        return map;
    }
}
