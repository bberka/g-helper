namespace GHelper.Platform;

/// <summary>Mirror of legacy ScreenNative / ScreenBrightness / ScreenCCD / TouchscreenHelper / gamma-ramp bits of DisplayNative.</summary>
public interface IDisplayService
{
    bool IsExternalDisplayConnected(bool log = false);

    /// <summary>Opaque id of the internal panel (Windows: GDI device name \\.\DISPLAY1, Linux: connector name eDP-1).</summary>
    string? FindLaptopScreen(bool log = false);

    int GetMaxRefreshRate(string? screen);
    int GetRefreshRate(string? screen);
    /// <returns>1 on success, otherwise a platform error code.</returns>
    int SetRefreshRate(string screen, int frequency);

    /// <summary>Panel backlight 0..100, or -1 if not controllable.</summary>
    int GetBrightness();
    void SetBrightness(int brightness);
    int AdjustBrightness(int delta);

    /// <summary>HDR / advanced colour on the internal panel. False when unsupported.</summary>
    bool GetHdrStatus(out bool acm, bool log = true);

    bool SupportsGammaRamp { get; }
    /// <summary>Apply a 256 entry per channel gamma ramp (legacy VisualControl.SetGamma). No-op when unsupported.</summary>
    void SetGammaRamp(string? screen, ushort[] red, ushort[] green, ushort[] blue);
    bool IsColorProfileInstalled(string profileName);
    Task InstallColorProfile(string profileName, byte[] iccData);

    /// <summary>null when there is no touchscreen / not supported.</summary>
    bool? GetTouchscreenStatus();
    void SetTouchscreenStatus(bool enabled);

    // --- ASUS Splendid / GameVisual (Windows only, defaults mean "not available") --------------------
    /// <summary>Directory of AsusSplendid.exe (legacy: folder of the ATKWMIACPIIO driver). Null when not installed.</summary>
    string? SplendidDirectory => null;
    /// <summary>HKCU\Software\ASUS\ARMOURY CRATE Service\GameVisual ActiveGVStatus (legacy default 1).</summary>
    int GetGameVisualStatus() => 1;
    void SetGameVisualStatus(int status) { }

    /// <summary>
    /// Panel ELMB / DPCD state access (legacy ScreenELMB.Aux: Intel ControlLib / NVAPI AUX channel).
    /// writeValue &lt; 0 reads. Returns the value, or -1 when unsupported.
    /// </summary>
    int ElmbAccess(int writeValue) => -1;

    /// <summary>
    /// ASUS Armoury "RefreshFlag" (Windows: HKLM WOW6432Node Uninstall {8714A8D1-0F08-4681-9DF6-A8C4607A58B4} RefreshFlag).
    /// Core only calls this when elevated.
    /// </summary>
    void SetAsusRefreshFlag(int value) { }

    // Display change notifications are the optional IDisplaySettingsEvents, screen grabbing for the Aura ambient mode the
    // optional IScreenCapture; both are implemented on the platform's IDisplayService object (see tasks/seams.md).
}
