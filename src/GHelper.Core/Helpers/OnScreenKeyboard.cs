using GHelper.Platform;

namespace GHelper.Helpers
{
    /// <summary>Legacy OnScreenKeyboard (TabTip COM) as a facade over <see cref="IInputService"/>.</summary>
    public static class OnScreenKeyboard
    {
        private static IInputService Svc => PlatformServices.Current.Input;

        public static bool IsSupported => Svc.OnScreenKeyboardSupported;

        public static void ToggleVisibility()
        {
            if (!IsSupported) return;
            Svc.ToggleOnScreenKeyboard();
        }

        public static void Show()
        {
            if (!IsSupported) return;
            if (!IsOpen()) Svc.ToggleOnScreenKeyboard();
        }

        public static void Hide()
        {
            if (!IsSupported) return;
            if (IsOpen()) Svc.ToggleOnScreenKeyboard();
        }

        /// <returns>true when a keyboard was open (and got closed).</returns>
        public static bool Close()
        {
            if (!IsSupported) return false;
            bool active = IsOpen();
            if (active) Svc.ToggleOnScreenKeyboard();
            return active;
        }

        public static bool IsOpen()
        {
            if (!IsSupported) return false;
            return Svc.IsOnScreenKeyboardOpen();
        }
    }
}
