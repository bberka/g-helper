using GHelper.Helpers;
using GHelper.Platform;
using System.Drawing;
using Xunit;

namespace GHelper.Tests.Engine;

public class ProcessHelperTests : EngineTestBase
{
    public ProcessHelperTests(EngineFixture fixture) : base(fixture) { }

    [Fact]
    public void ForwardsProcessCalls()
    {
        ProcessHelper.RunAsAdmin("cpu");
        ProcessHelper.KillByName("foo");
        ProcessHelper.SetPriority(System.Diagnostics.ProcessPriorityClass.BelowNormal);
        string output = ProcessHelper.RunCMD("cmd", "/c ver");
        ProcessHelper.KillSmartDisplayControl();

        Assert.Equal("out", output);
        Assert.Contains("RunAsAdmin:cpu:False", P.ProcessService.Calls);
        Assert.Contains("Kill:foo", P.ProcessService.Calls);
        Assert.Contains("Priority:BelowNormal", P.ProcessService.Calls);
        Assert.Contains("Run:cmd /c ver", P.ProcessService.Calls);
        Assert.Contains("KillSmartDisplay", P.VendorService.Calls);
    }

    [Fact]
    public void ElevationFollowsSystemInfo()
    {
        Assert.False(ProcessHelper.IsUserAdministrator());
        P.SystemInfo.IsElevated = true;
        Assert.True(ProcessHelper.IsUserAdministrator());
        Assert.False(ProcessHelper.IsRunningAsSystem());
    }

    [Fact]
    public void SingleInstanceRetriesUntilPreviousInstanceLeft()
    {
        P.ProcessService.AcquireFailures = 2;

        Assert.True(ProcessHelper.TryAcquireSingleInstance(3000));
        Assert.Equal(3, P.ProcessService.AcquireCalls);
    }

    [Fact]
    public void SingleInstanceGivesUpAfterTimeout()
    {
        P.ProcessService.AcquireFailures = 1000;

        Assert.False(ProcessHelper.TryAcquireSingleInstance(300));
    }

    [Fact]
    public void SecondInstanceSignalQuitsRunningInstance()
    {
        Assert.True(ProcessHelper.TryAcquireSingleInstance());

        P.ProcessService.SecondInstanceCallback!();

        Assert.Contains("Release", P.ProcessService.Calls);
        Assert.Equal(1, Ui.Quits);
    }

    [Fact]
    public void CheckAlreadyRunningAlertsAndQuitsWhenStuck()
    {
        P.ProcessService.AcquireFailures = 1000;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        ProcessHelper.CheckAlreadyRunning();

        Assert.Single(Ui.Alerts);
        Assert.Equal(1, Ui.Quits);
        Assert.True(watch.ElapsedMilliseconds >= 4000);
    }

    [Fact]
    public void KillByProcessSwallowsErrors()
    {
        // already exited process: Kill throws, helper must not
        using var p = System.Diagnostics.Process.GetCurrentProcess();
        var exited = new System.Diagnostics.Process();
        ProcessHelper.KillByProcess(exited);
    }

    [Fact]
    public void RestrictedUserSkipsEmptyCommand()
    {
        RestrictedProcessHelper.RunAsRestrictedUser("   ");
        RestrictedProcessHelper.RunAsRestrictedUser("notepad");

        Assert.Equal(new[] { "Restricted:notepad" }, P.ProcessService.Calls.Where(c => c.StartsWith("Restricted")).ToArray());
    }
}

public class StartupAndServiceFacadeTests : EngineTestBase
{
    public StartupAndServiceFacadeTests(EngineFixture fixture) : base(fixture)
    {
        CleanConfig("services_disabled", "services_skip");
    }

    [Fact]
    public void StartupForwardsToPlatform()
    {
        Startup.Schedule();
        Startup.UnSchedule();
        Startup.ReScheduleAdmin();
        Startup.StartupCheck();
        Startup.ScheduleCharge();
        Startup.UnscheduleCharge();

        Assert.Equal(new[] { "Schedule", "UnSchedule", "ReScheduleAdmin", "StartupCheck", "ScheduleCharge", "UnscheduleCharge" },
            P.StartupService.Calls.ToArray());
        Assert.True(Startup.IsSupported);
        Assert.False(Startup.IsScheduled());
    }

