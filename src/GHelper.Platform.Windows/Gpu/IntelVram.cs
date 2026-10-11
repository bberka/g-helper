using System.Runtime.Versioning;
using GHelper.Platform.Windows.Interop;
using Microsoft.Win32;

namespace GHelper.Platform.Windows.Gpu;

/// <summary>
/// Intel iGPU dedicated video memory size override ("DedicatedSegmentSize" under the adapter GMM key).
/// Lifted from legacy app/Gpu/IntelVram.cs.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class IntelVram
{
    private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string ValueName = "DedicatedSegmentSize";

    private string? key;

    public bool Supported => key is not null;

    public int[] Sizes { get; private set; } = [];

    /// <summary>0 = automatic, n = Sizes[n - 1].</summary>
    public int Index { get; private set; }

    /// <summary>Selectable sizes in GB: powers of two up to half of the installed memory (in 2 GB units, as legacy).</summary>
    public static int[] BuildSizes(long installedMemoryKb)
    {
        int max = installedMemoryKb > 0 ? (int)(installedMemoryKb >> 21) : 4;
        return [.. Enumerable.Range(0, 16).Select(i => 1 << i).TakeWhile(size => size <= max)];
    }

    public bool Init()
    {
        key = FindKey();
        if (key is null) return false;

        Sizes = BuildSizes(Win32.GetPhysicallyInstalledSystemMemory(out long memory) ? memory : 0);

        using var adapter = Registry.LocalMachine.OpenSubKey(key);
        Index = Array.IndexOf(Sizes, (adapter?.GetValue(ValueName) as int? ?? 0) / 1024) + 1;

        return true;
    }

    public void SetIndex(int index)
    {
        if (key is null) return;

        try
        {
            if (index < 1)
            {
                using var existing = Registry.LocalMachine.OpenSubKey(key, true);
                existing?.DeleteValue(ValueName, false);
                Index = 0;
                return;
            }

            using var adapter = Registry.LocalMachine.CreateSubKey(key);
            adapter?.SetValue(ValueName, Sizes[index - 1] * 1024, RegistryValueKind.DWord);
            Index = index;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Failed to set Intel VRAM size: {ex.Message}");
        }
    }

    private static string? FindKey()
    {
        using var display = Registry.LocalMachine.OpenSubKey(DisplayClass);
        if (display is null) return null;

        foreach (string name in display.GetSubKeyNames())
        {
            if (!int.TryParse(name, out _)) continue;
            using var adapter = display.OpenSubKey(name);
            if (adapter?.GetValue("DriverDesc") is string desc && desc.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                return $@"{DisplayClass}\{name}\GMM";
        }

        return null;
    }
}
