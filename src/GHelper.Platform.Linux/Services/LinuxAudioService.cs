using System.Text.RegularExpressions;

namespace GHelper.Platform.Linux;

/// <summary>PipeWire (wpctl) / PulseAudio (pactl) based audio state and loopback capture (parec).</summary>
public sealed class LinuxAudioService : IAudioService, IDisposable
{
    private readonly LinuxContext ctx;
    private readonly List<IDisposable> subscriptions = new();
    private readonly List<Action<bool>> muteHandlers = new();
    private readonly object gate = new();
    private bool lastMuted;

    public LinuxAudioService(LinuxContext ctx)
    {
        this.ctx = ctx;
    }

    private bool HasWpctl => ctx.Runner.CommandExists("wpctl");
    private bool HasPactl => ctx.Runner.CommandExists("pactl");

    public bool IsSupported => HasWpctl || HasPactl;

    // ------------------------------------------------------------------ parsing

    /// <summary>"Volume: 0.40 [MUTED]" (wpctl get-volume).</summary>
    internal static bool? ParseWpctlMuted(string output)
    {
        if (string.IsNullOrWhiteSpace(output) || !output.Contains("Volume:", StringComparison.Ordinal)) return null;
        return output.Contains("[MUTED]", StringComparison.Ordinal);
    }

    /// <summary>"Mute: yes" (pactl get-sink-mute / get-source-mute).</summary>
    internal static bool? ParsePactlMuted(string output)
    {
        Match m = Regex.Match(output ?? "", @"Mute:\s*(yes|no)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return m.Groups[1].Value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ state

    private bool QueryMuted(bool microphone)
    {
        if (HasWpctl)
        {
            CommandResult r = ctx.Runner.Run("wpctl",
                ["get-volume", microphone ? "@DEFAULT_AUDIO_SOURCE@" : "@DEFAULT_AUDIO_SINK@"], 3000);
            if (r.Success && ParseWpctlMuted(r.StdOut) is bool wp) return wp;
        }

        if (HasPactl)
        {
            CommandResult r = ctx.Runner.Run("pactl",
                [microphone ? "get-source-mute" : "get-sink-mute", microphone ? "@DEFAULT_SOURCE@" : "@DEFAULT_SINK@"], 3000);
            if (r.Success && ParsePactlMuted(r.StdOut) is bool pa) return pa;
        }
        return false;
    }

    public bool IsMicMuted() => QueryMuted(true);

    public bool IsMuted() => QueryMuted(false);

    public bool ToggleMicMute()
    {
        bool done = false;
        if (HasWpctl)
            done = ctx.Runner.Run("wpctl", ["set-mute", "@DEFAULT_AUDIO_SOURCE@", "toggle"], 3000).Success;
        if (!done && HasPactl)
            ctx.Runner.Run("pactl", ["set-source-mute", "@DEFAULT_SOURCE@", "toggle"], 3000);

        return IsMicMuted();
    }

    // ------------------------------------------------------------------ mute events

    public void SubscribeMute(Action<bool> onMuteChange)
    {
        bool first;
        lock (gate)
        {
            first = muteHandlers.Count == 0;
            muteHandlers.Add(onMuteChange);
        }
        if (!first) return;

        lastMuted = IsMuted();

        IDisposable? stream = HasPactl
            ? ctx.Runner.StartLineStream("pactl", ["subscribe"], OnPactlEvent)
            : null;

        if (stream is not null) lock (gate) subscriptions.Add(stream);
        else
        {
            var timer = new Timer(_ => CheckMute(), null, 2000, 2000);
            lock (gate) subscriptions.Add(timer);
        }
    }

    private void OnPactlEvent(string line)
    {
        // "Event 'change' on sink #53" / "Event 'change' on server"
        if (line.Contains(" on sink ", StringComparison.Ordinal) || line.Contains(" on server", StringComparison.Ordinal))
            CheckMute();
    }

    private void CheckMute()
    {
        try
        {
            bool muted = IsMuted();
            Action<bool>[] handlers;
            lock (gate)
            {
                if (muted == lastMuted) return;
                lastMuted = muted;
                handlers = muteHandlers.ToArray();
            }
            foreach (var handler in handlers) handler(muted);
        }
        catch (Exception ex)
        {
            LinuxLog.Write("mute check: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------ loopback capture

    public IDisposable? StartLoopbackCapture(Action<float[], int> onSamples)
    {
        if (!ctx.Runner.CommandExists("parec")) return null;

        const int sampleRate = 48000;
        byte[] carry = new byte[4];
        int carried = 0;

        IDisposable? process = ctx.Runner.StartBinaryStream("parec",
            ["--device=@DEFAULT_MONITOR@", "--raw", "--format=float32le", $"--rate={sampleRate}", "--channels=1", "--latency-msec=20"],
            (buffer, count) =>
            {
                // keep samples aligned when a read ends in the middle of a float
                int total = carried + count;
                int samples = total / 4;
                if (samples == 0)
                {
                    Buffer.BlockCopy(buffer, 0, carry, carried, count);
                    carried += count;
                    return;
                }

                var result = new float[samples];
                int src = 0;
                for (int i = 0; i < samples; i++)
                {
                    if (carried > 0)
                    {
                        int need = 4 - carried;
                        Buffer.BlockCopy(buffer, src, carry, carried, need);
                        result[i] = BitConverter.ToSingle(carry, 0);
                        src += need;
                        carried = 0;
                    }
                    else
                    {
                        result[i] = BitConverter.ToSingle(buffer, src);
                        src += 4;
                    }
                }

                int rest = count - src;
                if (rest > 0)
                {
                    Buffer.BlockCopy(buffer, src, carry, 0, rest);
                    carried = rest;
                }

                onSamples(result, sampleRate);
            });

        return process;
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (IDisposable s in subscriptions) s.Dispose();
            subscriptions.Clear();
            muteHandlers.Clear();
        }
    }
}
