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

    // --- additive (platform-windows) ---------------------------------------------------------

    /// <summary>ELMB (extreme low motion blur) state of the internal panel: 0/1, or -1 when unsupported. Legacy ScreenELMB.Get.</summary>
    int GetElmbStatus() => -1;

    /// <summary>Legacy ScreenELMB.Set. No-op when unsupported.</summary>
    void SetElmbStatus(int status) { }

    /// <summary>Monitors added / removed or resolution, refresh rate, layout changed (legacy SystemEvents.DisplaySettingsChanged).</summary>
    event Action? DisplaySettingsChanged { add { } remove { } }
}
