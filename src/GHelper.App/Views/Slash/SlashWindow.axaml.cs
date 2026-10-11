using GHelper.AnimeMatrix;
using GHelper.App.Services;
using GHelper.Properties;

namespace GHelper.App.Views;

/// <summary>
/// Slash lighting settings (legacy Slash form): power gating, pattern interval, boot / sleep / low battery
/// animations and the dim-on-low-battery level. Flags are read from and written to the device records directly.
/// </summary>
public partial class SlashWindow : SideWindow
{
    private static readonly int[] DimLevels = { 10, 20, 30, 40, 50, 100 };

    private readonly AniMatrixControl _control;
    private bool _loading = true;

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void RegisterWindow() =>
        WindowRegistry.Register("slash", _ => new SlashWindow(Program.matrixControl ?? AniMatrixControl.Instance));

    public SlashWindow() : this(Program.matrixControl ?? AniMatrixControl.Instance)
    {
    }

    public SlashWindow(int arg) : this(Program.matrixControl ?? AniMatrixControl.Instance)
    {
    }

    internal SlashWindow(AniMatrixControl control)
    {
        _control = control;
        InitializeComponent();

        LabelPower.Text = Strings.Power;
        LabelAnimations.Text = Strings.Animations;
        CheckAutoOff.Content = Strings.TurnOffOnBattery;
        CheckLidOff.Content = Strings.DisableOnLidClose;
        CheckBoot.Content = Strings.SlashBootAnimation;
        CheckSleepAnimation.Content = Strings.SlashSleepAnimation;
        CheckLowBattery.Content = Strings.SlashLowBatteryAlert;
        CheckBatteryLevel.Content = Strings.SlashBatteryIndicator;
        CheckPowerSaving.Content = Strings.SlashDimOnLowBattery;

        CheckAutoOff.IsChecked = AppConfig.Is("matrix_auto");
        CheckLidOff.IsChecked = AppConfig.Is("matrix_lid");

        SliderInterval.Value = Math.Min(AppConfig.Get("matrix_interval", 0), SliderInterval.Maximum);
        VisualiseInterval();

        ComboSleepPattern.ItemsSource = new[] { Strings.SystemDefault, Strings.AnimationPattern };
        ComboSleepPattern.SelectedIndex = 0;

        ComboDim.ItemsSource = DimLevels.Select(level => level == 100 ? Strings.Always : $"{level} %").ToArray();
        ComboDim.SelectedIndex = Math.Max(0, Array.IndexOf(DimLevels, AppConfig.Get("slash_dim", 20)));

        LoadFlags();

        CheckAutoOff.IsCheckedChanged += CheckAutoOff_Changed;
        CheckLidOff.IsCheckedChanged += CheckLidOff_Changed;

        SliderInterval.ValueChanged += (_, _) => VisualiseInterval();
        SliderInterval.ValueCommitted += SliderInterval_Commit;

        CheckBoot.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            bool on = CheckBoot.IsChecked == true;
            SetFlag(0xA0, on);
            SetFlag(0xA4, on);
        };
        CheckSleepAnimation.IsCheckedChanged += (_, _) =>
        {
            if (!_loading) SetFlag(0xA1, CheckSleepAnimation.IsChecked == true);
        };
        ComboSleepPattern.SelectionChanged += (_, _) =>
        {
            if (!_loading) ApplySleepPattern();
        };
        CheckLowBattery.IsCheckedChanged += (_, _) =>
        {
            if (!_loading) SetFlag(0xA2, CheckLowBattery.IsChecked == true);
        };
        CheckBatteryLevel.IsCheckedChanged += CheckBatteryLevel_Changed;
        CheckPowerSaving.IsCheckedChanged += CheckPowerSaving_Changed;
        ComboDim.SelectionChanged += ComboDim_Changed;

        _loading = false;
    }

    private SlashDevice? Slash => _control.deviceSlash;

    private void LoadFlags()
    {
        var slash = Slash;
        if (slash is null) return;

        try
        {
            CheckBoot.IsChecked = slash.GetFlag(0xA0);

            var sleep = slash.GetRecord(0xA1);
            if (sleep is not null && sleep.Length > 8)
            {
                CheckSleepAnimation.IsChecked = sleep[8] == 0x01;
                ComboSleepPattern.SelectedIndex = sleep[6] == 0x00 ? 0 : 1;
            }

            CheckLowBattery.IsChecked = slash.GetFlag(0xA2);
            CheckBatteryLevel.IsChecked = slash.GetFlag(0xA3);
            CheckPowerSaving.IsChecked = slash.GetFlag(0xA8);
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }
    }

    private void SetFlag(byte region, bool status)
    {
        try
        {
            Slash?.SetFlag(region, status);
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }
    }

    private void CheckAutoOff_Changed(object? sender, EventArgs e)
    {
        if (_loading) return;
        bool off = CheckAutoOff.IsChecked == true;

        AppConfig.Set("matrix_auto", off ? 1 : 0);
        try
        {
            Slash?.SetLightingOnBattery(!off);
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }
    }

    private void CheckLidOff_Changed(object? sender, EventArgs e)
    {
        if (_loading) return;
        bool off = CheckLidOff.IsChecked == true;

        AppConfig.Set("matrix_lid", off ? 1 : 0);
        try
        {
            Slash?.SetLightingOnLidClose(!off);
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }
    }

    private void VisualiseInterval()
    {
        int value = (int)SliderInterval.Value;
        LabelInterval.Text = value == 0
            ? Strings.IntervalOff
            : string.Format(Strings.IntervalSeconds, value);
    }

    private void SliderInterval_Commit(object? sender, EventArgs e)
    {
        int value = (int)SliderInterval.Value;
        if (AppConfig.Get("matrix_interval", 0) == value) return;

        AppConfig.Set("matrix_interval", value);
        _control.SetDevice();
    }

    private void ApplySleepPattern()
    {
        try
        {
            byte pattern = ComboSleepPattern.SelectedIndex == 0
                ? (byte)0x00
                : SlashDevice.GetModeCode((SlashMode)AppConfig.Get("matrix_running", 0));

            Slash?.SetRecordByte(0xA1, 6, pattern);
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }
    }

    private void CheckBatteryLevel_Changed(object? sender, EventArgs e)
    {
        if (_loading) return;
        bool on = CheckBatteryLevel.IsChecked == true;

        try
        {
            Slash?.SetBatteryAnimation(on);
            Slash?.SetFlag(0xA5, on);
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }
    }

    private void CheckPowerSaving_Changed(object? sender, EventArgs e)
    {
        if (_loading) return;
        bool on = CheckPowerSaving.IsChecked == true;

        try
        {
            Slash?.SetPowerSaving(on, AppConfig.Get("slash_dim", 20));
            Slash?.SetFlag(0xA8, on);
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }
    }

    private void ComboDim_Changed(object? sender, EventArgs e)
    {
        if (_loading) return;

        int level = DimLevels[Math.Clamp(ComboDim.SelectedIndex, 0, DimLevels.Length - 1)];
        AppConfig.Set("slash_dim", level);

        try
        {
            Slash?.SetPowerSaving(CheckPowerSaving.IsChecked == true, level);
        }
        catch (Exception ex)
        {
            Logger.WriteLine(ex.Message);
        }
    }
}
