using GHelper.Input;

namespace GHelper.Platform.Windows.Input;

/// <summary>Pure translation helpers between the platform neutral contract types and Win32 values.</summary>
internal static class KeyMapper
{
    // RegisterHotKey fsModifiers
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    public static uint ToWin32Modifiers(HotkeyModifiers modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) result |= MOD_ALT;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) result |= MOD_CONTROL;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) result |= MOD_SHIFT;
        if (modifiers.HasFlag(HotkeyModifiers.Win)) result |= MOD_WIN;
        return result;
    }

    public static HotkeyModifiers FromWin32Modifiers(uint modifiers)
    {
        var result = HotkeyModifiers.None;
        if ((modifiers & MOD_ALT) != 0) result |= HotkeyModifiers.Alt;
        if ((modifiers & MOD_CONTROL) != 0) result |= HotkeyModifiers.Control;
        if ((modifiers & MOD_SHIFT) != 0) result |= HotkeyModifiers.Shift;
        if ((modifiers & MOD_WIN) != 0) result |= HotkeyModifiers.Win;
        return result;
    }

    /// <summary>Virtual key of a Keys value (modifier flag bits stripped).</summary>
    public static byte ToVirtualKey(Keys key) => (byte)((int)key & 0xFF);

    /// <summary>Keys value for a hook vkCode.</summary>
    public static Keys FromVirtualKey(uint vkCode) => (Keys)(int)(vkCode & 0xFFFF);

    public static bool IsMouseButton(Keys key) => key is Keys.LButton or Keys.RButton or Keys.MButton;
}
