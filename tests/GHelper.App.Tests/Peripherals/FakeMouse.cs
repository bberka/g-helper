using System.Drawing;
using GHelper.Peripherals.Mouse;

namespace GHelper.App.Tests.Peripherals;

/// <summary>
/// Mouse without hardware: no HID, every packet is recorded and answered with an empty reply. Capabilities are plain
/// fields (field initialisers run before the base constructor, which already queries the virtual capability methods).
/// </summary>
internal sealed class FakeMouse : AsusMouse
{
    public readonly List<byte[]> Packets = new();

    public bool Battery_ = true;
    public bool Profiles = true;
    public int DpiCount = 4;
    public bool DpiColors = true;
    public bool ChangeDpiCount = true;
    public bool Rgb = true;
    public LightingZone[] Zones = { LightingZone.Logo, LightingZone.Scrollwheel, LightingZone.Underglow };
    public bool AutoPowerOff = true;
    public bool LowBattery = true;
    public bool AngleSnap = true;
    public bool AngleTune = true;
    public bool Acceleration_ = true;
    public bool Deceleration_ = true;
    public bool LiftOff = true;
    public bool Debounce_ = true;
    public bool MotionSync_ = true;
    public bool ZoneMode_ = true;
    public bool Bindings = true;
    public PollingRate[] Rates = { PollingRate.PR125Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz, PollingRate.PR2000Hz, PollingRate.PR4000Hz, PollingRate.PR8000Hz };
    public string Name = "ROG Test Mouse";
    public bool SpeedShift;

    public FakeMouse() : base(0x0B05, 0x7777, "fake", true)
    {
    }

    public override string GetDisplayName() => Name;
    public override int ProfileCount() => 3;
    public override int DPIProfileCount() => DpiCount;
    public override PollingRate[] SupportedPollingrates() => Rates;

    public override bool HasBattery() => Battery_;
    public override bool HasProfiles() => Profiles;
    public override bool HasDPIColors() => DpiColors;
    public override bool CanChangeDPICount() => ChangeDpiCount;
    public override int DPIIncrements() => 50;
    public override int MinDPI() => 100;
    public override int MaxDPI() => 26000;
    public override bool HasRGB() => Rgb;
    public override LightingZone[] SupportedLightingZones() => Zones;
    public override bool HasAutoPowerOff() => AutoPowerOff;
    public override bool HasLowBatteryWarning() => LowBattery;
    public override int LowBatteryWarningMax() => 80;
    public override int LowBatteryWarningStep() => 10;
    public override bool HasAngleSnapping() => AngleSnap;
    public override bool HasAngleTuning() => AngleTune;
    public override int AngleTuningMin() => -30;
    public override int AngleTuningMax() => 30;
    public override int AngleTuningStep() => 5;
    public override bool HasAcceleration() => Acceleration_;
    public override bool HasDeceleration() => Deceleration_;
    public override int MaxAcceleration() => 10;
    public override int MaxDeceleration() => 10;
    public override bool HasLiftOffSetting() => LiftOff;
    public override bool HasDebounceSetting() => Debounce_;
    public override bool HasMotionSync() => MotionSync_;
    public override bool HasZoneMode() => ZoneMode_;
    public override bool HasButtonBindings() => Bindings;

    public override IReadOnlyList<(string GroupLabel, IReadOnlyList<(ushort Code, string Name)> Items)> BindingGroups =>
        SpeedShift ? SpeedShiftBindingGroups : DefaultBindingGroups;

    protected override byte[]? WriteForResponse(byte[] packet, int matchLength = 3)
    {
        lock (Packets) Packets.Add((byte[])packet.Clone());
        return new byte[USBPacketSize()];
    }

    /// <summary>Make the state the view model reads look like a synchronised device.</summary>
    public override void SynchronizeDevice()
    {
        IsDeviceReady = true;
        Battery = 85;
        Charging = false;
        Profile = 1;
        DpiProfile = 2;
        CurrentDPIProfileCount = Math.Min(3, Math.Max(DpiCount, 0));
        DpiSettings = new AsusMouseDPI[Math.Max(DpiCount, 1)];
        var colors = new[] { Color.Red, Color.Orange, Color.Blue, Color.Green };
        int[] values = { 800, 1600, 3200, 6400 };
        for (int i = 0; i < DpiSettings.Length && i < 4; i++)
            DpiSettings[i] = new AsusMouseDPI { DPI = (uint)values[i], Color = colors[i] };

        PollingRate = PollingRate.PR1000Hz;
        AngleSnapping = true;
        AngleAdjustmentDegrees = 10;
        Debounce = DebounceTime.MS16;
        Acceleration = 4;
        Deceleration = 6;
        LowBatteryWarning = 20;
        PowerOffSetting = PowerOffSetting.Minutes5;
        LiftOffDistance = LiftOffDistance.High;
        MotionSyncSet(true);

        LightingSetting = new LightingSetting[Math.Max(1, Zones.Length)];
        for (int i = 0; i < LightingSetting.Length; i++)
        {
            LightingSetting[i] = new LightingSetting
            {
                LightingMode = LightingMode.Static,
                Brightness = 60,
                RGBColor = Color.FromArgb(255, 0, 200, 120),
            };
        }

        if (Bindings)
        {
            ButtonBindings = new ushort[16];
            foreach (var (slot, def) in ButtonSlots) ButtonBindings[slot] = def.SourceCode;
            ButtonBindingsReady = true;
        }
    }

    private void MotionSyncSet(bool value) => MotionSync = value;
}