    [Fact]
    public void StoppingServicesRemembersTheChoice()
    {
        P.VendorService.RunningCount = 3;

        AsusService.StopAsusServices();

        Assert.Contains("StopAsusServices", P.VendorService.Calls);
        Assert.Equal(1, AppConfig.Get("services_disabled"));
    }

    [Fact]
    public void StartingServicesClearsTheChoice()
    {
        AppConfig.Set("services_disabled", 1);

        AsusService.StartAsusServices();

        Assert.Equal(0, AppConfig.Get("services_disabled"));
        Assert.Contains("StartAsusServices", P.VendorService.Calls);
    }

    [Fact]
    public void StopOnStartupRestopsRevivedServicesWhenElevated()
    {
        AppConfig.Set("services_disabled", 1);
        P.SystemInfo.IsElevated = true;
        P.VendorService.RunningCount = 2;

        AsusService.StopOnStartup();

        Assert.True(WaitFor(() => P.VendorService.Calls.Contains("StopAsusServices")));
    }

    [Fact]
    public void StopOnStartupIsOptIn()
    {
        P.VendorService.RunningCount = 2;
        P.SystemInfo.IsElevated = true;

        AsusService.StopOnStartup();                 // services_disabled not set
        AppConfig.Set("services_disabled", 1);
        AppConfig.Set("services_skip", 1);
        AsusService.StopOnStartup();                 // explicitly skipped
        AppConfig.Remove("services_skip");
        P.SystemInfo.IsElevated = false;
        AsusService.StopOnStartup();                 // not elevated

        Thread.Sleep(100);
        Assert.DoesNotContain("StopAsusServices", P.VendorService.Calls);
    }

    [Fact]
    public void GpuErrorIsForwarded()
    {
        Assert.Null(DeviceHelper.GetGpuError());
        P.VendorService.GpuError = "NVIDIA\nError 43";
        Assert.Equal("NVIDIA\nError 43", DeviceHelper.GetGpuError());
    }
}

public class InputAudioFacadeTests : EngineTestBase
{
    public InputAudioFacadeTests(EngineFixture fixture) : base(fixture)
    {
        P.InputService.Supported = true;
        P.InputService.Open = false;
        P.InputService.Toggles = 0;
        P.AudioService.Throw = false;
    }

    [Fact]
    public void ShowTogglesOnlyWhenClosed()
    {
        OnScreenKeyboard.Show();
        Assert.True(OnScreenKeyboard.IsOpen());
        OnScreenKeyboard.Show();

        Assert.Equal(1, P.InputService.Toggles);
    }

    [Fact]
    public void HideAndCloseToggleOnlyWhenOpen()
    {
        OnScreenKeyboard.Hide();
        Assert.False(OnScreenKeyboard.Close());
        Assert.Equal(0, P.InputService.Toggles);

        P.InputService.Open = true;
        Assert.True(OnScreenKeyboard.Close());
        Assert.False(P.InputService.Open);
    }

    [Fact]
    public void UnsupportedKeyboardIsNoOp()
    {
        P.InputService.Supported = false;

        OnScreenKeyboard.Show();
        OnScreenKeyboard.ToggleVisibility();

        Assert.Equal(0, P.InputService.Toggles);
        Assert.False(OnScreenKeyboard.IsOpen());
    }

    [Fact]
    public void AudioFailuresReportNotMuted()
    {
        P.AudioService.Throw = true;

        Assert.False(Audio.IsMuted());
        Assert.False(Audio.IsMicMuted());
        Audio.SubscribeMute(_ => { });   // must not throw
    }

    [Fact]
    public void AudioForwardsToggleAndSubscription()
    {
        P.AudioService.Muted = true;
        bool? seen = null;

        Audio.SubscribeMute(m => seen = m);

        Assert.True(seen);
        Assert.True(Audio.ToggleMicMute());
        Assert.True(Audio.IsMicMuted());
        Assert.True(Audio.IsMuted());
    }

