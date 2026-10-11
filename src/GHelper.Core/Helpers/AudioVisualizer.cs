using GHelper.Platform;

namespace GHelper.Helpers
{
    /// <summary>
    /// Shared loopback FFT feed for the Aura audio modes and the matrix / slash visualizers.
    /// Samples come from <see cref="IAudioService.StartLoopbackCapture"/> (WASAPI loopback on Windows, PipeWire/Pulse monitor on Linux);
    /// this class only does the windowing and the FFT (FftSharp) and fans the magnitudes out to the subscribers.
    /// </summary>
    public class AudioVisualizer
    {
        public static readonly AudioVisualizer Shared = new();

        private readonly HashSet<Action<double[]>> subscribers = new();
        private volatile Action<double[]>[] subscriberSnapshot = Array.Empty<Action<double[]>>();

        private double[]? audioValues;
        private IDisposable? capture;

        private readonly object _lock = new();
        private volatile bool _running;

        public bool IsRunning => _running;

        public bool Subscribe(Action<double[]> handler)
        {
            lock (_lock)
            {
                if (subscribers.Contains(handler)) return true;
                if (subscribers.Count == 0 && !StartCapture()) return false;
                subscribers.Add(handler);
                subscriberSnapshot = subscribers.ToArray();
                return true;
            }
        }

        public void Unsubscribe(Action<double[]> handler)
        {
            lock (_lock)
            {
                if (!subscribers.Remove(handler)) return;
                subscriberSnapshot = subscribers.ToArray();
                if (subscribers.Count == 0) StopCapture();
            }
        }

        private bool StartCapture()
        {
            if (_running) return true;

            try
            {
                if (!PlatformServices.IsRegistered) return false;

                audioValues = null;
                capture = PlatformServices.Current.Audio.StartLoopbackCapture(OnSamples);

                if (capture is null)
                {
                    Logger.WriteLine("AudioVisualizer: loopback capture is not supported");
                    return false;
                }

                _running = true;
                Logger.WriteLine("AudioVisualizer: subscribed to default output");
                return true;
            }
            catch (Exception ex)
            {
                Logger.WriteLine("AudioVisualizer: " + ex);
                Cleanup();
                return false;
            }
        }

        private void StopCapture()
        {
            _running = false;
            Cleanup();
        }

        private void Cleanup()
        {
            var current = capture;
            capture = null;

            if (current is not null)
            {
                try { current.Dispose(); }
                catch (Exception ex) { Logger.WriteLine("AudioVisualizer: dispose failed: " + ex); }
            }
        }

        /// <summary>Analysis window: 1 ms of audio per kHz of sample rate (48 samples at 48 kHz), the legacy value.</summary>
        public static int WindowSize(int sampleRate) => Math.Max(1, sampleRate / 1000);

        /// <summary>Zero padded FFT magnitudes of <paramref name="window"/> (legacy Capture_DataAvailable tail).</summary>
        public static double[] Spectrum(double[] window)
        {
            double[] padded = FftSharp.Pad.ZeroPad(window);
            var fft = FftSharp.FFT.Forward(padded);
            return FftSharp.FFT.Magnitude(fft);
        }

        /// <summary>
        /// Copy as many leading samples as fit into the analysis window; the tail keeps the previous values, same as the legacy
        /// buffer reuse.
        /// </summary>
        public static void FillWindow(double[] window, ReadOnlySpan<float> samples)
        {
            int count = Math.Min(samples.Length, window.Length);
            for (int i = 0; i < count; i++) window[i] = samples[i];
        }

        private void OnSamples(float[] samples, int sampleRate)
        {
            if (!_running || samples.Length == 0) return;

            int size = WindowSize(sampleRate);
            if (audioValues is null || audioValues.Length != size) audioValues = new double[size];

            FillWindow(audioValues, samples);
            double[] mag = Spectrum(audioValues);

            foreach (var sub in subscriberSnapshot)
            {
                try { sub.Invoke(mag); }
                catch (Exception ex) { Logger.WriteLine("AudioVisualizer: subscriber threw: " + ex); }
            }
        }
    }
}
