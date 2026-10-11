using System.Globalization;
using Avalonia;
using Avalonia.Media;
using GHelper.Overlay;
using DColor = System.Drawing.Color;

namespace GHelper.App.Views.Overlay;

/// <summary>
/// Draws one <see cref="OverlayFrame"/> (port of HardwareOverlay.PerformPaint). Coordinates are device pixels with the
/// origin at the window's top-left; the hosting <see cref="OverlayView"/> applies the pixel to DIP scale. All sizes come
/// from <see cref="OverlayLayout"/>, so this class only decides what goes where inside the computed columns.
/// </summary>
public static class OverlayRenderer
{
    /// <summary>Minimum background alpha while dragging, so a near transparent box stays grabbable.</summary>
    public const int DragMinAlpha = 110;

    // Consolas first (legacy); the fallbacks keep the column maths valid on Linux where it does not exist.
    private static readonly FontFamily MonoFamily =
        new("Consolas, Cascadia Mono, DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, Courier New, monospace");

    private static readonly Typeface Regular = new(MonoFamily, FontStyle.Normal, FontWeight.Normal);
    private static readonly Typeface Bold = new(MonoFamily, FontStyle.Normal, FontWeight.Bold);

    private static readonly Color BatteryColor = Color.FromArgb(255, 235, 235, 235);
    private static readonly Color BatteryDimColor = Color.FromArgb(128, 78, 78, 78);
    private static readonly Color BoltColor = Color.FromArgb(255, 25, 25, 25);

    private static readonly Point[] BoltShape =
    {
        new(0.6, 0), new(0.15, 0.55), new(0.45, 0.55), new(0.35, 1), new(0.85, 0.45), new(0.5, 0.45),
    };

    public static Color ToAvalonia(DColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private static FormattedText Text(string s, double size, IBrush brush, bool bold = false) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? Bold : Regular, size, brush);

