namespace GHelper.Platform.Windows.Power;

internal enum PowerSettingKind
{
    Unknown,
    Lid,
    Display,
    BatterySaver,
    PowerSource,
}

/// <summary>Decoded PBT_POWERSETTINGCHANGE notification. <see cref="Flag"/> meaning depends on the kind (see decoder).</summary>
internal readonly record struct PowerSettingEvent(PowerSettingKind Kind, bool Flag);

/// <summary>Pure mapping of power setting GUID + payload to the IPowerService events (legacy Settings.WndProc).</summary>
internal static class PowerSettingDecoder
{
    // 0 = closed, 1 = open
    public static readonly Guid LidSwitchStateChange = new("ba3e0f4d-b817-4094-a2d1-d56379e6a0f3");
    // 0 = off, 1 = on, 2 = dimmed
    public static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");
    // Win11 energy saver status (0 = off)
    public static readonly Guid EnergySaverStatus = new("550E8400-E29B-41D4-A716-446655440000");
    // classic battery saver (0 = off, 1 = on)
    public static readonly Guid PowerSavingStatus = new("E00958C0-C213-4ACE-AC77-FECCED2EEEA5");
    // 0 = AC, 1 = battery, 2 = short term (UPS)
    public static readonly Guid AcdcPowerSource = new("5d3e9a59-e9D5-4b00-a6bd-ff34ff516548");

    public static readonly Guid[] AllSettings =
    [
        LidSwitchStateChange, ConsoleDisplayState, EnergySaverStatus, PowerSavingStatus, AcdcPowerSource,
    ];

    /// <summary>
    /// Lid: Flag = closed. Display: Flag = on (dimmed is reported as Unknown). BatterySaver: Flag = active.
    /// PowerSource: Flag = on AC.
    /// </summary>
    public static PowerSettingEvent Decode(Guid setting, int data)
    {
        if (setting == LidSwitchStateChange) return new(PowerSettingKind.Lid, data == 0);

        if (setting == ConsoleDisplayState)
            return data is 0 or 1 ? new(PowerSettingKind.Display, data == 1) : new(PowerSettingKind.Unknown, false);

        if (setting == EnergySaverStatus || setting == PowerSavingStatus) return new(PowerSettingKind.BatterySaver, data != 0);

        if (setting == AcdcPowerSource) return new(PowerSettingKind.PowerSource, data == 0);

        return new(PowerSettingKind.Unknown, false);
    }

    /// <summary>SYSTEM_POWER_STATUS.ACLineStatus: 1 online, 0 offline, 255 unknown.</summary>
    public static PowerSource ToPowerSource(byte acLineStatus) => acLineStatus switch
    {
        1 => PowerSource.AC,
        0 => PowerSource.Battery,
        _ => PowerSource.Unknown,
    };
}
