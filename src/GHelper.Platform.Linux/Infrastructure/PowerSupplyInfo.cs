namespace GHelper.Platform.Linux;

/// <summary>/sys/class/power_supply helpers shared by the backend, the sensors and the power service.</summary>
internal static class PowerSupplyInfo
{
    public const string Dir = "/sys/class/power_supply";

    public static IEnumerable<string> All(SysfsRoot root) => root.ListDirectories(Dir);

    /// <summary>The system battery (type Battery, scope System), BAT0 preferred.</summary>
    public static string? FindBattery(SysfsRoot root)
    {
        string? best = null;
        foreach (string dir in All(root))
        {
            if (!string.Equals(root.ReadText(dir + "/type"), "Battery", StringComparison.OrdinalIgnoreCase)) continue;
            string? scope = root.ReadText(dir + "/scope");
            if (string.Equals(scope, "Device", StringComparison.OrdinalIgnoreCase)) continue; // wireless mouse, ...
            string name = Path.GetFileName(dir);
            if (name.StartsWith("BAT", StringComparison.OrdinalIgnoreCase) || name == "BATT" || name == "BATC")
            {
                if (best is null || string.CompareOrdinal(name, Path.GetFileName(best)) < 0) best = dir;
            }
            best ??= dir;
        }
        return best;
    }

    /// <summary>true = on mains power, false = on battery, null = unknown (no mains supply reported).</summary>
    public static bool? IsOnMains(SysfsRoot root)
    {
        bool sawMains = false;
        foreach (string dir in All(root))
        {
            string? type = root.ReadText(dir + "/type");
            if (type is null) continue;
            bool isMains = type.Equals("Mains", StringComparison.OrdinalIgnoreCase)
                           || type.StartsWith("USB", StringComparison.OrdinalIgnoreCase);
            if (!isMains) continue;
            // USB peripherals (phones, mice) can also appear as USB supplies; only count system supplies
            if (string.Equals(root.ReadText(dir + "/scope"), "Device", StringComparison.OrdinalIgnoreCase)) continue;

            sawMains = true;
            if (root.ReadInt(dir + "/online") == 1) return true;
        }
        return sawMains ? false : null;
    }

    /// <summary>Instantaneous battery power in watts (positive), null when the supply does not report it.</summary>
    public static double? ReadPowerWatts(SysfsRoot root, string batteryDir)
    {
        long? powerNow = root.ReadLong(batteryDir + "/power_now");
        if (powerNow is > 0) return powerNow.Value / 1_000_000.0;

        long? current = root.ReadLong(batteryDir + "/current_now");
        long? voltage = root.ReadLong(batteryDir + "/voltage_now");
        if (current is not null && voltage is not null && current != 0)
            return Math.Abs(current.Value / 1_000_000.0 * (voltage.Value / 1_000_000.0));

        return null;
    }

    public static BatteryState? ReadBattery(SysfsRoot root)
    {
        string? dir = FindBattery(root);
        if (dir is null) return null;

        string status = root.ReadText(dir + "/status") ?? "Unknown";
        bool charging = status.Equals("Charging", StringComparison.OrdinalIgnoreCase);
        bool discharging = status.Equals("Discharging", StringComparison.OrdinalIgnoreCase);
        bool? mains = IsOnMains(root);
        bool onAc = mains ?? !discharging;

        double percent = root.ReadInt(dir + "/capacity") ?? 0;
        double? voltage = root.ReadLong(dir + "/voltage_now") is { } vn && vn > 0 ? vn / 1_000_000.0 : null;

        double? design = null, full = null;
        long? energyDesign = root.ReadLong(dir + "/energy_full_design");
        long? energyFull = root.ReadLong(dir + "/energy_full");
        if (energyDesign is > 0) design = energyDesign.Value / 1_000_000.0;
        if (energyFull is > 0) full = energyFull.Value / 1_000_000.0;

        if (design is null || full is null)
        {
            // charge_* attributes are in uAh, convert with the design/min voltage
            double? vRef = root.ReadLong(dir + "/voltage_min_design") is { } vmin && vmin > 0 ? vmin / 1_000_000.0 : voltage;
            long? chargeDesign = root.ReadLong(dir + "/charge_full_design");
            long? chargeFull = root.ReadLong(dir + "/charge_full");
            if (vRef is not null)
            {
                if (design is null && chargeDesign is > 0) design = chargeDesign.Value / 1_000_000.0 * vRef;
                if (full is null && chargeFull is > 0) full = chargeFull.Value / 1_000_000.0 * vRef;
            }
        }

        double? rate = discharging ? ReadPowerWatts(root, dir) : null;
        bool present = root.ReadInt(dir + "/present") != 0;

        return new BatteryState(present, charging, onAc, percent, rate, design, full, voltage);
    }
}
