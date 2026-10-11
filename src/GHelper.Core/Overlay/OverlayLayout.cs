using System.Drawing;

namespace GHelper.Overlay
{
    /// <summary>Which blocks are drawn (after the per-mode user options are applied).</summary>
    public readonly record struct OverlayBlocks(
        bool Fps, bool Temp, bool Fans, bool Chart, bool Power, bool Usage, bool Ram, bool Names, bool Battery);

    /// <summary>Resolved horizontal layout of one overlay frame, all values in pixels at the given scale.</summary>
    public readonly record struct OverlayLayoutResult(
        int Width, int Height,
        int PadX, int PadY, int LineHeight, int LineGap, int CornerRadius,
        int FpsColWidth, int NameX, int NameColWidth,
        int LeftX, int ChartX, int ChartWidth,
        int PowerX, int PowerColWidth,
        int UsageNumX, int UsageNumColWidth, int BarX, int BarWidth,
        int MemNumX, int MemNumColWidth, int MemBarX,
        int BatteryX, int BatteryColWidth, int BatteryIconX, int BatteryIconWidth, int BatteryIconHeight,
        int InnerHeight);

    /// <summary>Usage bar cell geometry (fixed bar height per scale, integer cell pitch).</summary>
    public readonly record struct OverlayBarGeometry(int CellHeight, int SeparatorHeight, int NumCells, int BarHeight, int BarYOffset);

    /// <summary>
    /// The overlay's layout rules (legacy HardwareOverlay.PerformPaint metrics), independent of any drawing API.
    /// Layouts per preset:
    /// Light:    fps | temp | power
    /// Default:  fps | temp + fan RPM | chart | power | bat% + rate
    /// Full:     fps | temp + fan RPM | chart | power | usage% | bar | bat% + rate
    /// Complete: fps | name | temp + fan RPM | chart | power | usage% | bar | mem GB | bar | bat% + rate
    /// </summary>
    public static class OverlayLayout
    {
        // Fixed base scale, deliberately independent of OS DPI. 2.0 reproduces the size the overlay had at 200% display scaling.
        public const float BaseScale = 2.0f;

        public const float BaseFontSize = 13f;
        public const float BaseRpmFontSize = 8.5f;
        public const int BaseLineHeight = 18;
        public const int BaseLineSpacing = 1;
        public const int BasePadX = 8;
        public const int BasePadY = 4;
        public const int BaseFpsColWidth = 52;
        public const int BaseLeftColWidth = 128;
        public const int BaseChartColWidth = 120;
        public const int BasePowerGap = 4;
        public const int BasePowerColWidth = 46; // fits "120.9W" right-aligned
        public const int BaseColGap = 8;
        public const int CornerRadius = 3;
        public const int MarginFromEdge = 10;
        public const int BaseLightLeftColWidth = 64; // fits "GPU: 82" (9 Consolas chars)
        public const int BaseUsageBarGap = 11;       // gap between the power W and the usage % column (full mode)
        public const int BaseUsageBarWidth = 5;
        public const int BaseUsageNumGap = 4;        // gap between the usage % text and its bar
        public const int BaseUsageNumColWidth = 30;  // right-aligned column fitting "100%"
        public const int BaseFullPadRight = 4;       // tighter right margin in full mode (vs BasePadX)
        public const int BaseUsageBarHeight = 15;    // tuned so at 2x scale numCells = 10
        public const int BaseUsageBarYNudge = 1;     // raise bars to align with the text baseline
        public const int BaseNameColWidth = 90;      // fits "Core Ultra 9" / "Ryzen AI 9"
        public const int BaseMemBarGap = 8;
        public const int BaseMemNumColWidth = 54;    // fits "127.9GB"
        public const int BaseBatColGap = 8;
        public const int BaseBatColWidth = 38;       // fits "-22.4"
        public const int BaseBatIconGap = 2;
        public const int BaseBatIconWidth = 8;
        public const int BaseBatIconHeight = 15;
        public const int BaseBatNubWidth = 4;
        public const int BaseBatNubHeight = 2;

