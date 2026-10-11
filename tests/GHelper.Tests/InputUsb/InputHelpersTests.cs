using GHelper.Helpers;
using GHelper.Input;
using Xunit;

namespace GHelper.Tests.InputUsb;

public class InputHelpersTests
{
    [Theory]
    [InlineData("0x5B 0x50", new[] { 0x5B, 0x50 })]
    [InlineData("5B 50 7", new[] { 0x5B, 0x50, 0x07 })]
    [InlineData("0xAF", new[] { 0xAF })]
    [InlineData("a0 0x1b", new[] { 0xA0, 0x1B })]
    public void HexKeyCombosAreParsed(string command, int[] expected)
    {
        Assert.Equal(expected, KeyCommand.ParseHexValues(command));
    }

    [Theory]
    [InlineData("")]
    [InlineData("calc")]
    [InlineData("C:\\Windows\\System32\\notepad.exe")]
    [InlineData("0x5B, 0x50")]
    [InlineData("0x5B1 0x50")]
    public void NonKeyCommandsAreNotKeyCombos(string command)
    {
        Assert.Empty(KeyCommand.ParseHexValues(command));
    }

    [Fact]
    public void ModifierConfigIsParsed()
    {
        var fallback = ModifierKeys.Shift | ModifierKeys.Control;

        Assert.Equal(fallback, KeyCommand.ParseModifiers(null, fallback));
        Assert.Equal(fallback, KeyCommand.ParseModifiers("  ", fallback));
        Assert.Equal(ModifierKeys.Alt | ModifierKeys.Win, KeyCommand.ParseModifiers("Win-ALT", fallback));
        Assert.Equal(ModifierKeys.None, KeyCommand.ParseModifiers("meta", fallback));
    }

    [Fact]
    public void ModifierFlagsMatchHotkeyContract()
    {
        // the legacy Win32 values are passed through to IInputService.RegisterHotkey unchanged
        Assert.Equal((int)GHelper.Platform.HotkeyModifiers.Alt, (int)ModifierKeys.Alt);
        Assert.Equal((int)GHelper.Platform.HotkeyModifiers.Control, (int)ModifierKeys.Control);
        Assert.Equal((int)GHelper.Platform.HotkeyModifiers.Shift, (int)ModifierKeys.Shift);
        Assert.Equal((int)GHelper.Platform.HotkeyModifiers.Win, (int)ModifierKeys.Win);
        Assert.Equal(0x4000, (int)ModifierKeys.NoRepeat);
    }

    [Fact]
    public void HidKeyReportFiltering()
    {
        // report id, key code
        Assert.True(AsusKeyReport.TryGetKey(new byte[] { 0x5A, 0x38 }, false, out int key));
        Assert.Equal(0x38, key);

        // heartbeat and release are dropped
        Assert.False(AsusKeyReport.TryGetKey(new byte[] { 0x5A, 236 }, false, out _));
        Assert.False(AsusKeyReport.TryGetKey(new byte[] { 0x5A, 0 }, false, out _));

        // the Ally also reports releases
        Assert.True(AsusKeyReport.TryGetKey(new byte[] { 0x5A, 0 }, true, out key));
        Assert.Equal(0, key);
        Assert.False(AsusKeyReport.TryGetKey(new byte[] { 0x5A, 236 }, true, out _));

        // other reports and truncated ones
        Assert.False(AsusKeyReport.TryGetKey(new byte[] { 0x5D, 0x10 }, false, out _));
        Assert.False(AsusKeyReport.TryGetKey(new byte[] { 0x5A }, false, out _));
        Assert.False(AsusKeyReport.TryGetKey(Array.Empty<byte>(), true, out _));
    }

    [Fact]
    public void NumberPadRequestLayout()
    {
        byte[] request = NumberPad.BuildRequest(function: 7, value: 2);

        Assert.Equal(0x800, request.Length);
        Assert.Equal(BitConverter.GetBytes(0x104), request[0..4]);
        Assert.Equal(BitConverter.GetBytes(0x800), request[4..8]);
        Assert.Equal(new byte[] { 0xFF, 0x00, 0x00, 0xFF }, request[8..12]);
        Assert.Equal(BitConverter.GetBytes(7), request[12..16]);
        Assert.Equal(BitConverter.GetBytes(0xC), request[16..20]);
        Assert.Equal(BitConverter.GetBytes(2), request[20..24]);
        Assert.All(request[24..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void ToastIconKeysAreStable()
    {
        Assert.Equal("brightness_up", ToastIconKeys.Get(ToastIcon.BrightnessUp));
        Assert.Equal("backlight_down", ToastIconKeys.Get(ToastIcon.BacklightDown));
        Assert.Equal("microphone_mute", ToastIconKeys.Get(ToastIcon.MicrophoneMute));
        Assert.Equal("fnlock", ToastIconKeys.Get(ToastIcon.FnLock));
        Assert.Equal("controller", ToastIconKeys.Get(ToastIcon.Controller));
    }

    sealed class IdleExtras : IInputExtras
    {
        public TimeSpan GetIdleTime() => TimeSpan.FromSeconds(42);
        public void TurnOffScreen() { }
        public Keys GetModifierKeys() => Keys.Shift;
        public bool? GetTouchpadEnabled() => true;
        public void SetSlateMode(int status) { }
        public string? GetAsusSoftwarePath() => null;
    }

    [Fact]
    public void InputExtrasOverrideWins()
    {
        var extras = new IdleExtras();
        InputExtras.Override = extras;
        try
        {
            Assert.Same(extras, InputExtras.Current);
        }
        finally
        {
            InputExtras.Override = null;
        }

        // without override or platform support the null object answers
        Assert.NotNull(InputExtras.Current);
    }
}
