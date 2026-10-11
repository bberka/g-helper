using GHelper.Platform;

namespace GHelper.Input
{
    /// <summary>
    /// Small input related OS capabilities the legacy InputDispatcher got from Win32 directly (GetLastInputInfo,
    /// Control.ModifierKeys, SC_MONITORPOWER, the PrecisionTouchPad registry key, ConvertibleSlateMode, the ASUS
    /// driver folder). Implement it on the platform's <see cref="IInputService"/> object (or set
    /// <see cref="InputExtras.Override"/>). Every member degrades gracefully when the platform has no equivalent.
    /// </summary>
    public interface IInputExtras
    {
        /// <summary>Time since the last user input (backlight timeout). Zero when unknown.</summary>
        TimeSpan GetIdleTime();

        /// <summary>Switch the display(s) off (hotkey "screen off").</summary>
        void TurnOffScreen();

        /// <summary>Currently held modifier keys as flags (Keys.Shift / Keys.Control / Keys.Alt), legacy Control.ModifierKeys.</summary>
        Keys GetModifierKeys();

        /// <summary>Touchpad enabled in the OS settings (Windows PrecisionTouchPad\Status\Enabled). Null when unknown.</summary>
        bool? GetTouchpadEnabled();

        /// <summary>Windows ConvertibleSlateMode registry value (tablet mode flip). No-op elsewhere.</summary>
        void SetSlateMode(int status);

        /// <summary>
        /// Folder of the ASUS system control interface (Win32_SystemDriver ATKWMIACPIIO path) that contains AsusHotkey.exe
        /// (camera toggle). Null when not installed / not Windows.
        /// </summary>
        string? GetAsusSoftwarePath();
    }

    /// <summary>Resolves <see cref="IInputExtras"/>: explicit override, else the platform input service, else a null object.</summary>
    public static class InputExtras
    {
        public static IInputExtras? Override { get; set; }

        public static IInputExtras Current
        {
            get
            {
                if (Override is not null) return Override;
                if (PlatformServices.IsRegistered && PlatformServices.Current.Input is IInputExtras extras) return extras;
                return NullInputExtras.Instance;
            }
        }

        private sealed class NullInputExtras : IInputExtras
        {
            public static readonly NullInputExtras Instance = new();

            public TimeSpan GetIdleTime() => TimeSpan.Zero;
            public void TurnOffScreen() { }
            public Keys GetModifierKeys() => Keys.None;
            public bool? GetTouchpadEnabled() => null;
            public void SetSlateMode(int status) { }
            public string? GetAsusSoftwarePath() => null;
        }
    }

    /// <summary>
    /// Windows only: raw IOCTL to the touchpad number pad driver (\\.\AsusTPPC). Core builds the 0x800 byte request
    /// (see <see cref="NumberPad"/>), the platform opens the device (GENERIC_READ|WRITE, share 3, OPEN_EXISTING), calls
    /// DeviceIoControl(code, buffer, buffer) and returns the status. Implement on the platform's IInputService object.
    /// </summary>
    public interface INumberPadDriver
    {
        bool NumberPadIoctl(uint code, byte[] buffer);
    }

    /// <summary>
    /// Actions the hotkeys trigger that are bound to widgets of the main window in the UI layer
    /// (legacy Program.settingsForm.CycleAuraMode(...) etc.). Implement next to <see cref="IUiHost"/> in the app head;
    /// <see cref="Program.Ui"/> is probed for this interface. Always invoked on the UI thread.
    /// </summary>
    public interface IUiActionHandler
    {
        void RunAction(string action, int arg);
    }

    public static class UiActions
    {
        /// <summary>arg: +1 next, -1 previous. Cycles the keyboard Aura mode combo and shows a toast with its name.</summary>
        public const string CycleAuraMode = "cycle_aura_mode";
        /// <summary>arg: +1 / -1. Cycles the visual (display) mode combo and shows a toast with its name.</summary>
        public const string CycleVisualMode = "cycle_visual_mode";
        /// <summary>arg: +1 / -1. Matrix / slash brightness combo (AppConfig matrix_brightness) + SetDevice + toast.</summary>
        public const string CycleMatrix = "cycle_matrix";
        /// <summary>arg: 1 when triggered from a hotkey. Toggles the hardware overlay (legacy SettingsForm.ToggleOverlay).</summary>
        public const string ToggleOverlay = "toggle_overlay";

        /// <summary>Queue <paramref name="action"/> on the UI thread; returns false when the UI does not handle actions.</summary>
        public static bool Run(string action, int arg = 0)
        {
            if (Program.Ui is not IUiActionHandler handler) return false;
            Program.Ui.Post(() =>
            {
                try { handler.RunAction(action, arg); }
                catch (Exception ex) { Logger.WriteLine($"UI action {action}: {ex.Message}"); }
            });
            return true;
        }
    }
}
