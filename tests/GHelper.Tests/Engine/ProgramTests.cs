using GHelper.Battery;
using GHelper.Input;
using GHelper.Mode;
using GHelper.USB;
using System.Reflection;
using Xunit;

namespace GHelper.Tests.Engine;

public class ProgramTests : EngineTestBase
{
    public ProgramTests(EngineFixture fixture) : base(fixture)
    {
        CleanConfig("charge_limit", "shutdown_reset", "sleep_reset", "disable_power_event", "charger_delay", "usbc_profile",
            "performance_0", "performance_1", "performance_2", "performance_mode", "start_count");
        BatteryControl.chargeFull = false;
        TestStatics.Set(typeof(Program), "lastAuto", 0L);
        Program.modeControl = new ModeControl();
        Program.gpuControl = new GHelper.Gpu.GPUModeControl();
    }

    [Fact]
    public void ReadPowerSourceDetectsBattery()
    {
        P.PowerService.Source = Platform.PowerSource.Battery;
        Asus.Values[(uint)AsusACPI.ChargerMode] = AsusACPI.ChargerUSB;

        Assert.Equal(Program.PowerSource.Battery, Program.ReadPowerSource());
    }

    [Fact]
    public void ReadPowerSourceTellsBarrelFromUsbC()
    {
        P.PowerService.Source = Platform.PowerSource.AC;

        Assert.Equal(Program.PowerSource.Barrel, Program.ReadPowerSource());   // unsupported charger mode

        Asus.Values[(uint)AsusACPI.ChargerMode] = AsusACPI.ChargerBarrel;
        Assert.Equal(Program.PowerSource.Barrel, Program.ReadPowerSource());

        Asus.Values[(uint)AsusACPI.ChargerMode] = AsusACPI.ChargerUSB;
        Assert.Equal(Program.PowerSource.USBC, Program.ReadPowerSource());
    }

    [Fact]
    public void PerformanceKeyFollowsAcOrUsbcProfile()
    {
        Program.usbcProfile = false;
        P.PowerService.Source = Platform.PowerSource.Battery;
        Assert.Equal(0, Program.PerformanceKey());
        P.PowerService.Source = Platform.PowerSource.AC;
        Assert.Equal(1, Program.PerformanceKey());

        Program.usbcProfile = true;
        Asus.Values[(uint)AsusACPI.ChargerMode] = AsusACPI.ChargerUSB;
        Assert.Equal((int)Program.PowerSource.USBC, Program.PerformanceKey());
    }

    [Fact]
    public void SetAutoModesAppliesBatteryLimitAndIsThrottled()
    {
        AppConfig.Set("charge_limit", 80);
        P.PowerService.Source = Platform.PowerSource.AC;

        Assert.True(Program.SetAutoModes());
        Program.modeControl.WaitForApply();

        Assert.Equal(Program.PowerSource.Barrel, Program.currentSource);
        Assert.Contains(80, Asus.SetValues(AsusACPI.BatteryLimit));

        // second call inside the 3 s window is skipped
        Asus.ClearSets();
        Assert.False(Program.SetAutoModes());
        Assert.Empty(Asus.SetValues(AsusACPI.BatteryLimit));

        // wake up uses a longer window
        TestStatics.Set(typeof(Program), "lastAuto", DateTimeOffset.Now.ToUnixTimeMilliseconds() - 5000);
        Assert.False(Program.SetAutoModes(wakeup: true));
        Assert.True(Program.SetAutoModes());
        Program.modeControl.WaitForApply();
    }

    [Fact]
    public void PowerSettledSwitchesProfileOnlyWhenSourceChanged()
    {
        P.PowerService.Source = Platform.PowerSource.Battery;
        Program.currentSource = Program.PowerSource.Battery;
        Program.OnPowerSettled();
        Assert.Equal(Program.PowerSource.Battery, Program.currentSource);
        Assert.Empty(Asus.Sets);

        // charger connected: mode for AC is applied (stored AC mode = turbo)
        AppConfig.Set("performance_1", 1);
        P.PowerService.Source = Platform.PowerSource.AC;
        Program.OnPowerSettled();
        Program.modeControl.WaitForApply();

        Assert.Equal(Program.PowerSource.Barrel, Program.currentSource);
        Assert.Equal(1, Modes.GetCurrent());
    }

    [Fact]
    public void SchedulePowerCheckHonoursDisableFlag()
    {
        var timer = TestStatics.Get<System.Timers.Timer>(typeof(Program), "powerSettleTimer")!;
        timer.Stop();

        AppConfig.Set("disable_power_event", 1);
        Program.SchedulePowerCheck();
        Assert.False(timer.Enabled);

        AppConfig.Set("disable_power_event", 0);
        Program.SchedulePowerCheck();
        Assert.True(timer.Enabled);
        Assert.Equal(2000, timer.Interval);   // charger_delay not set: minimum 2 s

        AppConfig.Set("charger_delay", 4500);
        Program.SchedulePowerCheck();
        Assert.Equal(4500, timer.Interval);

        timer.Stop();
    }

