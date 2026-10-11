using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace GHelper.Platform.Windows.Audio;

/// <summary>
/// Core Audio (WASAPI) through NAudio: mic mute toggle, mute state subscription and loopback capture of the default
/// output for the audio visualizer. Lifted from legacy app/Helpers/Audio.cs and AudioVisualizer.cs.
/// </summary>
public sealed class WindowsAudioService : IAudioService
{
    private AudioEndpointVolume? endpointVolume; // keeps the COM callback alive

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool ToggleMicMute()
    {
        if (!OperatingSystem.IsWindows()) return false;

        return ToggleMic();
    }

    [SupportedOSPlatform("windows")]
    private static bool ToggleMic()
    {
        using var enumerator = new MMDeviceEnumerator();
        var commDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        var consoleDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        var mmDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

        bool status = !commDevice.AudioEndpointVolume.Mute;

        commDevice.AudioEndpointVolume.Mute = status;
        consoleDevice.AudioEndpointVolume.Mute = status;
        mmDevice.AudioEndpointVolume.Mute = status;

        Logger.WriteLine(commDevice + ":" + status);
        Logger.WriteLine(consoleDevice + ":" + status);
        Logger.WriteLine(mmDevice + ":" + status);

        return status;
    }

    public bool IsMicMuted()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).AudioEndpointVolume.Mute;
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Error checking mic mute status: " + ex.Message);
            return false; // assume not muted
        }
    }

    public bool IsMuted()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).AudioEndpointVolume.Mute;
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Error checking mute status: " + ex.Message);
            return false;
        }
    }

    public void SubscribeMute(Action<bool> onMuteChange)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            endpointVolume = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).AudioEndpointVolume;
            bool muted = endpointVolume.Mute;
            onMuteChange(muted);
            endpointVolume.OnVolumeNotification += data =>
            {
                if (data.Muted != muted) onMuteChange(muted = data.Muted);
            };
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Error subscribing to volume notifications: " + ex.Message);
        }
    }

    public IDisposable? StartLoopbackCapture(Action<float[], int> onSamples)
    {
        if (!OperatingSystem.IsWindows()) return null;

        var capture = new LoopbackCaptureSession(onSamples);
        if (capture.Start()) return capture;

        capture.Dispose();
        return null;
    }
}

/// <summary>
/// Loopback capture of the default render device that follows default device changes and converts every buffer to
/// mono float samples (first channel, like the legacy FFT input) in -1..1.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class LoopbackCaptureSession : IMMNotificationClient, IDisposable
{
    private readonly Action<float[], int> onSamples;
    private readonly object sync = new();
    private WasapiCapture? capture;
    private MMDeviceEnumerator? enumerator;
    private string? captureDeviceId;
    private bool running;
    private bool disposed;

    public LoopbackCaptureSession(Action<float[], int> onSamples)
    {
        this.onSamples = onSamples;
    }

    public bool Start()
    {
        lock (sync)
        {
            if (running) return true;

            try
            {
                enumerator = new MMDeviceEnumerator();
                enumerator.RegisterEndpointNotificationCallback(this);

                using MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                capture = new WasapiLoopbackCapture(device);
                captureDeviceId = device.ID;
                capture.DataAvailable += OnDataAvailable;
                capture.StartRecording();

                running = true;
                Logger.WriteLine("Audio loopback: subscribed to default output");
                return true;
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Audio loopback: " + ex.Message);
                Cleanup();
                return false;
            }
        }
    }

    /// <summary>Convert one capture buffer to mono (first channel) floats in -1..1.</summary>
    public static float[] ConvertBuffer(byte[] buffer, int bytesRecorded, int bitsPerSample, int channels, bool isFloat)
    {
        int bytesPerChannel = bitsPerSample / 8;
        int frameBytes = bytesPerChannel * Math.Max(1, channels);
        if (bytesPerChannel == 0 || frameBytes == 0) return [];

        int frames = bytesRecorded / frameBytes;
        float[] samples = new float[frames];

        if (isFloat && bytesPerChannel == 4)
            for (int i = 0; i < frames; i++) samples[i] = BitConverter.ToSingle(buffer, i * frameBytes);
        else if (bytesPerChannel == 2)
            for (int i = 0; i < frames; i++) samples[i] = BitConverter.ToInt16(buffer, i * frameBytes) / 32768f;
        else if (bytesPerChannel == 4)
            for (int i = 0; i < frames; i++) samples[i] = BitConverter.ToInt32(buffer, i * frameBytes) / 2147483648f;

        return samples;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var format = capture?.WaveFormat;
        if (format is null) return;

        try
        {
            float[] samples = ConvertBuffer(e.Buffer, e.BytesRecorded, format.BitsPerSample, format.Channels,
                format.Encoding == WaveFormatEncoding.IeeeFloat || format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32);
            if (samples.Length > 0) onSamples(samples, format.SampleRate);
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Audio loopback handler: " + ex.Message);
        }
    }

    private void Cleanup()
    {
        if (enumerator is not null)
        {
            try { enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
        }

        if (capture is not null)
        {
            try
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.StopRecording();
                capture.Dispose();
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Audio loopback dispose: " + ex.Message);
            }

            capture = null;
        }

        captureDeviceId = null;
        running = false;

        if (enumerator is not null)
        {
            try { enumerator.Dispose(); } catch { }
            enumerator = null;
        }
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (!running || disposed) return;
        if (flow != DataFlow.Render || role != Role.Console) return;
        if (!string.IsNullOrEmpty(captureDeviceId) && captureDeviceId == defaultDeviceId) return;

        Logger.WriteLine("Audio loopback: default output changed -> " + defaultDeviceId);

        Task.Delay(50).ContinueWith(_ =>
        {
            lock (sync)
            {
                if (disposed) return;
                Cleanup();
                Start();
            }
        });
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            Cleanup();
        }
    }
}
