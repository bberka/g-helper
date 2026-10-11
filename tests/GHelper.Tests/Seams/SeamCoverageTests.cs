using System.Reflection;
using GHelper.Input;
using GHelper.Platform;
using GHelper.Platform.Linux;
using GHelper.Platform.Windows;
using GHelper.Platform.Windows.Fps;
using GHelper.Tests.Hw;
using GHelper.Tests.Linux;
using GHelper.USB;
using Xunit;

namespace GHelper.Tests.Seams;

/// <summary>
/// The platform contracts have two kinds of seams that compile without an implementation and then silently do nothing:
/// C# default interface members (IDisplayService.ElmbAccess, IGpuProvider.GetDiscretePowerState, ...) and the optional
/// capability interfaces Core finds through PlatformCapabilities (IInputExtras, IDriverInventory, ...).
/// <see cref="Table"/> lists every one of them, with whether the Linux platform has a meaningful implementation.
/// WindowsPlatform has to override all of them; LinuxPlatform exactly the ones marked as supported (a seam that is
/// implemented but marked N/A, or marked supported but missing, fails too, so the table and tasks/seams.md stay honest).
/// Adding a new default member / optional interface without a row fails <see cref="TableCoversEverySeamOfTheContracts"/>.
/// </summary>
[Collection("Hw platform")]
public class SeamCoverageTests
{
    public SeamCoverageTests()
    {
        // some platform constructors read AppConfig, whose static initialiser needs a registered platform
        HwTestPlatform.Ensure();
    }

    /// <summary>One seam. <see cref="Member"/> null = optional interface, otherwise a default member of <see cref="Contract"/>.</summary>
    private sealed record Seam(Type Contract, string? Member, bool Linux, string Note)
    {
        public string Id => Member is null ? Contract.Name : Contract.Name + "." + Member;
        public override string ToString() => Id;
    }

    // Contract = the interface; Linux = LinuxPlatform implements it; Note = why not (N/A) or how.
    private static readonly Seam[] Table =
    [
        // --- default interface members ---------------------------------------------------------------------
        new(typeof(IDisplayService), nameof(IDisplayService.SplendidDirectory), false, "ASUS Splendid command line tool: Windows ASUS software"),
        new(typeof(IDisplayService), nameof(IDisplayService.GetGameVisualStatus), false, "Armoury Crate GameVisual registry flag: Windows only"),
        new(typeof(IDisplayService), nameof(IDisplayService.SetGameVisualStatus), false, "Armoury Crate GameVisual registry flag: Windows only"),
        new(typeof(IDisplayService), nameof(IDisplayService.ElmbAccess), false, "DPCD AUX access through Intel ControlLib / NVAPI: no portable Linux equivalent"),
        new(typeof(IDisplayService), nameof(IDisplayService.SetAsusRefreshFlag), false, "ASUS Armoury uninstall registry key: Windows only"),

        new(typeof(ICpuTuning), nameof(ICpuTuning.CreatePawnIo), false, "ryzen_smu exposes finished SMU commands, not the PawnIO module protocol: LinuxCpuTuning is a direct ICpuTuning"),
        new(typeof(ICpuTuning), nameof(ICpuTuning.IsDriverInstalled), false, "default (= IsSupported = ryzen_smu loaded / RAPL readable) is the Linux answer"),

        new(typeof(IGpuProvider), nameof(IGpuProvider.IntelVramSizes), false, "Intel shared VRAM is a Windows driver registry value"),
        new(typeof(IGpuProvider), nameof(IGpuProvider.IntelVramIndex), false, "Intel shared VRAM is a Windows driver registry value"),
        new(typeof(IGpuProvider), nameof(IGpuProvider.GetDiscretePowerState), true, "PCI runtime_status"),
        new(typeof(IGpuProvider), nameof(IGpuProvider.CreateAmdDriver), true, "amdgpu sysfs / hwmon"),

        new(typeof(IFpsMonitor), nameof(IFpsMonitor.TargetPid), false, "no FPS source on Linux (IFpsMonitorFactory.IsSupported = false)"),
        new(typeof(IFpsMonitorFactory), nameof(IFpsMonitorFactory.GetForegroundProcessId), false, "only used to pin the FPS counter, which Linux does not have"),
        new(typeof(IFpsMonitorFactory), nameof(IFpsMonitorFactory.ForegroundChanged), false, "only used to pin the FPS counter, which Linux does not have"),

        // --- optional capability interfaces ------------------------------------------------------------------
        new(typeof(IDynamicLightingEffects), null, false, "Windows 11 Dynamic Lighting registry"),
        new(typeof(IDriverInventory), null, false, "ASUS driver update list of Windows drivers; without it every update stays 'not found'"),
        new(typeof(ICpuFamilyInfo), null, true, "CPUID family / model of the ryzen_smu command table"),
        new(typeof(IApuEnergySensors), null, false, "Windows 'Energy Meter' counters; Core falls back to amdgpu hwmon power"),
        new(typeof(IAsusChargeLimitSync), null, false, "ASUS System Control Interface registry: Windows only"),
        new(typeof(IPowerModeCatalog), null, true, "power-profiles-daemon profile names"),
        new(typeof(IDisplaySettingsEvents), null, true, "DRM connector polling"),
        new(typeof(ISessionEndingEvents), null, true, "logind PrepareForShutdown"),
        new(typeof(IGpuOverclock), null, false, "NVAPI clock offsets / lock; on Linux Core's NvidiaGpuControl uses NVML directly"),

        new(typeof(IInputExtras), null, true, "idle monitor / evdev, modifiers, screen off, touchpad state"),
        new(typeof(INumberPadDriver), null, false, "\\\\.\\AsusTPPC IOCTL: Windows driver"),
        new(typeof(IScreenCapture), null, true, "X11 root window grab (not on Wayland)"),
    ];

