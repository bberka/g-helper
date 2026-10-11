using GHelper.Input;
using GHelper.Platform;
using GHelper.Platform.Windows.Display;
using GHelper.Platform.Windows.Fps;
using GHelper.Platform.Windows.Input;
using Xunit;
using static GHelper.Platform.Windows.Display.DisplayNative;

namespace GHelper.Tests.Windows;

public class DisplayLogicTests
{
    [Theory]
    [InlineData(0x80000000u)] // INTERNAL
    [InlineData(11u)]         // DISPLAYPORT_EMBEDDED
    public void BuiltInTechnologiesAreInternal(uint technology)
    {
        Assert.True(DisplayLogic.IsInternal((DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY)technology, "Panel", null));
    }

    [Theory]
    [InlineData(5u)]          // HDMI
    [InlineData(10u)]         // DISPLAYPORT_EXTERNAL
    [InlineData(0xFFFFFFFFu)] // OTHER
    public void ExternalTechnologiesAreNotInternal(uint technology)
    {
        var tech = (DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY)technology;
        Assert.False(DisplayLogic.IsInternal(tech, "Dell U2720Q", null));
        Assert.False(DisplayLogic.IsInternal(tech, "Dell U2720Q", ""));
    }

    [Fact]
    public void RememberedNameMarksAPlainDisplayPortPanelAsInternal()
    {
        Assert.True(DisplayLogic.IsInternal(DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYPORT_EXTERNAL, "NE160QDM", "NE160QDM"));
        Assert.False(DisplayLogic.IsInternal(DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYPORT_EXTERNAL, "Other", "NE160QDM"));
    }

    [Theory]
    [InlineData(@"\\.\DISPLAY1\Monitor0", @"\\.\DISPLAY1")]
    [InlineData(@"\\.\DISPLAY12\Monitor0", @"\\.\DISPLAY12")]
    [InlineData(@"\\.\DISPLAY2", @"\\.\DISPLAY2")]
    [InlineData("", "")]
    public void GdiNameIsCutBeforeTheMonitorSuffix(string input, string expected)
    {
        Assert.Equal(expected, DisplayLogic.ExtractDisplay(input));
    }

    [Theory]
    [InlineData(0u, 8, false, false)]          // advanced colour off
    [InlineData(0x1u, 8, false, false)]        // supported, not enabled
    [InlineData(0x2u, 10, true, false)]        // HDR enabled
    [InlineData(0x6u, 10, false, true)]        // enabled + wide colour enforced, 10 bit: auto colour management
    [InlineData(0x6u, 8, false, false)]        // wide colour enforced but only 8 bit
    public void AdvancedColorDecoding(uint value, int bits, bool hdr, bool acm)
    {
        var result = DisplayLogic.DecodeAdvancedColor(value, bits);
        Assert.Equal(hdr, result.hdr);
        Assert.Equal(acm, result.acm);
    }
}

public class KeyMapperTests
{
    [Fact]
    public void HotkeyModifiersUseTheWin32Values()
    {
        Assert.Equal(0u, KeyMapper.ToWin32Modifiers(HotkeyModifiers.None));
        Assert.Equal(1u, KeyMapper.ToWin32Modifiers(HotkeyModifiers.Alt));
        Assert.Equal(2u, KeyMapper.ToWin32Modifiers(HotkeyModifiers.Control));
        Assert.Equal(4u, KeyMapper.ToWin32Modifiers(HotkeyModifiers.Shift));
        Assert.Equal(8u, KeyMapper.ToWin32Modifiers(HotkeyModifiers.Win));
        Assert.Equal(6u, KeyMapper.ToWin32Modifiers(HotkeyModifiers.Control | HotkeyModifiers.Shift));
    }

    [Theory]
    [InlineData(HotkeyModifiers.None)]
    [InlineData(HotkeyModifiers.Alt | HotkeyModifiers.Win)]
    [InlineData(HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win)]
    public void ModifiersRoundTrip(HotkeyModifiers modifiers)
    {
        Assert.Equal(modifiers, KeyMapper.FromWin32Modifiers(KeyMapper.ToWin32Modifiers(modifiers)));
    }

    [Fact]
    public void VirtualKeyStripsModifierFlags()
    {
        Assert.Equal(0x70, KeyMapper.ToVirtualKey(Keys.F1));
        Assert.Equal(0x41, KeyMapper.ToVirtualKey(Keys.A));
        Assert.Equal(0x41, KeyMapper.ToVirtualKey(Keys.A | Keys.Control | Keys.Shift));
    }

    [Fact]
    public void HookKeyCodesMapBackToKeys()
    {
        Assert.Equal(Keys.F13, KeyMapper.FromVirtualKey(0x7C));
        Assert.Equal(Keys.VolumeMute, KeyMapper.FromVirtualKey(0xAD));
    }

    [Fact]
    public void MouseButtonsAreRecognised()
    {
        Assert.True(KeyMapper.IsMouseButton(Keys.LButton));
        Assert.True(KeyMapper.IsMouseButton(Keys.RButton));
        Assert.True(KeyMapper.IsMouseButton(Keys.MButton));
        Assert.False(KeyMapper.IsMouseButton(Keys.A));
    }
}

public class FpsWindowTests
{
    private const long Freq = 10_000_000; // QPC style frequency

    private static FpsWindow Fill(int frames, long startTick, long stepTicks)
    {
        var window = new FpsWindow();
        for (int i = 0; i < frames; i++) window.Add(startTick + i * stepTicks);
        return window;
    }

    [Fact]
    public void NeedsTwoFrames()
    {
        var window = new FpsWindow();
        Assert.Equal(0, window.Sample(0, Freq));

        window.Add(100);
        Assert.Equal(0, window.Sample(100, Freq));
    }

    [Fact]
    public void SteadySixtyFps()
    {
        long step = Freq / 60;
        var window = Fill(120, 1_000, step);
        long now = 1_000 + 119 * step;

        Assert.Equal(60, window.Sample(now, Freq), 0.5);
    }

    [Fact]
    public void OnlyTheLastSecondCounts()
    {
        // 30 fps for 3 s, then 120 fps for 1 s
        var window = new FpsWindow();
        long t = 0;
        for (int i = 0; i < 90; i++) { window.Add(t); t += Freq / 30; }
        for (int i = 0; i < 120; i++) { window.Add(t); t += Freq / 120; }

        double fps = window.Sample(t, Freq);
        Assert.InRange(fps, 110, 130);
    }

    [Fact]
    public void IdleReturnsZeroAfterFourSeconds()
    {
        var window = Fill(60, 0, Freq / 60);
        long last = 59 * (Freq / 60);

        Assert.True(window.Sample(last + Freq, Freq) > 0);
        Assert.Equal(0, window.Sample(last + 5 * Freq, Freq));
    }

    [Fact]
    public void ClearForgetsEverything()
    {
        var window = Fill(30, 0, Freq / 30);
        window.Clear();
        Assert.Equal(0, window.Sample(29 * (Freq / 30), Freq));
    }

    [Fact]
    public void RingBufferWrapsWithoutLosingTheNewestFrames()
    {
        long step = Freq / 240;
        var window = Fill(FpsWindow.Size * 3, 0, step);
        long now = (FpsWindow.Size * 3 - 1) * step;

        Assert.Equal(240, window.Sample(now, Freq), 1.0);
    }
}
