namespace GHelper.Platform.Linux;

/// <summary>
/// Converts evdev events of the ASUS input devices into the numeric ATK event ids the legacy
/// InputDispatcher.HandleEvent understands (the ids Windows delivers through ATKACPI / AsusAtkWmiEvent).
///
/// Two sources:
///   - "Asus WMI hotkeys" (asus-nb-wmi sparse keymap): the driver reports the raw WMI event code as MSC_SCAN right
///     before the key, so the ATK id is exactly that scancode.
///   - hid-asus ("Asus Keyboard", N-KEY devices): vendor usage page 0xFF31, MSC_SCAN carries 0xFF31xxxx, the low
///     byte is the same ATK id.
/// When no scancode was seen (other drivers), a small table maps the Linux key codes the drivers use for ASUS keys.
///
/// Note: asus-wmi consumes some codes itself (keyboard backlight 0xC4/0xC5/0xC7, Fn lock 0x4E, fan / throttle policy
/// key 0x99/0xAE which cycles the platform profile) and never reports them as input events. Codes without a keymap
/// entry are reported as KEY_UNKNOWN with the code in MSC_SCAN, which this translator forwards unchanged.
/// </summary>
public static class AtkEventTranslator
{
    public const int HidAsusVendorPage = 0xFF31;
    public const int WmiArmouryScan = 0xFFFF01; // ASUS_WMI_KEY_ARMOURY (ROG key variant)

    // linux/input-event-codes.h
    internal const int KEY_PROG1 = 148, KEY_PROG2 = 149, KEY_PROG3 = 202, KEY_PROG4 = 203;
    internal const int KEY_F13 = 183, KEY_F14 = 184, KEY_F15 = 185, KEY_F16 = 186, KEY_F17 = 187, KEY_F18 = 188;
    internal const int KEY_PHONE = 169, KEY_CAMERA = 212, KEY_MICMUTE = 248, KEY_DISPLAY_OFF = 245;
    internal const int KEY_FN_F5 = 0x1d6, KEY_FN_F = 0x1e2, KEY_FN_ESC = 0x1d1;
    internal const int KEY_KBDILLUMTOGGLE = 228, KEY_KBDILLUMDOWN = 229, KEY_KBDILLUMUP = 230;
    internal const int KEY_TOUCHPAD_TOGGLE = 0x212, KEY_RFKILL = 247, KEY_SLEEP = 142;

    /// <summary>
    /// ATK ids the desktop environment / kernel already acts upon (brightness, volume, keyboard backlight, rfkill,
    /// touchpad, sleep, Fn lock). Forwarding them would make the legacy handler act a second time.
    /// </summary>
    public static readonly IReadOnlySet<int> DesktopHandled = new HashSet<int>
    {
        0x10, 0x20,         // brightness (Windows ids; never emitted by asus-wmi)
        0x2E, 0x2F,         // ASUS_WMI_BRN_DOWN / UP
        0x30, 0x31, 0x32,   // volume up / down / mute
        0x33, 0x35,         // lock / display off
        0x4E,               // Fn lock (hid-asus toggles it itself)
        0x6B,               // touchpad toggle
        0x6C,               // sleep
        0x88,               // rfkill / airplane
        0xC4, 0xC5, 0xC7,   // keyboard backlight up / down / toggle
    };

    /// <summary>
    /// Returns the ATK id for an evdev key press or -1 if the key is not an ASUS special key.
    /// </summary>
    /// <param name="isWmiDevice">true for the "Asus WMI hotkeys" device, false for HID based ASUS devices.</param>
    /// <param name="lastScan">value of the MSC_SCAN event that preceded the key, null if none.</param>
    public static int Translate(bool isWmiDevice, int? lastScan, int keyCode)
    {
        if (lastScan is int scan)
        {
            if (isWmiDevice)
            {
                if (scan == WmiArmouryScan) return 0x38;
                if (scan > 0 && scan <= 0xFF) return scan;
                return -1;
            }

            int page = (scan >> 16) & 0xFFFF;
            if (page == HidAsusVendorPage) return scan & 0xFF;
            // standard HID usage (keyboard 0x07, consumer 0x0C, ...): an ordinary key, never an ASUS special key
            return -1;
        }

        return FromKeyCode(isWmiDevice, keyCode);
    }

    private static int FromKeyCode(bool isWmiDevice, int keyCode)
    {
        if (isWmiDevice)
        {
            return keyCode switch
            {
                KEY_PROG3 => 0x38,          // ROG / Armoury Crate key
                KEY_PROG1 => 0x86,          // MyASUS key (legacy: M4 on old devices)
                KEY_PROG4 => 0xB3,          // Aura
                KEY_FN_F5 => 0xAE,          // Fn+F5 fan mode
                KEY_PHONE => 0x99,          // Fn+F5 on old models
                KEY_F15 => 0x5C,            // Power gear key
                KEY_FN_F => 0x9D,           // Zenbook DUO Fn+F
                KEY_CAMERA => 0x85,
                KEY_MICMUTE => 0x7C,
                KEY_PROG2 => 0xBD,          // lid flip
                KEY_KBDILLUMUP => 0xC4,
                KEY_KBDILLUMDOWN => 0xC5,
                KEY_TOUCHPAD_TOGGLE => 0x6B,
                KEY_RFKILL => 0x88,
                _ => -1,
            };
        }

        return keyCode switch
        {
            KEY_PROG1 => 0x38,          // ROG key (hid-asus usage 0x38; also MyASUS 0x86 / ProArt 0x8B)
            KEY_PROG2 => 0xBA,          // Fn+C / previous Aura
            KEY_PROG3 => 0x5C,          // Power4Gear / next Aura
            KEY_PROG4 => 0xAE,          // Fn+F5 fan symbol
            KEY_F13 => 0x6A,            // ScreenPad toggle
            KEY_F14 => 0x4B,            // arrows / page up-down toggle
            KEY_F15 => 0xA5,            // ROG Ally left back
            KEY_F16 => 0xA6,            // ROG Ally QAM
            KEY_F17 => 0xA7,            // ROG Ally ROG long press
            KEY_F18 => 0xA8,            // ROG Ally ROG long press release
            KEY_MICMUTE => 0x7C,
            KEY_CAMERA => 0x82,
            _ => -1,
        };
    }

    public static bool IsDesktopHandled(int atkId) => DesktopHandled.Contains(atkId);
}