    [Fact]
    public void SuspendResetsModeWhenConfigured()
    {
        AppConfig.Set("shutdown_reset", 1);
        Program.AttachPlatformEvents();

        P.PowerService.RaiseSuspending();

        Assert.Contains(AsusACPI.PerformanceBalanced, Asus.SetValues(AsusACPI.PerformanceMode));
        Assert.True(GHelper.Gpu.GPUModeControl.suspended);
    }

    [Fact]
    public void ResumeReappliesBatteryLimit()
    {
        AppConfig.Set("charge_limit", 70);
        Program.AttachPlatformEvents();
        GHelper.Gpu.GPUModeControl.suspended = true;

        P.PowerService.RaiseResumed();

        Assert.False(GHelper.Gpu.GPUModeControl.suspended);
        Assert.Equal(new[] { 70 }, Asus.SetValues(AsusACPI.BatteryLimit));
    }

    [Fact]
    public void AttachingTwiceSubscribesOnce()
    {
        AppConfig.Set("charge_limit", 70);
        Program.AttachPlatformEvents();
        Program.AttachPlatformEvents();

        P.PowerService.RaiseResumed();

        Assert.Single(Asus.SetValues(AsusACPI.BatteryLimit));
    }

    [Fact]
    public void DetachStopsEventHandling()
    {
        AppConfig.Set("charge_limit", 70);
        Program.AttachPlatformEvents();
        Program.DetachPlatformEvents();

        P.PowerService.RaiseResumed();
        P.PowerService.RaiseSuspending();

        Assert.Empty(Asus.Sets);
    }

    [Fact]
    public void LidEventsDriveBacklightFlag()
    {
        AppConfig.Set("charge_limit", 70);
        Program.AttachPlatformEvents();

        P.PowerService.RaiseLid(true);
        Assert.True(InputDispatcher.lidClose);
        Assert.Contains(70, Asus.SetValues(AsusACPI.BatteryLimit));

        P.PowerService.RaiseLid(false);
        Assert.False(InputDispatcher.lidClose);
    }

    [Fact]
    public void SessionLockTracksAuraState()
    {
        Program.AttachPlatformEvents();

        P.PowerService.RaiseSession(true);
        Assert.True(Aura.sessionLock);

        P.PowerService.RaiseSession(false);
        Assert.False(Aura.sessionLock);
        Assert.Contains("KillSmartDisplay", P.VendorService.Calls);
    }

    [Fact]
    public void ThemeAndBatterySaverNotifyUi()
    {
        Program.AttachPlatformEvents();

        P.PowerService.RaisePrefs();
        P.PowerService.RaiseSaver(true);

        Assert.Contains("theme", Ui.Refreshes);
        Assert.Contains("energysaver", Ui.Refreshes);
    }

    [Fact]
    public void SessionEndingIsOptionalCapability()
    {
        AppConfig.Set("charge_limit", 65);
        AppConfig.Set("shutdown_reset", 1);
        Program.AttachPlatformEvents();

        P.RaiseSessionEnding();

        Assert.Contains(65, Asus.SetValues(AsusACPI.BatteryLimit));
        Assert.Contains(AsusACPI.PerformanceBalanced, Asus.SetValues(AsusACPI.PerformanceMode));
    }

    [Fact]
    public void ChargeActionAppliesLimitAndQuits()
    {
        AppConfig.Set("charge_limit", 75);

        bool keepRunning = Program.Start(new[] { "charge" });

        Assert.False(keepRunning);
        Assert.Equal(new[] { 75 }, Asus.SetValues(AsusACPI.BatteryLimit));
        Assert.Equal(1, Ui.Quits);
    }

    [Fact]
    public void StartExitsWhenAcpiMissingOnAsusLaptop()
    {
        Asus.IsConnected = false;
        Ui.ConfirmAnswer = true;

        bool keepRunning = Program.Start(Array.Empty<string>());

        Assert.False(keepRunning);
        Assert.Equal(1, Ui.Confirms);
        Assert.Contains("OpenUrl:https://www.asus.com/support/FAQ/1047338/", P.ProcessService.Calls);
        Assert.Equal(1, Ui.Quits);
    }

    [Fact]
    public void StartRunsFullSequence()
    {
        AppConfig.Set("charge_limit", 85);
        int before = AppConfig.Get("start_count");

        bool keepRunning;
        try
        {
            keepRunning = Program.Start(Array.Empty<string>());
        }
        finally
        {
            Program.OnExit();
        }

        Assert.True(keepRunning);
        Assert.Equal(before + 1, AppConfig.Get("start_count"));
        Assert.NotNull(Program.acpi);
        Assert.NotNull(Program.modeControl);
        Assert.NotNull(Program.inputDispatcher);
        Assert.Contains(85, Asus.SetValues(AsusACPI.BatteryLimit));
        Assert.Contains("contextmenu", Ui.Refreshes);
        Assert.True(WaitFor(() => P.StartupService.Calls.Contains("StartupCheck")));
        Program.modeControl.WaitForApply();
    }

    [Fact]
    public void ToastPassesIconKey()
    {
        Program.Toast("hello", GHelper.Helpers.ToastIcon.FnLock);
        Program.Toast("plain");

        Assert.Equal(("hello", "FnLock"), Ui.Toasts[0]);
        Assert.Equal(("plain", null), Ui.Toasts[1]);
    }
}
