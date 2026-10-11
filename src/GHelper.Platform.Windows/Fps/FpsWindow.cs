namespace GHelper.Platform.Windows.Fps;

/// <summary>
/// Rolling window of frame timestamps (QPC ticks) and the "frames in the last second" arithmetic of the legacy ETW FPS
/// monitor. Single writer (ETW callback thread), single reader (overlay tick).
/// </summary>
internal sealed class FpsWindow
{
    public const int Size = 360; // frames: holds a full 1 s window up to 360 fps

    private readonly long[] times = new long[Size];
    private volatile int head;    // next write slot
    private volatile int filled;  // valid entries (capped at Size)

    public void Add(long timestamp)
    {
        int slot = head;
        times[slot] = timestamp;
        head = (slot + 1) % Size;
        if (filled < Size) filled++;
    }

    public void Clear()
    {
        head = 0;
        filled = 0;
    }

    /// <summary>
    /// FPS averaged over the last second of frames; 0 when nothing was presented in the last 4 seconds (idle) or
    /// fewer than two frames are known. <paramref name="now"/> and <paramref name="frequency"/> use the same unit as the
    /// timestamps (QPC ticks / Stopwatch.Frequency).
    /// </summary>
    public double Sample(long now, long frequency)
    {
        int count = filled;
        if (count < 2) return 0;

        int h = head;
        long newest = times[(h - 1 + Size) % Size];

        // no frame in the last seconds: not rendering, blank instead of a stale value
        if (now - newest > 4 * frequency) return 0;

        long cutoff = newest - frequency;
        int frames = 1;
        long oldest = newest;
        for (int i = 2; i <= count; i++)
        {
            long t = times[(h - i + Size) % Size];
            if (t < cutoff) break;
            oldest = t;
            frames++;
        }

        double elapsed = (double)(newest - oldest) / frequency;
        if (elapsed <= 0) return 0;
        return (frames - 1) / elapsed;
    }
}
