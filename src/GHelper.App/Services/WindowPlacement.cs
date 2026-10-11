using Avalonia;

namespace GHelper.App.Services;

public enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>Pure geometry of the legacy SettingsForm.PositionNearTaskbar / GetSideWindowLeft (all values in pixels).</summary>
public static class WindowPlacement
{
    public const int Margin = 10;
    public const int SideGap = 5;

    /// <summary>
    /// The working area excludes the taskbar/panel, so its largest inset identifies the taskbar edge. Without any inset
    /// (auto hide taskbar, Wayland) the bottom edge is assumed.
    /// </summary>
    public static TaskbarEdge GetTaskbarEdge(PixelRect bounds, PixelRect workingArea)
    {
        int top = Math.Max(0, workingArea.Y - bounds.Y);
        int bottom = Math.Max(0, bounds.Bottom - workingArea.Bottom);
        int left = Math.Max(0, workingArea.X - bounds.X);
        int right = Math.Max(0, bounds.Right - workingArea.Right);
        int largest = Math.Max(Math.Max(top, bottom), Math.Max(left, right));

        if (largest == 0) return TaskbarEdge.Bottom;
        if (largest == top) return TaskbarEdge.Top;
        if (largest == left) return TaskbarEdge.Left;
        return TaskbarEdge.Right;
    }

    /// <summary>Top left position for a window of <paramref name="size"/> that hugs the taskbar corner.</summary>
    public static PixelPoint NearTaskbar(PixelRect bounds, PixelRect workingArea, PixelSize size, bool isAlly = false, int margin = Margin)
    {
        int left = workingArea.Right - margin - size.Width;
        int top = workingArea.Bottom - margin - size.Height;

        var edge = GetTaskbarEdge(bounds, workingArea);
        switch (edge)
        {
            case TaskbarEdge.Top:
                top = workingArea.Y + margin;
                break;
            case TaskbarEdge.Left:
                left = workingArea.X + margin;
                break;
            case TaskbarEdge.Right:
                left = workingArea.Right - margin - size.Width;
                break;
        }

        if (isAlly && edge == TaskbarEdge.Bottom)
        {
            // handheld: keep clear of the on-screen button bar
            top = Math.Min(top, bounds.Bottom - 110 - size.Height);
            top = Math.Max(bounds.Y + margin, top);
        }

        return new PixelPoint(left, top);
    }

    /// <summary>
    /// Left edge for a side window (fans, extra, matrix ...). Prefers the left side of the main window; if there is no
    /// room (taskbar on the left) it goes to the right of it.
    /// </summary>
    public static int SideWindowLeft(PixelRect workingArea, PixelRect mainFrame, int width, int gap = SideGap)
    {
        int leftOfMain = mainFrame.X - width - gap;
        if (leftOfMain >= workingArea.X) return leftOfMain;

        return Math.Max(workingArea.X, Math.Min(mainFrame.Right + gap, workingArea.Right - width));
    }
}
