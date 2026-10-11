using Avalonia;

namespace GHelper.App.Services;

/// <summary>Pure geometry of the main window / side window placement (upstream Program.SettingsToggle; pixels).</summary>
public static class WindowPlacement
{
    public const int Margin = 10;
    public const int SideGap = 5;

    /// <summary>
    /// Top left position for a window of <paramref name="size"/> that sits in the corner of the work area next to the
    /// taskbar / panel (upstream Program.SettingsToggle since #6130): always against the right edge of the work area;
    /// below a top panel, otherwise above the bottom edge. Handhelds keep 110 px clear of the screen bottom.
    /// </summary>
    public static PixelPoint NearTaskbar(PixelRect bounds, PixelRect workingArea, PixelSize size, bool isAlly = false, int margin = Margin)
    {
        int left = workingArea.Right - margin - size.Width;
        int top = workingArea.Y > bounds.Y
            ? workingArea.Y + margin
            : workingArea.Bottom - margin - size.Height;

        if (isAlly)
            top = Math.Max(bounds.Y + margin, bounds.Bottom - 110 - size.Height);

        return new PixelPoint(left, top);
    }

    /// <summary>
    /// Left edge for a side window (fans, extra, matrix ...): directly left of the main window, which sits at the right
    /// edge of the work area (upstream <c>Left - Width - 5</c>), kept inside the work area.
    /// </summary>
    public static int SideWindowLeft(PixelRect workingArea, PixelRect mainFrame, int width, int gap = SideGap)
        => Math.Max(workingArea.X, mainFrame.X - width - gap);
}
