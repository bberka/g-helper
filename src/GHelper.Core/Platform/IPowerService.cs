namespace GHelper.Platform;

public enum PowerSource { Unknown, Battery, AC }

/// <summary>Mirror of the legacy static GHelper.Mode.PowerNative plus system power events (SystemEvents / WM_POWERBROADCAST).</summary>
public interface IPowerService
{
    // --- power plan / profile --------------------------------------------------------------
    /// <summary>Windows: power overlay/plan guid string. Linux: power-profiles-daemon / platform_profile name.</summary>
    string GetPowerMode();
    void SetPowerMode(string scheme);
    /// <summary>Maps AsusMode (0 balanced, 1 turbo, 2 silent) to the OS profile.</summary>
    void SetPowerMode(int mode);
    string GetDefaultPowerMode(int mode);
    void SetPowerPlan(string scheme);

    // --- CPU / platform tunables (silently ignore when unsupported) -----------------------
    int GetCPUBoost();
    void SetCPUBoost(int boost = 0);
    int GetASPM();
    void SetASPM(int status = 0);
    void SetBalancedASPM(int status = 0);
    void SetConnectivityInStandby(int ac = 0, int dc = 0);

    // --- lid / sleep ----------------------------------------------------------------------
    int GetLidAction(bool ac);
    void SetLidAction(int action, bool acOnly = false);
    int GetHibernateAfter();
    void SetHibernateAfter(int minutes);
    bool GetBatterySaverStatus();

    // --- state + events -------------------------------------------------------------------
    PowerSource GetPowerSource();
    bool IsLidClosed();

    event Action<PowerSource>? PowerSourceChanged;
    event Action? Suspending;
    event Action? Resumed;
    event Action<bool>? LidChanged;           // true = closed
    event Action<bool>? DisplayStateChanged;  // true = on
    event Action<bool>? SessionLockChanged;   // true = locked
    event Action<bool>? BatterySaverChanged;  // true = active
    event Action? UserPreferencesChanged;     // theme / display settings changed

    /// <summary>Hotkey actions.</summary>
    void Sleep();
    void LockWorkstation();

    // --- additive (platform-windows) ---------------------------------------------------------

    /// <summary>Turn the display off (legacy NativeMethods.TurnOffScreen, "screen" hotkey).</summary>
    void TurnOffDisplay() { }

    /// <summary>User logoff / system shutdown is in progress (legacy SystemEvents.SessionEnding). No-op default.</summary>
    event Action? SessionEnding { add { } remove { } }
}
