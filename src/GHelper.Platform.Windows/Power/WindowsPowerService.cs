using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GHelper.Platform.Windows.Interop;
using Microsoft.Win32;

namespace GHelper.Platform.Windows.Power;

/// <summary>
/// Windows implementation of <see cref="IPowerService"/>: power overlays / plans / ASPM / lid / hibernate through powrprof
/// (legacy PowerNative) and system power events through Microsoft.Win32.SystemEvents plus a message-only window that
/// registers RegisterPowerSettingNotification / RegisterSuspendResumeNotification (legacy Program + Settings.WndProc).
/// Nothing touches the OS until a member is used (events start lazily with the first subscriber).
/// </summary>
public sealed class WindowsPowerService : IPowerService, IPowerModeCatalog, ISessionEndingEvents, IDisposable
{
    // power settings (sub group, setting)
    private static readonly Guid GUID_CPU = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid GUID_BOOST = new("be337238-0d82-4146-a960-4f3749d470c7");

    private static readonly Guid GUID_SLEEP_SUBGROUP = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static readonly Guid GUID_HIBERNATEIDLE = new("9d7815a6-7ee4-497e-8888-515a05f02364");

    private static readonly Guid GUID_SYSTEM_BUTTON_SUBGROUP = new("4f971e89-eebd-4455-a8de-9e59040e7347");
    private static readonly Guid GUID_LIDACTION = new("5CA83367-6E45-459F-A27B-476B1D01C936");

    private static readonly Guid GUID_SUB_PCIEXPRESS = new("501a4d13-42af-4429-9fd1-a8218c268e20");
    private static readonly Guid GUID_PCI_EXPRESS_ASPM = new("ee12f906-d277-404b-b6da-e5fa1a576df5");

    private static readonly Guid GUID_SUB_NONE = new("fea3413e-7e05-4911-9a71-700331f1c294");
    private static readonly Guid GUID_CONNECTIVITY_IN_STANDBY = new("f15576e8-98b7-4186-b944-eafa664402d9");

    // --- state ------------------------------------------------------------------------------

    private readonly object eventLock = new();
    private bool listening;
    private MessageWindow? window;
    private readonly List<IntPtr> notificationHandles = new();
    private IntPtr suspendResumeHandle;
    private bool suspended;
    private long lastResumeTick = -10000;
    private volatile bool lidClosed;

    // --- events (the first subscriber starts the OS listeners) -------------------------------

    private event Action<PowerSource>? powerSourceChanged;
    private event Action? suspending;
    private event Action? resumed;
    private event Action<bool>? lidChanged;
    private event Action<bool>? displayStateChanged;
    private event Action<bool>? sessionLockChanged;
    private event Action<bool>? batterySaverChanged;
    private event Action? userPreferencesChanged;
    private event Action? sessionEnding;

    public event Action<PowerSource>? PowerSourceChanged { add { powerSourceChanged += value; EnsureListening(); } remove { powerSourceChanged -= value; } }
    public event Action? Suspending { add { suspending += value; EnsureListening(); } remove { suspending -= value; } }
    public event Action? Resumed { add { resumed += value; EnsureListening(); } remove { resumed -= value; } }
    public event Action<bool>? LidChanged { add { lidChanged += value; EnsureListening(); } remove { lidChanged -= value; } }
    public event Action<bool>? DisplayStateChanged { add { displayStateChanged += value; EnsureListening(); } remove { displayStateChanged -= value; } }
    public event Action<bool>? SessionLockChanged { add { sessionLockChanged += value; EnsureListening(); } remove { sessionLockChanged -= value; } }
    public event Action<bool>? BatterySaverChanged { add { batterySaverChanged += value; EnsureListening(); } remove { batterySaverChanged -= value; } }
    public event Action? UserPreferencesChanged { add { userPreferencesChanged += value; EnsureListening(); } remove { userPreferencesChanged -= value; } }
    public event Action? SessionEnding { add { sessionEnding += value; EnsureListening(); } remove { sessionEnding -= value; } }

    // explicit: a public "PowerModes" member would shadow Microsoft.Win32.PowerModes used by the event handlers below
    IReadOnlyDictionary<string, string> IPowerModeCatalog.PowerModes => PowerSchemes.Names;

    // --- power plan / profile -----------------------------------------------------------------

    public string GetPowerMode()
    {
        if (!OperatingSystem.IsWindows()) return PowerSchemes.Balanced;

        if (GetActiveScheme().ToString() == PowerSchemes.PlanHighPerformance) return PowerSchemes.PlanHighPerformance;
        PowrProf.PowerGetEffectiveOverlayScheme(out Guid activeScheme);
        return activeScheme.ToString();
    }

