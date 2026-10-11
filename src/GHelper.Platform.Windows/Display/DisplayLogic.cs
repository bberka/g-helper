using static GHelper.Platform.Windows.Display.DisplayNative;

namespace GHelper.Platform.Windows.Display;

/// <summary>Pure decision logic of ScreenNative / ScreenCCD (no OS calls).</summary>
internal static class DisplayLogic
{
    /// <summary>
    /// Built-in panel: INTERNAL / DISPLAYPORT_EMBEDDED output technology, or the friendly name remembered from an earlier
    /// detection (config "internal_display") for panels that report themselves as plain DisplayPort.
    /// </summary>
    public static bool IsInternal(DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY technology, string? friendlyName, string? knownInternalName) =>
        technology == DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.INTERNAL
        || technology == DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYPORT_EMBEDDED
        || (!string.IsNullOrEmpty(knownInternalName) && friendlyName == knownInternalName);

    /// <summary>"\\.\DISPLAY1\Monitor0" -> "\\.\DISPLAY1".</summary>
    public static string ExtractDisplay(string input)
    {
        int index = input.IndexOf('\\', Math.Min(4, input.Length));
        return index != -1 ? input.Substring(0, index) : input;
    }

    /// <summary>
    /// Advanced colour flags to HDR / ACM (auto colour management) as legacy ScreenCCD.GetHDRStatus.
    /// HDR = enabled and not enforced wide colour; ACM = enabled, wide colour enforced and more than 8 bits per channel.
    /// </summary>
    public static (bool hdr, bool acm) DecodeAdvancedColor(uint value, int bitsPerColorChannel)
    {
        bool enabled = (value & 0x2) == 0x2;
        bool wideColorEnforced = (value & 0x4) == 0x4;
        return (enabled && !wideColorEnforced, enabled && wideColorEnforced && bitsPerColorChannel > 8);
    }
}
