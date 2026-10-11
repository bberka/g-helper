namespace GHelper.Platform.Linux;

/// <summary>
/// Custom fan curves of asus-wmi (hwmon device "asus_custom_fan_curve"):
///   pwmN_auto_pointM_temp  (M = 1..8, degrees C), pwmN_auto_pointM_pwm (0..255), pwmN_enable
///   (1 = write curve to firmware, 2 = back to the default of the current platform profile, 3 = reload the factory
///   default curve of the current profile into the attributes without touching the firmware).
/// N: 1 = CPU, 2 = GPU, 3 = mid fan. Writing a point disables the curve until pwmN_enable is written again.
///
/// The Windows facade works on 16 byte buffers: 8 temperatures followed by 8 fan percentages (0..100). The kernel
/// only exposes the curve of the *current* profile, so curves of other profiles are served from a snapshot taken
/// while that profile was active.
/// </summary>
internal sealed class FanCurveAccess
{
    public const int Points = 8;

    private readonly SysfsRoot root;
    private readonly Func<string?> hwmonDir;
    private readonly Func<int> currentFanMode;
    private readonly object gate = new();

    // key = fan * 8 + fanMode (ASUS swapped numbering 0 default / 1 silent / 2 overboost)
    private readonly Dictionary<int, byte[]> snapshots = new();
    private readonly HashSet<int> custom = new();
    private int lastMode = -1;

    public FanCurveAccess(SysfsRoot root, Func<string?> hwmonDir, Func<int> currentFanMode)
    {
        this.root = root;
        this.hwmonDir = hwmonDir;
        this.currentFanMode = currentFanMode;
    }

    /// <summary>fan 1..3 present in sysfs?</summary>
    public bool IsPresent(int pwmIndex)
    {
        string? dir = hwmonDir();
        return dir is not null && root.FileExists($"{dir}/pwm{pwmIndex}_enable")
               && root.FileExists($"{dir}/pwm{pwmIndex}_auto_point1_temp");
    }

    public static int PwmFromPercent(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        // the kernel converts back with 100 * pwm / 255 (floor): ceil keeps the percentage stable
        return (int)Math.Ceiling(percent * 255 / 100.0);
    }

    public static int PercentFromPwm(int pwm) => (int)Math.Round(Math.Clamp(pwm, 0, 255) * 100 / 255.0);

    /// <summary>Current content of the sysfs curve as 16 byte Windows buffer, null if unreadable.</summary>
    public byte[]? ReadLive(int pwmIndex)
    {
        string? dir = hwmonDir();
        if (dir is null) return null;

        var curve = new byte[16];
        for (int i = 0; i < Points; i++)
        {
            int? temp = root.ReadInt($"{dir}/pwm{pwmIndex}_auto_point{i + 1}_temp");
            int? pwm = root.ReadInt($"{dir}/pwm{pwmIndex}_auto_point{i + 1}_pwm");
            if (temp is null || pwm is null) return null;
            curve[i] = (byte)Math.Clamp(temp.Value, 0, 255);
            curve[i + 8] = (byte)PercentFromPwm(pwm.Value);
        }
        return curve;
    }

    /// <summary>Windows "get curve for mode" semantics (see class remarks).</summary>
    public byte[]? Read(int pwmIndex, int fanMode)
    {
        lock (gate)
        {
            int current = currentFanMode();
            ObserveMode(current);

            int key = pwmIndex * 8 + fanMode;

            if (fanMode != current)
                return snapshots.TryGetValue(key, out byte[]? other) ? (byte[])other.Clone() : null;

            // custom curve written by us for the active profile: the attributes are authoritative
            if (custom.Contains(pwmIndex)) return ReadLive(pwmIndex);

            if (snapshots.TryGetValue(key, out byte[]? known)) return (byte[])known.Clone();

            // make sure the attributes hold the factory curve of the active profile, not the one of a previous profile
            string? hwmon = hwmonDir();
            if (hwmon is not null) root.TryWrite($"{hwmon}/pwm{pwmIndex}_enable", "3");

            byte[]? factory = ReadLive(pwmIndex);
            if (factory is not null) snapshots[key] = (byte[])factory.Clone();
            return factory;
        }
    }

    /// <summary>Writes all 8 points and enables the curve. Returns false when any write failed.</summary>
    public bool Write(int pwmIndex, byte[] curve)
    {
        string? dir = hwmonDir();
        if (dir is null || curve.Length != 16) return false;

        lock (gate)
        {
            ObserveMode(currentFanMode());

            for (int i = 0; i < Points; i++)
            {
                if (!root.TryWrite($"{dir}/pwm{pwmIndex}_auto_point{i + 1}_temp", curve[i].ToString())) return false;
                if (!root.TryWrite($"{dir}/pwm{pwmIndex}_auto_point{i + 1}_pwm", PwmFromPercent(curve[i + 8]).ToString())) return false;
            }

            if (!root.TryWrite($"{dir}/pwm{pwmIndex}_enable", "1")) return false;
            custom.Add(pwmIndex);
            return true;
        }
    }

    /// <summary>pwmN_enable = 2: firmware default of the current profile.</summary>
    public bool Reset(int pwmIndex)
    {
        string? dir = hwmonDir();
        if (dir is null) return false;
        lock (gate)
        {
            custom.Remove(pwmIndex);
            return root.TryWrite($"{dir}/pwm{pwmIndex}_enable", "2");
        }
    }

    private void ObserveMode(int mode)
    {
        if (mode == lastMode) return;
        // a profile switch makes the kernel drop the custom curves (enabled=false) and the firmware reload defaults
        if (lastMode != -1) custom.Clear();
        lastMode = mode;
    }
}