        public const int BaseLightWidth = BasePadX + BaseFpsColWidth + BaseColGap + BaseLightLeftColWidth + BasePowerGap + BasePowerColWidth + BasePadX;
        public const int BaseWidth = BasePadX + BaseFpsColWidth + BaseColGap + BaseLeftColWidth + BaseColGap + BaseChartColWidth + BasePowerGap + BasePowerColWidth + BasePadX;
        public const int BaseFullWidth = BaseWidth - BasePadX + BaseUsageBarGap + BaseUsageBarWidth + BaseUsageNumGap + BaseUsageNumColWidth + BaseFullPadRight;
        public const int BaseCompleteWidth = BaseFullWidth + BaseMemBarGap + BaseMemNumColWidth + BaseUsageNumGap + BaseUsageBarWidth;

        public static float GetScale(int scalePercent) => BaseScale * (scalePercent / 100f);

        public static int S(float sc, int v) => (int)(v * sc);

        /// <summary>Unscaled width estimate for a preset, used for the first window size before the first paint (legacy BaseModeWidth).</summary>
        public static int BaseModeWidth(OverlayMode mode, bool batteryColumn, bool names)
        {
            int w = mode switch
            {
                OverlayMode.Light => BaseLightWidth,
                OverlayMode.Full => BaseFullWidth,
                OverlayMode.Complete => BaseCompleteWidth,
                _ => BaseWidth,
            };
            if (batteryColumn) w += BaseBatColGap + BaseBatColWidth + BaseBatIconGap + BaseBatIconWidth;
            return names ? w + BaseNameColWidth + BaseColGap : w;
        }

        public static Size InitialSize(OverlayMode mode, int scalePercent, bool batteryColumn, bool names)
        {
            float sc = GetScale(scalePercent);
            int innerH = S(sc, BaseLineHeight) * 2 + S(sc, BaseLineSpacing);
            return new Size(S(sc, BaseModeWidth(mode, batteryColumn, names)), S(sc, BasePadY) * 2 + innerH);
        }

        /// <param name="batteryVisible">Battery block enabled AND the device runs on battery (the column only shows then).</param>
        public static OverlayLayoutResult Compute(float sc, OverlayBlocks blocks, bool batteryVisible)
        {
            int padX = S(sc, BasePadX);
            int padY = S(sc, BasePadY);
            int lineH = S(sc, BaseLineHeight);
            int lineGap = S(sc, BaseLineSpacing);
            int radius = S(sc, CornerRadius);
            int fpsColW = S(sc, BaseFpsColWidth);
            int chartColW = S(sc, BaseChartColWidth);
            int powColW = S(sc, BasePowerColWidth);
            int colGap = S(sc, BaseColGap);
            int powGap = S(sc, BasePowerGap);

            int innerH = lineH * 2 + lineGap;
            int totalH = padY * 2 + innerH;

            bool showUsageMetric = blocks.Usage;
            bool showMem = blocks.Ram;
            bool showNames = blocks.Names;
            bool showBat = batteryVisible;

            int nameColW = showNames ? S(sc, BaseNameColWidth) : 0;
            int usageNumColW = S(sc, BaseUsageNumColWidth);
            int barW = S(sc, BaseUsageBarWidth);
            int memNumColW = S(sc, BaseMemNumColWidth);
            int batColW = S(sc, BaseBatColWidth);

            int cursor = padX;
            if (blocks.Fps) cursor += fpsColW + colGap;

            int nameX = cursor;
            if (showNames) cursor += nameColW + colGap;

            int leftX = cursor;
            cursor += S(sc, blocks.Temp && blocks.Fans ? BaseLeftColWidth : blocks.Temp || blocks.Fans ? BaseLightLeftColWidth : 0);

            int chartX = cursor;
            if (blocks.Chart) cursor += chartColW;

            int powX = cursor;
            if (blocks.Power) { powX = cursor + powGap; cursor = powX + powColW; }

            int usageNumX = cursor, barX = cursor;
            if (showUsageMetric)
            {
                usageNumX = cursor + S(sc, BaseUsageBarGap);
                barX = usageNumX + usageNumColW + S(sc, BaseUsageNumGap);
                cursor = barX + barW;
            }

            int memNumX = cursor, memBarX = cursor;
            if (showMem)
            {
                memNumX = cursor + S(sc, BaseMemBarGap);
                memBarX = memNumX + memNumColW + S(sc, BaseUsageNumGap);
                cursor = memBarX + barW;
            }

            int batX = cursor, batIconX = cursor;
            if (showBat)
            {
                batX = cursor + S(sc, BaseBatColGap);
                batIconX = batX + batColW + S(sc, BaseBatIconGap);
                cursor = batIconX + S(sc, BaseBatIconWidth);
            }

            int width = cursor + padX;

            return new OverlayLayoutResult(
                Width: width, Height: totalH,
                PadX: padX, PadY: padY, LineHeight: lineH, LineGap: lineGap, CornerRadius: radius,
                FpsColWidth: fpsColW, NameX: nameX, NameColWidth: nameColW,
                LeftX: leftX, ChartX: chartX, ChartWidth: chartColW,
                PowerX: powX, PowerColWidth: powColW,
                UsageNumX: usageNumX, UsageNumColWidth: usageNumColW, BarX: barX, BarWidth: barW,
                MemNumX: memNumX, MemNumColWidth: memNumColW, MemBarX: memBarX,
                BatteryX: batX, BatteryColWidth: batColW, BatteryIconX: batIconX,
                BatteryIconWidth: S(sc, BaseBatIconWidth), BatteryIconHeight: S(sc, BaseBatIconHeight),
                InnerHeight: innerH);
        }