    /// <summary>Interfaces of the namespace that are the base contracts, not optional capabilities.</summary>
    private static readonly HashSet<Type> BaseContracts =
    [
        typeof(IPlatform), typeof(ISystemInfo), typeof(IAsusBackend), typeof(IPowerService), typeof(IDisplayService),
        typeof(IInputService), typeof(IStartupService), typeof(IAudioService), typeof(IProcessService),
        typeof(IVendorServices), typeof(ICpuTuning), typeof(IGpuProvider), typeof(ISystemSensors),
        typeof(IFpsMonitor), typeof(IFpsMonitorFactory),
        // implemented by the app head (Avalonia), not by a platform
        typeof(IUiHost),
    ];

    // ---------------------------------------------------------------------------------------------- discovery

    private static string MemberName(MethodInfo method)
    {
        string name = method.Name;
        foreach (string prefix in new[] { "get_", "set_", "add_", "remove_" })
            if (method.IsSpecialName && name.StartsWith(prefix, StringComparison.Ordinal)) return name[prefix.Length..];
        return name;
    }

    private static IEnumerable<Type> PlatformInterfaces() =>
        typeof(IPlatform).Assembly.GetTypes().Where(t => t.IsInterface && t.Namespace == "GHelper.Platform");

    private static HashSet<string> DiscoveredDefaultMembers() =>
        PlatformInterfaces()
            .Where(t => t != typeof(IUiHost))
            .SelectMany(t => t.GetMethods().Where(m => !m.IsAbstract && !m.IsStatic).Select(m => t.Name + "." + MemberName(m)))
            .ToHashSet();

    private static HashSet<string> DiscoveredOptionalInterfaces() =>
        PlatformInterfaces().Where(t => !BaseContracts.Contains(t)).Select(t => t.Name).ToHashSet();

    // ---------------------------------------------------------------------------------------------- checks

