using GHelper.Helpers;
using GHelper.Peripherals;
using GHelper.Properties;

namespace GHelper.App.Backend;

// Sensor refresh (legacy SettingsForm.RefreshSensors + sensorTimer), battery hover texts and window visibility handling.
public sealed partial class MainBackend
{
    private System.Timers.Timer? _sensorTimer;
    private bool _sensorsAlways;
    private bool _windowVisible;
    private long _lastRefresh;
    private long _lastBatteryRefresh;
    private volatile bool _batteryMouseOver;
    private volatile bool _batteryFullMouseOver;

    private void InitSensorTimer()
    {
        _sensorsAlways = AppConfig.Is("sensors_always");

        _sensorTimer = new System.Timers.Timer(Math.Max(250, AppConfig.Get("sensor_timer", 1000)));
        _sensorTimer.Elapsed += (_, _) => RefreshSensors();
        _sensorTimer.Enabled = _sensorsAlways;
    }

    /// <summary>
    /// Main window shown / hidden (legacy SettingsForm_VisibleChanged and the tail of SettingsToggle): the sensor timer
    /// only runs while the window is visible unless "sensors_always" is set.
    /// </summary>
    public void OnWindowVisibilityChanged(bool visible)
    {
        _windowVisible = visible;

        if (_sensorTimer is not null) _sensorTimer.Enabled = visible || _sensorsAlways;

        if (visible)
        {
            Parallel(() => PeripheralsProvider.RefreshBatteryForAllDevices(true));
            _update?.CheckForUpdates();
            RefreshSensors();
            RefreshGpu();
            PopDonateBadge();
        }
        else
        {
            MemoryHelper.TrimAfter();
        }
    }

    /// <summary>
    /// Legacy RefreshSensors: throttled to once per 2 s (6 s while hidden with "sensors_always"). Reads on a background
    /// thread and publishes text through the view model.
    /// </summary>
    public void RefreshSensors(bool force = false)
    {
        int throttle = (!_windowVisible && _sensorsAlways) ? 6000 : 2000;
        long now = Environment.TickCount64;

        lock (_queueLock)
        {
            if (!force && Math.Abs(now - _lastRefresh) < throttle) return;
            _lastRefresh = now;
        }

        Parallel(RefreshSensorsNow);
    }

    /// <summary>The synchronous body of <see cref="RefreshSensors"/>.</summary>
    public void RefreshSensorsNow()
    {
        try
        {
            HardwareControl.ReadSensors();
            if (_windowVisible) Parallel(PeripheralsProvider.RefreshBatteryForAllDevices);

            string cpuTemp = "", gpuTemp = "";
            string cpuFan = "", gpuFan = "", midFan = "";
            string battery = "", charge = "";

            if (HardwareControl.cpuTemp > 0)
                cpuTemp = ": " + TempHelper.FormatTemp((double)HardwareControl.cpuTemp);

            if (HardwareControl.batteryCapacity > 0)
                charge = Strings.BatteryCharge + ": " + HardwareControl.batteryCharge;

            if (HardwareControl.batteryRate < 0)
                battery = Strings.Discharging + ": " + Math.Round(-(decimal)HardwareControl.batteryRate, 1) + "W";
            else if (HardwareControl.batteryRate > 0)
                battery = Strings.Charging + ": " + Math.Round((decimal)HardwareControl.batteryRate, 1) + "W";

            if (HardwareControl.gpuTemp > 0)
                gpuTemp = ": " + TempHelper.FormatTemp((double)HardwareControl.gpuTemp);

            if (HardwareControl.cpuFan is not null) cpuFan = Strings.FanSpeed + ": " + HardwareControl.cpuFan;
            if (HardwareControl.gpuFan is not null) gpuFan = Strings.FanSpeed + ": " + HardwareControl.gpuFan;
            if (HardwareControl.midFan is not null) midFan = Strings.FanSpeed + ": " + HardwareControl.midFan;

            string trayTip = "CPU" + cpuTemp + " " + cpuFan;
            if (gpuTemp.Length > 0) trayTip += "\nGPU" + gpuTemp + " " + gpuFan;
            if (battery.Length > 0) trayTip += "\n" + battery;

            string cpuText = "CPU" + cpuTemp + "  " + cpuFan;
            string gpuText = "GPU" + gpuTemp + "  " + gpuFan;

            string? mid = null;
            if (HardwareControl.gpuFan is not null && AppConfig.NoGpu()) mid = "GPU" + gpuTemp + " " + gpuFan;
            if (HardwareControl.midFan is not null) mid = "Mid " + midFan;

            OnUi(() =>
            {
                bool keepCharge = _batteryMouseOver || _batteryFullMouseOver;
                _vm.VisualiseSensors(cpuText, gpuText, mid, battery, keepCharge ? null : charge);
                _vm.TrayToolTip = trayTip;
            });
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Sensors: " + ex.Message);
        }
    }

    // ---- hover texts of the battery row (legacy PanelBattery_MouseEnter / ButtonBatteryFull_MouseEnter) ----

    private void SetBatteryHover(bool enter)
    {
        _batteryMouseOver = enter;
        if (!enter)
        {
            RefreshSensors(true);
            return;
        }

        Parallel(() =>
        {
            // refresh the health value again only after 15 minutes
            long now = Environment.TickCount64;
            bool stale;
            lock (_queueLock)
            {
                stale = _lastBatteryRefresh == 0 || Math.Abs(now - _lastBatteryRefresh) > 15 * 60_000;
                if (stale) _lastBatteryRefresh = now;
            }

            if (stale)
            {
                try { HardwareControl.RefreshBatteryHealth(); }
                catch (Exception ex) { Logger.WriteLine("Battery health: " + ex.Message); }
            }

            if (_batteryMouseOver && HardwareControl.batteryHealth != -1)
            {
                string text = Strings.BatteryHealth + ": " + Math.Round(HardwareControl.batteryHealth, 1) + "%";
                OnUi(() => _vm.ChargeText = text);
            }
        });
    }

    private void SetBatteryFullHover(bool enter)
    {
        _batteryFullMouseOver = enter;
        if (enter) _vm.ChargeText = Strings.BatteryLimitFull;
        else RefreshSensors(true);
    }
}