        /// <summary>
        /// Bar sizing: fixed bar height per scale; cellH grows with scale but the separator stays 1px, so cells are always
        /// a clean integer height and only their count varies.
        /// </summary>
        public static OverlayBarGeometry ComputeBar(float sc, int lineHeight)
        {
            int cellH = Math.Max(1, (int)Math.Floor(sc));
            int sepH = 1;
            int targetBarH = S(sc, BaseUsageBarHeight);
            int pitch = cellH + sepH;
            int numCells = Math.Max(2, (targetBarH + sepH) / pitch);
            int barH = numCells * cellH + (numCells - 1) * sepH;
            int barYOff = (lineHeight - barH) / 2 - S(sc, BaseUsageBarYNudge);
            return new OverlayBarGeometry(cellH, sepH, numCells, barH, barYOff);
        }

        /// <summary>Number of lit cells (counted from the bottom) of a usage bar.</summary>
        public static int LitCells(int usage, int numCells) =>
            Math.Clamp((int)Math.Ceiling(usage * numCells / 100f), 0, numCells);

        /// <summary>
        /// Window location of a pinned overlay: anchor bit 0 = right edge, bit 1 = bottom edge, offsets measured from that
        /// edge; the result is kept 5px inside the screen. Anchor &lt; 0 means "never positioned": top-left + margin.
        /// </summary>
        public static Point RestorePosition(Rectangle screen, Size window, int anchor, int offsetX, int offsetY)
        {
            if (anchor < 0) return new Point(screen.X + MarginFromEdge, screen.Y + MarginFromEdge);

            bool isRight = (anchor & 1) != 0;
            bool isBottom = (anchor & 2) != 0;
            int x = isRight ? screen.Right - window.Width - offsetX : screen.X + offsetX;
            int y = isBottom ? screen.Bottom - window.Height - offsetY : screen.Y + offsetY;
            const int margin = 5;
            x = Math.Max(screen.Left + margin, Math.Min(x, screen.Right - window.Width - margin));
            y = Math.Max(screen.Top + margin, Math.Min(y, screen.Bottom - window.Height - margin));
            return new Point(x, y);
        }

        /// <summary>Inverse of <see cref="RestorePosition"/>: (anchor, offsetX, offsetY) to persist for the current location.</summary>
        public static (int anchor, int offsetX, int offsetY) ToAnchor(Rectangle screen, Point location, Size window)
        {
            Point center = new Point(location.X + window.Width / 2, location.Y + window.Height / 2);
            bool isRight = center.X > screen.X + screen.Width / 2;
            bool isBottom = center.Y > screen.Y + screen.Height / 2;
            int anchor = (isBottom ? 2 : 0) | (isRight ? 1 : 0);
            int offsetX = isRight ? screen.Right - location.X - window.Width : location.X - screen.X;
            int offsetY = isBottom ? screen.Bottom - location.Y - window.Height : location.Y - screen.Y;
            return (anchor, offsetX, offsetY);
        }
    }
}