    public void SetPowerMode(string scheme)
    {
        if (!OperatingSystem.IsWindows()) return;

        if (scheme == PowerSchemes.PlanHighPerformance)
        {
            SetPowerPlan(scheme);
            return;
        }

        // Power plan from config, defaulting to balanced
        string plan = PowerSchemes.ResolvePlan(
            scheme,
            AppConfig.GetModeString("scheme"),
            AppConfig.GetModeString("scheme_usbc"),
            Program.currentSource == Program.PowerSource.USBC);
        SetPowerPlan(plan);

        if (!PowerSchemes.IsOverlay(scheme)) return;

        Guid guidScheme = new(scheme);
        uint status = PowrProf.PowerGetEffectiveOverlayScheme(out Guid activeScheme);

        if (GetBatterySaverStatus())
        {
            Logger.WriteLine("Battery Saver detected");
            return;
        }

        if (status != 0 || activeScheme != guidScheme)
        {
            status = PowrProf.PowerSetActiveOverlayScheme(guidScheme);
            Logger.WriteLine("Power Mode " + activeScheme + " -> " + scheme + ":" + (status == 0 ? "OK" : status));
        }
    }

    public void SetPowerMode(int mode) => SetPowerMode(PowerSchemes.GetDefaultPowerMode(mode));

    public string GetDefaultPowerMode(int mode) => PowerSchemes.GetDefaultPowerMode(mode);

    public void SetPowerPlan(string scheme)
    {
        if (!OperatingSystem.IsWindows()) return;

        // overlays are handled by SetPowerMode
        if (PowerSchemes.IsOverlay(scheme)) return;

        scheme ??= PowerSchemes.PlanBalanced;
        string activeScheme = GetActiveScheme().ToString();
        if (string.Equals(activeScheme, scheme, StringComparison.OrdinalIgnoreCase)) return;

        Guid guid = new(scheme);
        uint status = PowrProf.PowerSetActiveScheme(IntPtr.Zero, ref guid);
        Logger.WriteLine($"Power Plan {activeScheme} -> {scheme} :" + (status == 0 ? "OK" : status));
    }

    // --- CPU / platform tunables --------------------------------------------------------------