    [Fact]
    public void TouchscreenForwardsToDisplayService()
    {
        Assert.Null(TouchscreenHelper.GetStatus());
        P.DisplayService.Touch = true;
        Assert.True(TouchscreenHelper.GetStatus());

        TouchscreenHelper.ToggleTouchscreen(false);
        Assert.False(P.DisplayService.LastTouchSet);
    }
}

public class LightingAndClamshellTests : EngineTestBase
{
    public LightingAndClamshellTests(EngineFixture fixture) : base(fixture)
    {
        P.VendorService.DynamicSupported = true;
        P.VendorService.DynamicEnabled = false;
        P.DisplayService.External = false;
        P.PowerService.LidAction = 1;
        CleanConfig("lighting_init", "toggle_clamshell_mode", "clamshell_battery", "clamshell_default_lid_action", "screen_force");
    }

    [Fact]
    public void DynamicLightingInitPulsesWindowsSettingOnce()
    {
        AppConfig.Set("lighting_init", 1);

        DynamicLightingHelper.Init();

        Assert.Equal(new[] { "DynamicLighting:True", "DynamicLighting:False" }, P.VendorService.Calls.ToArray());
    }

    [Fact]
    public void DynamicLightingInitSkipsWhenDisabledOrSupportedAlready()
    {
        DynamicLightingHelper.Init();
        Assert.Empty(P.VendorService.Calls);

        AppConfig.Set("lighting_init", 1);
        P.VendorService.DynamicEnabled = true;
        DynamicLightingHelper.Init();
        Assert.Empty(P.VendorService.Calls);

        P.VendorService.DynamicEnabled = false;
        P.VendorService.DynamicSupported = false;
        DynamicLightingHelper.Init();
        Assert.Empty(P.VendorService.Calls);
        Assert.False(DynamicLightingHelper.IsEnabled());
    }

    [Fact]
    public void DynamicLightingEffectGoesToOptionalService()
    {
        DynamicLightingHelper.SetEffect(DynamicLightingHelper.DynamicLightingEffect.Wave, Color.Red, null, 80, 5);

        var effect = P.VendorService.LastEffect;
        Assert.NotNull(effect);
        Assert.Equal(4, effect!.Value.effect);
        Assert.Equal(Color.Red, effect.Value.c1);
        Assert.Equal(80, effect.Value.brightness);
        Assert.Equal(5, effect.Value.speed);
    }

    [Fact]
    public void KeystoneForwardsToVendorServices()
    {
        Keystone.SetEnabled(true);
        Assert.Contains("Keystone:True", P.VendorService.Calls);
        Assert.False(Keystone.IsEnabled());
    }

    [Fact]
    public void ClamshellKeepsLidAwakeWhenExternalDisplayAndCharger()
    {
        AppConfig.Set("toggle_clamshell_mode", 1);
        P.DisplayService.External = true;
        P.PowerService.Source = Platform.PowerSource.AC;
        var control = new ClamshellModeControl();

        Assert.True(control.IsClamshellReady());
        control.ToggleLidAction();

        Assert.Equal(0, P.PowerService.LidAction);
        Assert.Contains("SetLidAction:0,True", P.PowerService.Calls);
    }

    [Fact]
    public void ClamshellRestoresDefaultLidActionWhenNotReady()
    {
        AppConfig.Set("toggle_clamshell_mode", 1);
        P.PowerService.LidAction = 0;
        P.DisplayService.External = true;
        P.PowerService.Source = Platform.PowerSource.Battery;   // external display but no charger and no battery override
        var control = new ClamshellModeControl();

        Assert.False(control.IsClamshellReady());
        control.ToggleLidAction();

        Assert.Equal(1, P.PowerService.LidAction);
    }

    [Fact]
    public void ClamshellOnBatteryWhenAllowed()
    {
        AppConfig.Set("toggle_clamshell_mode", 1);
        AppConfig.Set("clamshell_battery", 1);
        P.DisplayService.External = true;

        Assert.True(new ClamshellModeControl().IsClamshellReady());
    }

    [Fact]
    public void ClamshellDoesNothingWhenDisabled()
    {
        new ClamshellModeControl().ToggleLidAction();
        Assert.DoesNotContain(P.PowerService.Calls, c => c.StartsWith("SetLidAction"));
    }

