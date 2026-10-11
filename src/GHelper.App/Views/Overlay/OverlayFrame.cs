using GHelper.Overlay;
using DColor = System.Drawing.Color;

namespace GHelper.App.Views.Overlay;

/// <summary>
/// Immutable snapshot of everything one overlay frame shows: texts as <see cref="OverlayModel"/> formatted them, the
/// resolved <see cref="OverlayLayout"/> and the power history ordered oldest first. The renderer is a pure function of it,
/// which is what the headless tests and the design data feed.
/// </summary>
public sealed class OverlayFrame
{
    public float Scale { get; init; } = OverlayLayout.BaseScale;
    public OverlayBlocks Blocks { get; init; }

    /// <summary>The battery column is part of the layout (block enabled and the device runs on battery).</summary>
    public bool BatteryVisible { get; init; }

    public int BackgroundAlpha { get; init; } = OverlaySettings.DefaultAlpha;
    public DColor GpuColor { get; init; } = OverlaySettings.DefaultGpuColor;
    public DColor CpuColor { get; init; } = OverlaySettings.DefaultCpuColor;

    /// <summary>The window is in drag mode: the background is kept grabbable (legacy DragMinAlpha).</summary>
    public bool DragMode { get; init; }

    public int Fps { get; init; }

    /// <summary>"GPU: 62" with the trailing separator space (as <see cref="OverlayModel.GpuTemp"/>).</summary>
    public string GpuTemp { get; init; } = "";
    public string CpuTemp { get; init; } = "";
    public string GpuFan { get; init; } = "";
    public string CpuFan { get; init; } = "";
    public string GpuPower { get; init; } = "";
    public string CpuPower { get; init; } = "";
    public int? GpuUsage { get; init; }
    public int? CpuUsage { get; init; }
    public int? VramUsage { get; init; }
    public int? RamUsage { get; init; }
    public int? VramUsedMb { get; init; }
    public int? RamUsedMb { get; init; }
    public string GpuName { get; init; } = "";
    public string CpuName { get; init; } = "";
    public string BatteryLevel { get; init; } = "";
    public string BatteryRate { get; init; } = "";
    public int BatteryPercent { get; init; }
    public bool BatteryCharging { get; init; }

    /// <summary>Power history (watts), <see cref="OverlayModel.HistoryLength"/> entries, oldest first.</summary>
    public float[] CpuHistory { get; init; } = new float[OverlayModel.HistoryLength];
    public float[] GpuHistory { get; init; } = new float[OverlayModel.HistoryLength];

    private OverlayLayoutResult? _layout;

    public OverlayLayoutResult Layout => _layout ??= OverlayLayout.Compute(Scale, Blocks, BatteryVisible);

    public static OverlayFrame From(OverlayModel m, bool dragMode = false) => new()
    {
        Scale = m.Scale,
        Blocks = m.Blocks,
        BatteryVisible = m.ShowBattery && m.OnBattery,
        BackgroundAlpha = m.BackgroundAlpha,
        GpuColor = m.GpuColor,
        CpuColor = m.CpuColor,
        DragMode = dragMode,
        Fps = m.CurrentFps,
        GpuTemp = m.GpuTemp,
        CpuTemp = m.CpuTemp,
        GpuFan = m.GpuFan,
        CpuFan = m.CpuFan,
        GpuPower = m.GpuPower,
        CpuPower = m.CpuPower,
        GpuUsage = m.GpuUsage,
        CpuUsage = m.CpuUsage,
        VramUsage = m.VramUsage,
        RamUsage = m.RamUsage,
        VramUsedMb = m.VramUsedMb,
        RamUsedMb = m.RamUsedMb,
        GpuName = m.GpuShortName,
        CpuName = m.CpuShortName,
        BatteryLevel = m.BatteryLevel,
        BatteryRate = m.BatteryRate,
        BatteryPercent = m.BatteryPercent,
        BatteryCharging = m.BatteryCharging,
        CpuHistory = Ordered(m.CpuHistory, m.HistoryHead),
        GpuHistory = Ordered(m.GpuHistory, m.HistoryHead),
    };

    /// <summary>Ring buffer to a plain array, oldest sample (the slot the next write will overwrite) first.</summary>
    public static float[] Ordered(IReadOnlyList<float> ring, int head)
    {
        int n = ring.Count;
        var result = new float[n];
        for (int i = 0; i < n; i++) result[i] = ring[(head + i) % n];
        return result;
    }

    /// <summary>Plausible values for a preset (design data, tests).</summary>
    public static OverlayFrame Sample(OverlayMode mode, bool names = false, bool battery = false, float scale = OverlayLayout.BaseScale)
    {
        int m = (int)mode;
        bool Show(string key) => OverlaySettings.BlockDefault(key, mode);

        var cpu = new float[OverlayModel.HistoryLength];
        var gpu = new float[OverlayModel.HistoryLength];
        for (int i = 0; i < cpu.Length; i++)
        {
            cpu[i] = 18f + 14f * (float)Math.Sin(i / 6.0) + (i % 7);
            gpu[i] = i < 12 ? 0f : 40f + 35f * (float)Math.Abs(Math.Sin(i / 9.0));
        }

        return new OverlayFrame
        {
            Scale = scale,
            Blocks = new OverlayBlocks(
                Fps: Show("overlay_show_fps"), Temp: Show("overlay_show_temp"), Fans: Show("overlay_show_fans"),
                Chart: Show("overlay_show_chart"), Power: Show("overlay_show_power"), Usage: Show("overlay_show_usage"),
                Ram: Show("overlay_show_ram"), Names: names, Battery: battery),
            BatteryVisible = battery,
            Fps = 144,
            GpuTemp = "GPU:" + OverlayFormat.FmtTemp(71) + " ",
            CpuTemp = "CPU:" + OverlayFormat.FmtTemp(83) + " ",
            GpuFan = "4300",
            CpuFan = "3900",
            GpuPower = OverlayFormat.FmtGpuPower(98.4f),
            CpuPower = OverlayFormat.FmtPow(45.2),
            GpuUsage = 87,
            CpuUsage = 42,
            VramUsage = 61,
            RamUsage = 38,
            VramUsedMb = 7500,
            RamUsedMb = 12700,
            GpuName = "RTX 4070",
            CpuName = "i9-13980HX",
            BatteryLevel = "64%",
            BatteryRate = "-22.4",
            BatteryPercent = 64,
            BatteryCharging = false,
            CpuHistory = cpu,
            GpuHistory = gpu,
        };
    }
}
