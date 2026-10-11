using GHelper.App.ViewModels;
using GHelper.Properties;

namespace GHelper.App.Services;

/// <summary>One tray menu row (port of the ToolStripMenuItem list built by SettingsForm.SetContextMenu).</summary>
public sealed record TrayMenuEntry(
    string Text,
    bool IsSeparator = false,
    bool? IsChecked = null,
    bool IsEnabled = true,
    Action? Invoke = null)
{
    public static TrayMenuEntry Separator { get; } = new("", IsSeparator: true);
}

/// <summary>Pure mapping view model -> tray menu, kept separate from Avalonia's NativeMenu so it can be tested.</summary>
public static class TrayMenuModel
{
    public static IReadOnlyList<TrayMenuEntry> Build(MainViewModel vm, IMainBackend backend)
    {
        var items = new List<TrayMenuEntry>
        {
            new(Strings.PerformanceMode, IsEnabled: false),
        };

        foreach (var mode in vm.TrayModes)
        {
            int id = mode.Key;
            items.Add(new TrayMenuEntry(mode.Text, IsChecked: id == vm.CurrentMode,
                Invoke: () => backend.SetValue(MainValue.PerformanceMode, id)));
        }

        items.Add(TrayMenuEntry.Separator);

        if (vm.IsGpuSection)
        {
            items.Add(new TrayMenuEntry(Strings.GPUMode, IsEnabled: false));

            if (vm.Eco.IsVisible)
                items.Add(new TrayMenuEntry(Strings.EcoMode, IsChecked: vm.Eco.Activated, Invoke: () => backend.Execute(MainAction.GpuEco)));

            items.Add(new TrayMenuEntry(Strings.StandardMode, IsChecked: vm.Standard.Activated, Invoke: () => backend.Execute(MainAction.GpuStandard)));

            if (vm.IsMuxGpu && vm.Ultimate.IsVisible)
                items.Add(new TrayMenuEntry(Strings.UltimateMode, IsChecked: vm.Ultimate.Activated, Invoke: () => backend.Execute(MainAction.GpuUltimate)));

            if (vm.Optimized.IsVisible)
                items.Add(new TrayMenuEntry(Strings.Optimized, IsChecked: vm.Optimized.Activated, Invoke: () => backend.Execute(MainAction.GpuOptimized)));

            items.Add(TrayMenuEntry.Separator);
        }

        items.Add(new TrayMenuEntry(Strings.BWTrayIcon, IsChecked: vm.BwTrayIcon,
            Invoke: () => backend.SetValue(MainValue.BwTrayIcon, vm.BwTrayIcon ? 0 : 1)));

        items.Add(TrayMenuEntry.Separator);

        items.Add(new TrayMenuEntry(Strings.Overlay, IsChecked: vm.OverlayEnabled,
            Invoke: () => backend.Execute(MainAction.OverlayToggleOnly)));
        items.Add(new TrayMenuEntry(Strings.OverlayOnlyInGames, IsChecked: vm.OverlayGameOnly, IsEnabled: vm.OverlayEnabled,
            Invoke: () => backend.SetValue(MainValue.OverlayGameOnly, vm.OverlayGameOnly ? 0 : 1)));

        items.Add(new TrayMenuEntry(Strings.Quit, Invoke: () => backend.Execute(MainAction.Quit)));
        return items;
    }

    /// <summary>
    /// Tray icon resource name for the GPU mode (legacy VisualiseIcon): colour icons per mode, or the black / white
    /// variants matching the taskbar theme when "bw_icon" is on.
    /// </summary>
    public static string IconName(int gpuMode, bool blackAndWhite, bool osDark)
    {
        string bw = osDark ? "light" : "dark";
        return gpuMode switch
        {
            AsusACPI.GPUModeEco => blackAndWhite ? bw + "-eco" : "eco",
            AsusACPI.GPUModeUltimate => blackAndWhite ? bw + "-standard" : "ultimate",
            _ => blackAndWhite ? bw + "-standard" : "standard",
        };
    }
}
