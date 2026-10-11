namespace GHelper.Platform.Windows.Power;

/// <summary>
/// Windows power overlay / power plan identifiers and the pure decision logic of legacy PowerNative.
/// Overlays are the Windows 10/11 "power mode" slider (Best power efficiency / Balanced / Best performance),
/// plans are the classic schemes (Balanced, High performance, ...).
/// </summary>
public static class PowerSchemes
{
    public const string Silent = "961cc777-2547-4f9d-8174-7d86181b8a7a";
    public const string Balanced = "00000000-0000-0000-0000-000000000000";
    public const string Turbo = "ded574b5-45a0-4f42-8737-46345c09c238";

    public const string PlanBalanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string PlanHighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    private static readonly string[] overlays = [Balanced, Turbo, Silent];

    /// <summary>Human readable names (legacy PowerNative.powerModes).</summary>
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        { Silent, "Best Power Efficiency" },
        { Balanced, "Balanced" },
        { Turbo, "Best Performance" },
        { PlanHighPerformance, "High Performance Plan" },
    };

    public static bool IsOverlay(string? scheme) =>
        scheme is not null && overlays.Contains(scheme, StringComparer.OrdinalIgnoreCase);

    /// <summary>AsusMode (0 balanced, 1 turbo, 2 silent, 3 full speed) to overlay / plan guid.</summary>
    public static string GetDefaultPowerMode(int mode) => mode switch
    {
        1 => Turbo,
        2 => Silent,
        3 => PlanHighPerformance,
        _ => Balanced,
    };

    /// <summary>
    /// Power plan that has to be active for the requested scheme. High performance is a plan itself; everything else
    /// (overlays) rides on the configured plan, a USB-C specific plan wins when the charger is USB-C, balanced when unset.
    /// </summary>
    public static string ResolvePlan(string requestedScheme, string? configuredPlan, string? usbcPlan, bool usbcSource)
    {
        if (string.Equals(requestedScheme, PlanHighPerformance, StringComparison.OrdinalIgnoreCase)) return PlanHighPerformance;

        string? plan = configuredPlan;
        if (usbcSource && usbcPlan is not null) plan = usbcPlan;
        return string.IsNullOrEmpty(plan) ? PlanBalanced : plan;
    }
}