    /// <summary>The concrete type that has to provide <paramref name="seam"/> on <paramref name="platform"/>, null when there is none.</summary>
    private static Type? ImplementingType(IPlatform platform, Seam seam)
    {
        if (seam.Contract == typeof(IFpsMonitor))
        {
            return platform.Kind == PlatformKind.Windows
                ? typeof(WindowsFpsMonitorFactory).Assembly.GetType("GHelper.Platform.Windows.Fps.EtwFpsMonitor")
                : null; // Linux has no FPS monitor to create
        }

        var property = typeof(IPlatform).GetProperties().Single(p => p.PropertyType == seam.Contract);
        return property.GetValue(platform)?.GetType();
    }

    private static bool Overrides(Type implementation, Type contract, string member)
    {
        InterfaceMapping map = implementation.GetInterfaceMap(contract);
        for (int i = 0; i < map.InterfaceMethods.Length; i++)
        {
            if (MemberName(map.InterfaceMethods[i]) != member) continue;

            MethodInfo? target = map.TargetMethods[i];
            if (target is not null && target.DeclaringType != contract) return true;
        }
        return false;
    }

    private static bool IsProvided(IPlatform platform, Seam seam)
    {
        if (seam.Member is null)
        {
            object? provider = typeof(PlatformCapabilities)
                .GetMethod(nameof(PlatformCapabilities.Get))!
                .MakeGenericMethod(seam.Contract)
                .Invoke(null, [platform]);
            return provider is not null;
        }

        Type? implementation = ImplementingType(platform, seam);
        return implementation is not null && Overrides(implementation, seam.Contract, seam.Member);
    }

    [Fact]
    public void TableCoversEverySeamOfTheContracts()
    {
        var tableDefaults = Table.Where(s => s.Member is not null).Select(s => s.Id).ToHashSet();
        var tableOptional = Table.Where(s => s.Member is null).Select(s => s.Id).ToHashSet();

        // optional interfaces that live next to the code that consumes them
        var discoveredOptional = DiscoveredOptionalInterfaces();
        discoveredOptional.UnionWith([nameof(IInputExtras), nameof(INumberPadDriver), nameof(IScreenCapture)]);

        Assert.Empty(DiscoveredDefaultMembers().Except(tableDefaults));   // default member without a row
        Assert.Empty(tableDefaults.Except(DiscoveredDefaultMembers()));   // row of a member that no longer exists
        Assert.Empty(discoveredOptional.Except(tableOptional));
        Assert.Empty(tableOptional.Except(discoveredOptional));
    }

    [Fact]
    public void WindowsPlatformImplementsEverySeam()
    {
        using var platform = new WindowsPlatform();

        var missing = Table.Where(s => !IsProvided(platform, s)).Select(s => s.Id).ToList();

        Assert.True(missing.Count == 0, "WindowsPlatform does not override: " + string.Join(", ", missing));
    }

    [Fact]
    public void LinuxPlatformImplementsExactlyTheSeamsMarkedSupported()
    {
        using var fs = new FakeSysfs();
        using var platform = new LinuxPlatform(new LinuxContext(fs.Root, new FakeRunner(),
            getEnv: n => n == "HOME" ? "/home/test" : null, getEuid: () => 1000, processPath: "/usr/bin/ghelper"));

        var missing = Table.Where(s => s.Linux && !IsProvided(platform, s)).Select(s => s.Id).ToList();
        var unexpected = Table.Where(s => !s.Linux && IsProvided(platform, s)).Select(s => s.Id).ToList();

        Assert.True(missing.Count == 0, "LinuxPlatform should implement: " + string.Join(", ", missing));
        Assert.True(unexpected.Count == 0, "LinuxPlatform implements seams marked N/A (update the table and tasks/seams.md): " + string.Join(", ", unexpected));
    }

    [Fact]
    public void EveryNotApplicableSeamHasAReason()
    {
        Assert.All(Table, s => Assert.False(string.IsNullOrWhiteSpace(s.Note), s.Id));
    }
}
