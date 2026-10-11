using GHelper.Helpers;

namespace GHelper.Input
{
    /// <summary>
    /// Maps the legacy <see cref="ToastIcon"/> values to the icon keys passed to <c>IUiHost.Toast</c>
    /// (named after the legacy resource: brightness_up.png, backlight_down.png, ...).
    /// </summary>
    public static class ToastIconKeys
    {
        public static string Get(ToastIcon icon) => icon switch
        {
            ToastIcon.BrightnessUp => "brightness_up",
            ToastIcon.BrightnessDown => "brightness_down",
            ToastIcon.BacklightUp => "backlight_up",
            ToastIcon.BacklightDown => "backlight_down",
            ToastIcon.Touchpad => "touchpad",
            ToastIcon.Microphone => "microphone",
            ToastIcon.MicrophoneMute => "microphone_mute",
            ToastIcon.FnLock => "fnlock",
            ToastIcon.Battery => "battery",
            ToastIcon.Charger => "charger",
            ToastIcon.Controller => "controller",
            _ => icon.ToString().ToLowerInvariant(),
        };
    }
}