    [Fact]
    public void ClamshellRemembersHibernateAsDefault()
    {
        P.PowerService.LidAction = 2;

        _ = new ClamshellModeControl();

        Assert.Equal(2, AppConfig.Get("clamshell_default_lid_action"));
    }

    [Fact]
    public void ClamshellSubscribesToDisplayChanges()
    {
        var control = new ClamshellModeControl();
        control.RegisterDisplayEvents();
        control.RegisterDisplayEvents();   // idempotent
        P.DisplayService.RaiseSettingsChanged();   // re-inits the screen (stub), must not throw
        control.UnregisterDisplayEvents();
        P.DisplayService.RaiseSettingsChanged();
    }
}

public class ColorAndMiscHelperTests : EngineTestBase
{
    public ColorAndMiscHelperTests(EngineFixture fixture) : base(fixture) { }

    [Fact]
    public void WeightedAverageInterpolatesAndClamps()
    {
        var half = ColorUtils.GetWeightedAverage(Color.FromArgb(0, 0, 0), Color.FromArgb(100, 200, 255), 0.5f);
        Assert.Equal((50, 100, 128), (half.R, half.G, half.B));

        // weights outside 0..1 overshoot, channels are clamped to a byte
        var over = ColorUtils.GetWeightedAverage(Color.Black, Color.White, 2f);
        Assert.Equal((255, 255, 255), (over.R, over.G, over.B));
    }

    [Fact]
    public void MidColorIsChannelAverage()
    {
        var mid = ColorUtils.GetMidColor(Color.FromArgb(10, 20, 30), Color.FromArgb(30, 40, 50));
        Assert.Equal((20, 30, 40), (mid.R, mid.G, mid.B));
    }

    [Fact]
    public void DominantColorAveragesPixels()
    {
        var pixels = new[] { Color.FromArgb(255, 0, 0), Color.FromArgb(0, 0, 255), Color.FromArgb(0, 0, 0), Color.FromArgb(0, 0, 0) };

        var viaSpan = ColorUtils.GetDominantColor(pixels);
        var viaFunc = ColorUtils.GetDominantColor(2, 2, (x, y) => pixels[y * 2 + x]);

        Assert.Equal((63, 0, 63), (viaSpan.R, viaSpan.G, viaSpan.B));
        Assert.Equal(viaSpan, viaFunc);
        Assert.Equal(Color.Black, ColorUtils.GetDominantColor(0, 0, (x, y) => Color.White));
    }

    [Fact]
    public void HsvRoundTripKeepsColor()
    {
        var color = Color.FromArgb(200, 80, 40);

        var hsv = ColorUtils.HSV.ToHSV(color);
        var back = hsv.ToRGB();

        Assert.InRange(back.R, 199, 201);
        Assert.InRange(back.G, 79, 81);
        Assert.InRange(back.B, 39, 41);
    }

    [Fact]
    public void UpSaturationLeavesGreyUntouched()
    {
        Assert.Equal(Color.FromArgb(120, 120, 120), ColorUtils.HSV.UpSaturation(Color.FromArgb(120, 120, 120)));

        var saturated = ColorUtils.HSV.UpSaturation(Color.FromArgb(150, 100, 100));
        Assert.True(saturated.R - saturated.G > 50);
    }

    [Fact]
    public void TempFormatting()
    {
        Assert.Equal("22°C", TempHelper.FormatTemp(21.6));
        Assert.Equal(212, TempHelper.CelsiusToFahrenheit(100));
    }

    [Fact]
    public void MemoryTrimRunsPlatformTrim()
    {
        MemoryHelper.TrimAfter();

        Assert.True(WaitFor(() => P.ProcessService.Calls.Contains("Trim")));
    }

    [Fact]
    public void OptionalCapabilityLookup()
    {
        Assert.Same(P.VendorService, P.Get<IDynamicLightingEffects>());
        Assert.Same(P.Tuning, P.Get<ICpuFamilyInfo>());
        Assert.Same(P, P.Get<IDriverInventory>());
        Assert.Same(P.SensorService, P.Get<IApuEnergySensors>());
        Assert.Null(P.Get<IFpsMonitor>());
        Assert.Same(P.VendorService, PlatformCapabilities.GetOptional<IAsusChargeLimitSync>());
    }
}