    public static void Render(DrawingContext ctx, OverlayFrame f)
    {
        var L = f.Layout;
        float sc = f.Scale;
        var b = f.Blocks;
        bool showUsageBars = b.Usage || b.Ram;

        Color gpu = ToAvalonia(f.GpuColor), cpu = ToAvalonia(f.CpuColor);
        var gpuBrush = new SolidColorBrush(gpu);
        var cpuBrush = new SolidColorBrush(cpu);
        var gpuFill = new SolidColorBrush(ToAvalonia(OverlaySettings.ChartFill(f.GpuColor)));
        var cpuFill = new SolidColorBrush(ToAvalonia(OverlaySettings.ChartFill(f.CpuColor)));
        var batBrush = new SolidColorBrush(BatteryColor);

        int alpha = f.DragMode && f.BackgroundAlpha < DragMinAlpha ? DragMinAlpha : f.BackgroundAlpha;
        // always drawn, even fully transparent: the fill is also the hit area of the control
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(alpha, 0, 255), 0, 0, 0)), null,
            new Rect(0, 0, L.Width, L.Height), L.CornerRadius, L.CornerRadius);

        double fontSize = OverlayLayout.BaseFontSize * sc;
        double rpmSize = OverlayLayout.BaseRpmFontSize * sc;
        double charW = Text("XX", fontSize, Brushes.White).Width - Text("X", fontSize, Brushes.White).Width;

        int topY = L.PadY;
        int row2 = L.LineHeight + L.LineGap;

        // text lines are centred in their row (legacy relied on GDI+ DrawString metrics + a 1 sc nudge)
        double lineBox = Text("Xg", fontSize, Brushes.White).Height;
        double textY = topY + (L.LineHeight - lineBox) / 2 + sc * 0.5;

        // FPS, vertically centred over both rows
        if (b.Fps)
        {
            string fps = f.Fps > 0 ? f.Fps.ToString(CultureInfo.InvariantCulture) : "--";
            var t = Text(fps, L.InnerHeight / 1.15, gpuBrush, bold: true);
            ctx.DrawText(t, new Point(L.PadX + (L.FpsColWidth - t.Width) / 2, topY + (L.InnerHeight - t.Height) / 2));
        }

        if (b.Names)
        {
            var clip = new Rect(L.NameX, topY, L.NameColWidth, L.InnerHeight);
            using (ctx.PushClip(clip))
            {
                ctx.DrawText(Text(f.GpuName, fontSize, gpuBrush), new Point(L.NameX, textY));
                ctx.DrawText(Text(f.CpuName, fontSize, cpuBrush), new Point(L.NameX, textY + row2));
            }
        }

        // temperature + fan RPM (the RPM is hidden when the fan block is off)
        DrawTempFan(ctx, fontSize, rpmSize, charW, sc, L.LeftX, textY, b.Temp ? f.GpuTemp : "", b.Fans ? f.GpuFan : "", gpuBrush);
        DrawTempFan(ctx, fontSize, rpmSize, charW, sc, L.LeftX, textY + row2, b.Temp ? f.CpuTemp : "", b.Fans ? f.CpuFan : "", cpuBrush);

        if (b.Chart)
            DrawStackedChart(ctx, f, L.ChartX, topY, L.ChartWidth, L.InnerHeight, sc, gpu, cpu, gpuFill, cpuFill);

        if (b.Power)
        {
            DrawRight(ctx, f.GpuPower, fontSize, gpuBrush, L.PowerX + L.PowerColWidth, textY);
            DrawRight(ctx, f.CpuPower, fontSize, cpuBrush, L.PowerX + L.PowerColWidth, textY + row2);
        }

        if (showUsageBars)
        {
            var bar = OverlayLayout.ComputeBar(sc, L.LineHeight);

            if (b.Usage)
            {
                DrawUsageBar(ctx, L.BarX, topY + bar.BarYOffset, L.BarWidth, bar, f.GpuUsage ?? 0, gpu, gpuFill);
                DrawUsageBar(ctx, L.BarX, topY + row2 + bar.BarYOffset, L.BarWidth, bar, f.CpuUsage ?? 0, cpu, cpuFill);

                // empty when the value is unavailable, like the power column
                if (f.GpuUsage is { } gu) DrawRight(ctx, OverlayFormat.FormatUsage(gu), fontSize, gpuBrush, L.UsageNumX + L.UsageNumColWidth, textY);
                if (f.CpuUsage is { } cu) DrawRight(ctx, OverlayFormat.FormatUsage(cu), fontSize, cpuBrush, L.UsageNumX + L.UsageNumColWidth, textY + row2);
            }

            // VRAM (GPU row) / RAM (CPU row): complete preset
            if (b.Ram)
            {
                DrawRight(ctx, OverlayFormat.FormatMemGb(f.VramUsedMb), fontSize, gpuBrush, L.MemNumX + L.MemNumColWidth, textY);
                DrawRight(ctx, OverlayFormat.FormatMemGb(f.RamUsedMb), fontSize, cpuBrush, L.MemNumX + L.MemNumColWidth, textY + row2);

                DrawUsageBar(ctx, L.MemBarX, topY + bar.BarYOffset, L.BarWidth, bar, f.VramUsage ?? 0, gpu, gpuFill);
                DrawUsageBar(ctx, L.MemBarX, topY + row2 + bar.BarYOffset, L.BarWidth, bar, f.RamUsage ?? 0, cpu, cpuFill);
            }
        }

        if (f.BatteryVisible)
        {
            int colRight = L.BatteryX + L.BatteryColWidth;
            DrawRight(ctx, f.BatteryLevel, fontSize, batBrush, colRight, textY);
            if (f.BatteryRate.Length > 0)
            {
                DrawRight(ctx, f.BatteryRate, fontSize, batBrush, colRight, textY + row2);
                var w = Text("W", fontSize, batBrush);
                ctx.DrawText(w, new Point(L.BatteryIconX + (L.BatteryIconWidth - w.Width) / 2, textY + row2));
            }

            int iconH = L.BatteryIconHeight;
            DrawBatteryIcon(ctx, L.BatteryIconX, topY + (L.LineHeight - iconH) / 2 - OverlayLayout.S(sc, OverlayLayout.BaseUsageBarYNudge),
                L.BatteryIconWidth, iconH, sc, f.BatteryPercent, f.BatteryCharging);
        }
    }

    private static void DrawRight(DrawingContext ctx, string s, double size, IBrush brush, double right, double y)
    {
        if (s.Length == 0) return;
        var t = Text(s, size, brush);
        ctx.DrawText(t, new Point(right - t.Width, y));
    }

    // tempStr already ends with a space (natural separator from the fan number); 2 sc px gap before "RPM"
    private static void DrawTempFan(DrawingContext ctx, double size, double rpmSize, double charW, float sc,
        double x, double y, string tempStr, string fanNum, IBrush brush)
    {
        if (tempStr.Length > 0) ctx.DrawText(Text(tempStr, size, brush), new Point(x, y));
        if (fanNum.Length == 0) return;

        double numX = x + charW * tempStr.Length;
        ctx.DrawText(Text(fanNum, size, brush), new Point(numX, y));

        double rpmX = numX + charW * fanNum.Length + 2.0 * sc;
        var rpm = Text("RPM", rpmSize, brush);
        // small label sits on the baseline of the digits
        var digits = Text(fanNum, size, brush);
        ctx.DrawText(rpm, new Point(rpmX, y + digits.Baseline - rpm.Baseline));
    }

    private static void DrawUsageBar(DrawingContext ctx, int x, int y, int w, OverlayBarGeometry bar, int usage, Color lit, IBrush dim)
    {
        int cells = bar.NumCells;
        int litCells = OverlayLayout.LitCells(usage, cells);
        int pitch = bar.CellHeight + bar.SeparatorHeight;
        var litBrush = new SolidColorBrush(lit);

        for (int i = 0; i < cells; i++)
        {
            bool isLit = i >= cells - litCells; // filled from the bottom
            ctx.DrawRectangle(isLit ? litBrush : dim, null, new Rect(x, y + i * pitch, w, bar.CellHeight));
        }
    }

    private static void DrawBatteryIcon(DrawingContext ctx, int x, int y, int w, int h, float sc, int level, bool charging)
    {
        var fill = new SolidColorBrush(BatteryColor);
        var dim = new SolidColorBrush(BatteryDimColor);

        int nubW = Math.Max(2, OverlayLayout.S(sc, OverlayLayout.BaseBatNubWidth));
        int nubH = Math.Max(1, OverlayLayout.S(sc, OverlayLayout.BaseBatNubHeight));
        int border = Math.Max(1, (int)sc);
        int bodyY = y + nubH;
        int bodyH = h - nubH;

        void Fill(IBrush brush, double rx, double ry, double rw, double rh) =>
            ctx.DrawRectangle(brush, null, new Rect(rx, ry, rw, rh));

        Fill(fill, x + (w - nubW) / 2, y, nubW, nubH);
        Fill(fill, x, bodyY, w, border);
        Fill(fill, x, bodyY + bodyH - border, w, border);
        Fill(fill, x, bodyY, border, bodyH);
        Fill(fill, x + w - border, bodyY, border, bodyH);

        int innerX = x + border, innerW = w - 2 * border;
        int innerY = bodyY + border, innerH = bodyH - 2 * border;
        int fillH = (int)Math.Round(innerH * Math.Clamp(level, 0, 100) / 100.0);
        if (fillH > 0) Fill(fill, innerX, innerY + innerH - fillH, innerW, fillH);
        if (fillH < innerH) Fill(dim, innerX, innerY, innerW, innerH - fillH);

        if (charging)
        {
            double bw = w * 0.9, bh = bodyH * 0.64;
            double bx = x + (w - bw) / 2, by = bodyY + (bodyH - bh) / 2;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                for (int i = 0; i < BoltShape.Length; i++)
                {
                    var p = new Point(bx + BoltShape[i].X * bw, by + BoltShape[i].Y * bh);
                    if (i == 0) g.BeginFigure(p, true); else g.LineTo(p);
                }
                g.EndFigure(true);
            }
            ctx.DrawGeometry(new SolidColorBrush(BoltColor), null, geometry);
        }
    }

    /// <summary>The three polylines of the stacked chart (oldest left), same maths as <see cref="OverlayModel.BuildChart"/>.</summary>
    public static void BuildChart(IReadOnlyList<float> cpuHistory, IReadOnlyList<float> gpuHistory, double x, double y, double w, double h,
        Point[] basePts, Point[] cpuPts, Point[] gpuPts)
    {
        int n = Math.Min(cpuHistory.Count, gpuHistory.Count);
        float peak = OverlayModel.ChartPeak(cpuHistory, gpuHistory);
        double stepX = w / (n - 1);

        for (int i = 0; i < n; i++)
        {
            double px = x + i * stepX;
            double cpuH = cpuHistory[i] / peak * h;
            double gpuH = gpuHistory[i] / peak * h;
            basePts[i] = new Point(px, y + h);
            cpuPts[i] = new Point(px, y + h - cpuH);
            gpuPts[i] = new Point(px, y + h - cpuH - gpuH);
        }
    }

    private static void DrawStackedChart(DrawingContext ctx, OverlayFrame f, double x, double y, double w, double h, float sc,
        Color gpu, Color cpu, IBrush gpuFill, IBrush cpuFill)
    {
        int n = Math.Min(f.CpuHistory.Length, f.GpuHistory.Length);
        if (n < 2) return;

        var basePts = new Point[n];
        var cpuPts = new Point[n];
        var gpuPts = new Point[n];
        BuildChart(f.CpuHistory, f.GpuHistory, x, y, w, h, basePts, cpuPts, gpuPts);

        using (ctx.PushClip(new Rect(x, y, w, h)))
        {
            ctx.DrawGeometry(cpuFill, null, Area(cpuPts, basePts));
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(cpu), 1.5), Line(cpuPts));
            ctx.DrawGeometry(gpuFill, null, Area(gpuPts, cpuPts));
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(gpu), 1.5), Line(gpuPts));

            // dotted outline of the total
            var dotted = new Pen(new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)), sc * 0.75, new DashStyle(new double[] { 1, 1 }, 0));
            ctx.DrawGeometry(null, dotted, Line(gpuPts));
        }

        var axis = new Pen(new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)), sc * 0.5);
        ctx.DrawLine(axis, new Point(x, y + h), new Point(x + w, y + h));
    }

    private static StreamGeometry Line(Point[] pts)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(pts[0], false);
        for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
        c.EndFigure(false);
        return g;
    }

    /// <summary>Polygon between <paramref name="top"/> (left to right) and <paramref name="bottom"/> (back right to left).</summary>
    private static StreamGeometry Area(Point[] top, Point[] bottom)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(top[0], true);
        for (int i = 1; i < top.Length; i++) c.LineTo(top[i]);
        for (int i = bottom.Length - 1; i >= 0; i--) c.LineTo(bottom[i]);
        c.EndFigure(true);
        return g;
    }
}
