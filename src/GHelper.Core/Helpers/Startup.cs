using GHelper.Platform;

/// <summary>Legacy Startup (Task Scheduler) as a facade over <see cref="IStartupService"/> (Linux: autostart .desktop).</summary>
public class Startup
{
    private static IStartupService Svc => PlatformServices.Current.Startup;

    public static bool IsSupported => Svc.IsSupported;

    public static bool IsScheduled() => Svc.IsScheduled();

    public static void ReScheduleAdmin() => Svc.ReScheduleAdmin();

    public static void StartupCheck() => Svc.StartupCheck();

    public static void UnscheduleCharge() => Svc.UnscheduleCharge();

    public static void ScheduleCharge() => Svc.ScheduleCharge();

    public static void Schedule() => Svc.Schedule();

    public static void UnSchedule() => Svc.UnSchedule();
}