    public int GetCPUBoost()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        return ReadAc(GetActiveScheme(), GUID_CPU, GUID_BOOST);
    }

    public void SetCPUBoost(int boost = 0)
    {
        if (!OperatingSystem.IsWindows()) return;
        Guid scheme = GetActiveScheme();
        if (boost == ReadAc(scheme, GUID_CPU, GUID_BOOST)) return;

        WriteAc(scheme, GUID_CPU, GUID_BOOST, boost);
        Activate(scheme);
        WriteDc(scheme, GUID_CPU, GUID_BOOST, boost);
        Activate(scheme);

        Logger.WriteLine("Boost " + boost);
    }

    public int GetASPM()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        return ReadAc(GetActiveScheme(), GUID_SUB_PCIEXPRESS, GUID_PCI_EXPRESS_ASPM);
    }

    public void SetASPM(int status = 0)
    {
        if (!OperatingSystem.IsWindows()) return;
        Guid scheme = GetActiveScheme();
        int current = ReadAc(scheme, GUID_SUB_PCIEXPRESS, GUID_PCI_EXPRESS_ASPM);
        if (current == status) return;

        WriteAc(scheme, GUID_SUB_PCIEXPRESS, GUID_PCI_EXPRESS_ASPM, status);
        Activate(scheme);
        Logger.WriteLine($"Changed AC ASPM {current} -> {status}");
    }

    public void SetBalancedASPM(int status = 0)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (GetActiveScheme().ToString() != PowerSchemes.PlanBalanced) return;
        SetASPM(status);
    }

    public void SetConnectivityInStandby(int ac = 0, int dc = 0)
    {
        if (!OperatingSystem.IsWindows()) return;
        Guid scheme = GetActiveScheme();

        using (var key = Registry.LocalMachine.OpenSubKey(
            $@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\{scheme}\{GUID_CONNECTIVITY_IN_STANDBY}"))
        {
            if (key != null && (int?)key.GetValue("ACSettingIndex") == ac && (int?)key.GetValue("DCSettingIndex") == dc) return;
        }

        uint hrAC = WriteAc(scheme, GUID_SUB_NONE, GUID_CONNECTIVITY_IN_STANDBY, ac);
        uint hrDC = WriteDc(scheme, GUID_SUB_NONE, GUID_CONNECTIVITY_IN_STANDBY, dc);
        Activate(scheme);
        Logger.WriteLine($"Connectivity in Standby {ac}/{dc}: " + (hrAC == 0 && hrDC == 0 ? "OK" : $"{hrAC}/{hrDC}"));
    }

    // --- lid / sleep --------------------------------------------------------------------------

    public int GetLidAction(bool ac)
    {
        if (!OperatingSystem.IsWindows()) return 0;
        Guid scheme = GetActiveScheme();
        return ac
            ? ReadAc(scheme, GUID_SYSTEM_BUTTON_SUBGROUP, GUID_LIDACTION)
            : ReadDc(scheme, GUID_SYSTEM_BUTTON_SUBGROUP, GUID_LIDACTION);
    }

    /// <summary>1: do nothing, 2: sleep, 3: hibernate, 4: shutdown.</summary>
    public void SetLidAction(int action, bool acOnly = false)
    {
        if (!OperatingSystem.IsWindows()) return;
        Guid scheme = GetActiveScheme();

        WriteAc(scheme, GUID_SYSTEM_BUTTON_SUBGROUP, GUID_LIDACTION, action);
        Activate(scheme);

        if (!acOnly)
        {
            WriteDc(scheme, GUID_SYSTEM_BUTTON_SUBGROUP, GUID_LIDACTION, action);
            Activate(scheme);
        }

        Logger.WriteLine("Changed Lid Action to " + action);
    }

    public int GetHibernateAfter()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        int seconds = ReadDc(GetActiveScheme(), GUID_SLEEP_SUBGROUP, GUID_HIBERNATEIDLE);
        Logger.WriteLine("Hibernate after " + seconds);
        return seconds / 60;
    }

    public void SetHibernateAfter(int minutes)
    {
        if (!OperatingSystem.IsWindows()) return;
        int seconds = minutes * 60;
        Guid scheme = GetActiveScheme();
        uint hr = WriteDc(scheme, GUID_SLEEP_SUBGROUP, GUID_HIBERNATEIDLE, seconds);
        Activate(scheme);
        Logger.WriteLine("Setting Hibernate after " + seconds + ": " + (hr == 0 ? "OK" : hr));
    }

    public bool GetBatterySaverStatus()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            object? status = Registry.GetValue(@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control\Power", "EnergySaverState", null);
            if (status is null)
                return PowrProf.GetSystemPowerStatus(out var sps) && sps.SystemStatusFlag > 0;
            return (int)status == 1;
        }
        catch (Exception e)
        {
            Logger.WriteLine("Can't check EnergySaverState" + e.Message);
            return false;
        }
    }

    // --- state --------------------------------------------------------------------------------

    public PowerSource GetPowerSource()
    {
        if (!OperatingSystem.IsWindows()) return PowerSource.Unknown;
        try
        {
            return PowrProf.GetSystemPowerStatus(out var sps) ? PowerSettingDecoder.ToPowerSource(sps.ACLineStatus) : PowerSource.Unknown;
        }
        catch
        {
            return PowerSource.Unknown;
        }
    }

    public bool IsLidClosed()
    {
        EnsureListening(); // the first LIDSWITCH notification carries the current state
        return lidClosed;
    }

    public void Sleep()
    {
        if (!OperatingSystem.IsWindows()) return;
        Win32.SetSuspendState(false, false, false);
    }

    public void LockWorkstation()
    {
        if (!OperatingSystem.IsWindows()) return;
        Win32.LockWorkStation();
    }

    // --- powrprof helpers ---------------------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static Guid GetActiveScheme()
    {
        if (PowrProf.PowerGetActiveScheme(IntPtr.Zero, out IntPtr ptr) != 0 || ptr == IntPtr.Zero) return Guid.Empty;
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { Win32.LocalFree(ptr); }
    }

    [SupportedOSPlatform("windows")]
    private static int ReadAc(Guid scheme, Guid subGroup, Guid setting)
    {
        PowrProf.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subGroup, ref setting, out uint value);
        return (int)value;
    }

    [SupportedOSPlatform("windows")]
    private static int ReadDc(Guid scheme, Guid subGroup, Guid setting)
    {
        PowrProf.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subGroup, ref setting, out uint value);
        return (int)value;
    }

    [SupportedOSPlatform("windows")]
    private static uint WriteAc(Guid scheme, Guid subGroup, Guid setting, int value) =>
        PowrProf.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subGroup, ref setting, value);

    [SupportedOSPlatform("windows")]
    private static uint WriteDc(Guid scheme, Guid subGroup, Guid setting, int value) =>
        PowrProf.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subGroup, ref setting, value);

    [SupportedOSPlatform("windows")]
    private static void Activate(Guid scheme) => PowrProf.PowerSetActiveScheme(IntPtr.Zero, ref scheme);

    // --- system events ------------------------------------------------------------------------

    private void EnsureListening()
    {
        if (listening) return;
        if (!OperatingSystem.IsWindows()) return;

        lock (eventLock)
        {
            if (listening) return;
            listening = true;
        }

        try { StartListening(); }
        catch (Exception ex) { Logger.WriteLine("Power events: " + ex.Message); }
    }

    [SupportedOSPlatform("windows")]
    private void StartListening()
    {
        window = new MessageWindow();
        window.MessageReceived += OnWindowMessage;

        if (window.IsAlive)
        {
            foreach (Guid setting in PowerSettingDecoder.AllSettings)
            {
                Guid guid = setting;
                IntPtr handle = Win32.RegisterPowerSettingNotification(window.Handle, ref guid, Win32.DEVICE_NOTIFY_WINDOW_HANDLE);
                if (handle != IntPtr.Zero) notificationHandles.Add(handle);
            }

            suspendResumeHandle = Win32.RegisterSuspendResumeNotification(window.Handle, Win32.DEVICE_NOTIFY_WINDOW_HANDLE);
        }

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.SessionEnding += OnSessionEnding;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnWindowMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != Win32.WM_POWERBROADCAST) return;

        switch ((int)wParam)
        {
            case Win32.PBT_APMSUSPEND:
                RaiseSuspending();
                break;

            case Win32.PBT_APMRESUMEAUTOMATIC:
            case Win32.PBT_APMRESUMESUSPEND:
                RaiseResumed();
                break;

            case Win32.PBT_POWERSETTINGCHANGE:
                if (lParam == IntPtr.Zero) break;
                var setting = Marshal.PtrToStructure<Win32.POWERBROADCAST_SETTING>(lParam);
                HandlePowerSetting(PowerSettingDecoder.Decode(setting.PowerSetting, setting.Data));
                break;
        }
    }

    private void HandlePowerSetting(PowerSettingEvent e)
    {
        switch (e.Kind)
        {
            case PowerSettingKind.Lid:
                lidClosed = e.Flag;
                Logger.WriteLine(e.Flag ? "Lid Closed" : "Lid Open");
                Raise(() => lidChanged?.Invoke(e.Flag));
                break;
            case PowerSettingKind.Display:
                Logger.WriteLine(e.Flag ? "Monitor Power On" : "Monitor Power Off");
                Raise(() => displayStateChanged?.Invoke(e.Flag));
                break;
            case PowerSettingKind.BatterySaver:
                Logger.WriteLine("Battery Saver: " + e.Flag);
                Raise(() => batterySaverChanged?.Invoke(e.Flag));
                break;
            case PowerSettingKind.PowerSource:
                Raise(() => powerSourceChanged?.Invoke(e.Flag ? PowerSource.AC : PowerSource.Battery));
                break;
        }
    }

    private void RaiseSuspending()
    {
        // WM_POWERBROADCAST and SystemEvents both report the transition: deliver once per cycle
        lock (eventLock)
        {
            if (suspended) return;
            suspended = true;
        }

        Logger.WriteLine("System Suspend");
        Raise(() => suspending?.Invoke());
    }

    private void RaiseResumed()
    {
        lock (eventLock)
        {
            long now = Environment.TickCount64;
            // duplicate notification of the same resume (message window + SystemEvents)
            if (!suspended && now - lastResumeTick < 3000) return;
            suspended = false;
            lastResumeTick = now;
        }

        Logger.WriteLine("System Resume");
        Raise(() => resumed?.Invoke());
    }

    [SupportedOSPlatform("windows")]
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                RaiseSuspending();
                break;
            case PowerModes.Resume:
                RaiseResumed();
                break;
            case PowerModes.StatusChange:
                var source = GetPowerSource();
                Raise(() => powerSourceChanged?.Invoke(source));
                break;
        }
    }

    [SupportedOSPlatform("windows")]
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
                Logger.WriteLine("Session:" + e.Reason);
                Raise(() => sessionLockChanged?.Invoke(true));
                break;
            case SessionSwitchReason.SessionLogon:
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
                Logger.WriteLine("Session:" + e.Reason);
                Raise(() => sessionLockChanged?.Invoke(false));
                break;
        }
    }

    [SupportedOSPlatform("windows")]
    private void OnSessionEnding(object sender, SessionEndingEventArgs e) => Raise(() => sessionEnding?.Invoke());

    [SupportedOSPlatform("windows")]
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            Raise(() => userPreferencesChanged?.Invoke());
    }

    private static void Raise(Action action)
    {
        try { action(); }
        catch (Exception ex) { Logger.WriteLine("Power event handler: " + ex.Message); }
    }

    public void Dispose()
    {
        if (!listening || !OperatingSystem.IsWindows()) return;
        listening = false;

        StopListening();
    }

    [SupportedOSPlatform("windows")]
    private void StopListening()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.SessionEnding -= OnSessionEnding;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

        foreach (IntPtr handle in notificationHandles) Win32.UnregisterPowerSettingNotification(handle);
        notificationHandles.Clear();

        if (suspendResumeHandle != IntPtr.Zero)
        {
            Win32.UnregisterSuspendResumeNotification(suspendResumeHandle);
            suspendResumeHandle = IntPtr.Zero;
        }

        window?.Dispose();
        window = null;
    }
}
