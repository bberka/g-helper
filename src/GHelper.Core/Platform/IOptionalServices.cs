using System.Drawing;

namespace GHelper.Platform;

// Optional capability interfaces (added by W1-A core-engine).
//
// The base contracts (IPlatform, IVendorServices, ...) are frozen. Legacy behaviour that has no home in them is exposed as
// small *optional* interfaces instead. A platform implements one by simply adding it to the class that already implements
// the related service (or to the IPlatform class itself). Core discovers them with PlatformServices.Current.Get<T>() and
// degrades gracefully (feature off / null) when the platform does not provide them.

/// <summary>Windows Dynamic Lighting effect registry writes (legacy DynamicLightingHelper.SetEffect).</summary>
public interface IDynamicLightingEffects
{
    /// <param name="effect">Value of GHelper.Helpers.DynamicLightingHelper.DynamicLightingEffect.</param>
    void SetEffect(int effect, Color? color, Color? color2, int? brightness, int? speed);
}

/// <summary>One installed driver / extension / firmware entry (legacy UpdatesController.LocalDriver).</summary>
public readonly record struct InstalledDriver(string MatchId, string Version, bool IsExtension, string Entry);

/// <summary>
/// OS driver inventory used by the ASUS driver update list (legacy UpdatesController: CfgMgr32 device nodes, HKLM\SOFTWARE\ASUS,
/// staged oem*.inf, MS Store packages). Only Windows implements it; without it every update stays "not found".
/// </summary>
public interface IDriverInventory
{
    /// <summary>Present device driver versions + extension INF entries + HKLM\SOFTWARE\ASUS DisplayVersion entries.</summary>
    List<InstalledDriver> BuildInventory();

    /// <summary>ROG MCU firmware version (last dotted component) or null.</summary>
    string? GetMcuFirmwareVersion();

    /// <summary>UEFI firmware resource version for an update title (last dotted component) or null.</summary>
    string? GetFirmwareVersion(string title);

    /// <summary>Hardware id -> highest staged (driver store) INF version for the given hardware ids.</summary>
    Dictionary<string, string> BuildStagedVersions(HashSet<string> hardwareIds, CancellationToken token);

    /// <summary>Installed MS Store package family names.</summary>
    HashSet<string> GetInstalledStorePackages();
}

/// <summary>CPU family / code name detected by the platform's SMU driver (names of the legacy PawnIO.CpuFamily / CpuCodeName enums).</summary>
public interface ICpuFamilyInfo
{
    /// <summary>"Renoir", "Mobile", "Raphael", "StrixPoint", ... or null when unknown / driver not available.</summary>
    string? CpuFamily { get; }
    string? CpuCodeName { get; }
}

/// <summary>
/// Raw AMD APU energy-meter channels (legacy HardwareControl EnergyChannel / "Energy Meter" performance counters).
/// All values are watts; null when the channel is unavailable.
/// </summary>
public interface IApuEnergySensors
{
    float? SampleApuPower();
    float? SampleCpuPower();
    float? SampleCoresPower();
}

/// <summary>ASUS System Control Interface "ChargingRate" mirror (legacy BatteryControl.SetAsusChargeLimit, HKLM, needs elevation).</summary>
public interface IAsusChargeLimitSync
{
    void SetChargingRate(int value);
}

/// <summary>
/// The OS power modes selectable per performance mode (legacy PowerNative.powerModes: overlay guid -> display name).
/// Without it Core falls back to the Windows list.
/// </summary>
public interface IPowerModeCatalog
{
    IReadOnlyDictionary<string, string> PowerModes { get; }
}

/// <summary>Monitor configuration changed: external display plugged / unplugged, resolution change (legacy SystemEvents.DisplaySettingsChanged).</summary>
public interface IDisplaySettingsEvents
{
    event Action? DisplaySettingsChanged;
}

/// <summary>User session / system is ending (legacy SystemEvents.SessionEnding, Linux: logind PrepareForShutdown).</summary>
public interface ISessionEndingEvents
{
    event Action? SessionEnding;
}

public static class PlatformCapabilities
{
    /// <summary>Returns the first registered platform object / service implementing <typeparamref name="T"/>, or null.</summary>
    public static T? Get<T>(this IPlatform platform) where T : class
    {
        if (platform is T self) return self;

        object?[] services =
        {
            platform.System, platform.Asus, platform.Power, platform.Display, platform.Input, platform.Startup,
            platform.Audio, platform.Process, platform.Vendor, platform.CpuTuning, platform.Gpu, platform.Sensors, platform.Fps,
        };

        foreach (var service in services)
            if (service is T match) return match;

        return null;
    }

    /// <summary>Same as <see cref="Get{T}"/> on the registered platform; null when no platform is registered.</summary>
    public static T? GetOptional<T>() where T : class
        => PlatformServices.IsRegistered ? PlatformServices.Current.Get<T>() : null;
}
