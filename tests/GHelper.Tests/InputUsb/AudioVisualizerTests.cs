using GHelper.Helpers;
using Xunit;

namespace GHelper.Tests.InputUsb;

public class AudioVisualizerTests
{
    [Theory]
    [InlineData(48000, 48)]
    [InlineData(44100, 44)]
    [InlineData(96000, 96)]
    [InlineData(0, 1)]
    public void WindowIsOneMillisecondPerKiloHertz(int sampleRate, int expected)
    {
        Assert.Equal(expected, AudioVisualizer.WindowSize(sampleRate));
    }

    [Fact]
    public void WindowKeepsStaleTailLikeTheLegacyBufferReuse()
    {
        double[] window = { 9, 9, 9, 9 };

        AudioVisualizer.FillWindow(window, new float[] { 0.5f, -0.5f });

        Assert.Equal(new double[] { 0.5, -0.5, 9, 9 }, window);

        AudioVisualizer.FillWindow(window, new float[] { 1, 2, 3, 4, 5, 6 });
        Assert.Equal(new double[] { 1, 2, 3, 4 }, window);
    }

    [Fact]
    public void ToneShowsUpInItsFftBin()
    {
        // 48 samples (1 ms at 48 kHz), zero padded to 64 by the FFT: a period of 8 samples lands in bin 8
        double[] window = new double[48];
        for (int i = 0; i < window.Length; i++) window[i] = Math.Sin(2 * Math.PI * i / 8);

        double[] magnitude = AudioVisualizer.Spectrum(window);

        Assert.True(magnitude.Length >= 32);
        var lower = magnitude.Take(32).ToArray();
        Assert.Equal(8, Array.IndexOf(lower, lower.Max()));
        Assert.True(magnitude[8] > 10 * magnitude[20]);
    }

    [Fact]
    public void SilenceHasNoEnergy()
    {
        double[] magnitude = AudioVisualizer.Spectrum(new double[48]);

        Assert.All(magnitude, m => Assert.Equal(0, m, 12));
    }
}
