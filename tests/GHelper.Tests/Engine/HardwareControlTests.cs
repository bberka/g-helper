using GHelper.Battery;
using GHelper.Fan;
using GHelper.Platform;
using Xunit;

namespace GHelper.Tests.Engine;

public class HardwareControlTests : EngineTestBase
{
    public HardwareControlTests(EngineFixture fixture) : base(fixture)
    {
        // HardwareControl throttles with static timestamps
        TestStatics.Set(typeof(HardwareControl), "lastUpdate", 0L);
        TestStatics.Set(typeof(HardwareControl), "_lastBatteryRead", 0L);
        HardwareControl.fullCapacity = null;
        HardwareControl.designCapacity = null;
        HardwareControl.chargeCapacity = null;
        HardwareControl.batteryCharge = null;
        HardwareControl.batteryRate = 0;
        HardwareControl.batteryHealth = -1;
        HardwareControl.chargeWatt = false;
        HardwareControl.GpuControl = null;
        FanSensorControl.fanRpm = true;
        BatteryControl.chargeFull = false;
    }

    private static BatteryState Battery(double percent, double? rate = null, double? design = null, double? full = null) =>
        new(Present: true, Charging: false, OnAc: false, Percent: percent, DischargeRateW: rate,
            DesignCapacityWh: design, FullChargeCapacityWh: full, Voltage: 15.2);

    [Fact]
    public void CpuTempPrefersAcpi()
    {
        Asus.Values[(uint)AsusACPI.Temp_CPU] = 55;
        P.SensorService.CpuTemp = 70;

        Assert.Equal(55, HardwareControl.GetCPUTemp());
    }

    [Fact]
    public void CpuTempFallsBackToOsThermalZone()
    {
        P.SensorService.CpuTemp = 61.5f;

        Assert.Equal(61.5f, HardwareControl.GetCPUTemp());
    }

    [Fact]
    public void CpuTempIsCachedForTwoSeconds()
    {
        Asus.Values[(uint)AsusACPI.Temp_CPU] = 55;
        Assert.Equal(55, HardwareControl.GetCPUTemp());

        Asus.Values[(uint)AsusACPI.Temp_CPU] = 80;
        Assert.Equal(55, HardwareControl.GetCPUTemp());
    }

    [Fact]
    public void GpuTempComesFromGpuControlThenAcpi()
    {
        HardwareControl.GpuControl = new FakeGpuControl { Temp = 70 };
        Assert.Equal(70, HardwareControl.GetGPUTemp());

        HardwareControl.GpuControl = new FakeGpuControl { Temp = null };
        Asus.Values[(uint)AsusACPI.Temp_GPU] = 65;
        Assert.Equal(65, HardwareControl.GetGPUTemp());

        HardwareControl.GpuControl = new FakeGpuControl { Temp = 130 };
        Asus.Values[(uint)AsusACPI.Temp_GPU] = 0;
        Assert.Null(HardwareControl.GetGPUTemp());
    }

    [Fact]
    public void ReadSensorsFormatsFansAndFallsBackToHwmon()
    {
        Asus.Values[AsusACPI.CPU_Fan] = 45;
        P.SensorService.FanRpm = 3000;   // GPU / mid are not exposed through ASUS

        HardwareControl.ReadSensors();

        Assert.Equal("4500RPM", HardwareControl.cpuFan);
        Assert.Equal("3000RPM", HardwareControl.gpuFan);
        Assert.Equal("3000RPM", HardwareControl.midFan);

        P.SensorService.FanRpm = null;
        HardwareControl.ReadSensors();
        Assert.Null(HardwareControl.gpuFan);
    }

    [Fact]
    public void BatteryStateMapsToLegacyUnits()
    {
        P.SensorService.Battery = Battery(50, rate: -12.5, design: 80, full: 76);

        HardwareControl.ReadBatteryState();

        Assert.Equal(76000m, HardwareControl.fullCapacity);
        Assert.Equal(38000m, HardwareControl.chargeCapacity);
        Assert.Equal(50m, HardwareControl.batteryCapacity);
        Assert.Equal(-12.5m, HardwareControl.batteryRate);
        Assert.StartsWith("50", HardwareControl.batteryCharge);
        Assert.EndsWith("%", HardwareControl.batteryCharge);
    }

    [Fact]
    public void BatteryChargeCanBeShownInWattHours()
    {
        P.SensorService.Battery = Battery(50, full: 76);
        HardwareControl.chargeWatt = true;

        HardwareControl.ReadBatteryState();

        Assert.EndsWith("Wh", HardwareControl.batteryCharge);
        Assert.StartsWith("38", HardwareControl.batteryCharge);
        Assert.Equal(1, AppConfig.Get("charge_watt"));
    }

    [Fact]
    public void BatteryWithoutEnergyValuesShowsPercent()
    {
        P.SensorService.Battery = Battery(42);

        HardwareControl.ReadBatteryState();

        Assert.StartsWith("42", HardwareControl.batteryCharge);
        Assert.EndsWith("%", HardwareControl.batteryCharge);
        Assert.Equal(42m, HardwareControl.batteryCapacity);
    }

    [Fact]
    public void FullBatteryClearsChargeFullFlag()
    {
        BatteryControl.chargeFull = true;
        P.SensorService.Battery = Battery(100, full: 76);

        HardwareControl.ReadBatteryState();

        Assert.False(BatteryControl.chargeFull);
    }

    [Fact]
    public void BatteryHealthIsFullOverDesignCapacity()
    {
        P.SensorService.Battery = Battery(60, design: 80, full: 76);

        HardwareControl.RefreshBatteryHealth();

        Assert.Equal(80000m, HardwareControl.designCapacity);
        Assert.Equal(95m, HardwareControl.batteryHealth);
    }

    [Fact]
    public void BatteryHealthUnknownWithoutDesignCapacity()
    {
        P.SensorService.Battery = null;

        HardwareControl.RefreshBatteryHealth();

        Assert.Equal(-1m, HardwareControl.batteryHealth);
    }

    [Fact]
    public void BatteryPercentageComesFromSensors()
    {
        P.SensorService.Battery = Battery(77);
        Assert.Equal(77, HardwareControl.GetBatteryChargePercentage());

        P.SensorService.Battery = null;
        Assert.Equal(0, HardwareControl.GetBatteryChargePercentage());
    }

    [Fact]
    public void OverlayReadHonoursEnabledGroups()
    {
        HardwareControl.GpuControl = new FakeGpuControl { Temp = 66, Use = 21, Power = 55, Vram = (2048, 8192) };
        Asus.Values[AsusACPI.CPU_Fan] = 40;
        P.SensorService.CpuUsage = 33.4f;
        P.SensorService.CpuPower = 18.5f;
        P.SensorService.Ram = (40, 8000);
        P.SensorService.Battery = Battery(50, rate: 10, full: 76);

        HardwareControl.readFans = true;
        HardwareControl.readUsage = true;
        HardwareControl.readMemory = true;
        HardwareControl.readPower = true;
        HardwareControl.readBattery = true;
        try
        {
            HardwareControl.ReadSensorsOverlay();

            Assert.Equal(4000, HardwareControl.cpuFanRPM);
            Assert.Equal(33, HardwareControl.cpuUsage);
            Assert.Equal(21, HardwareControl.gpuUsage);
            Assert.Equal(40, HardwareControl.ramUsage);
            Assert.Equal(8000, HardwareControl.ramUsedMb);
            Assert.Equal(25, HardwareControl.vramUsage);
            Assert.Equal(2048, HardwareControl.vramUsedMb);
            Assert.Equal(18.5f, HardwareControl.cpuPower);
            Assert.Equal(55f, HardwareControl.gpuPower);
            Assert.StartsWith("50", HardwareControl.batteryCharge);

            HardwareControl.readFans = false;
            HardwareControl.readUsage = false;
            HardwareControl.readMemory = false;
            HardwareControl.readPower = false;
            HardwareControl.readBattery = false;
            HardwareControl.ReadSensorsOverlay();

            Assert.Null(HardwareControl.cpuFanRPM);
            Assert.Null(HardwareControl.cpuUsage);
            Assert.Null(HardwareControl.ramUsage);
            Assert.Null(HardwareControl.vramUsage);
            Assert.Null(HardwareControl.cpuPower);
            Assert.Null(HardwareControl.gpuPower);
        }
        finally
        {
            HardwareControl.readFans = HardwareControl.readUsage = HardwareControl.readMemory = HardwareControl.readPower = HardwareControl.readBattery = false;
        }
    }

    [Fact]
    public void CpuUsageIsClampedAndRounded()
    {
        P.SensorService.CpuUsage = 99.7f;
        Assert.Equal(100, HardwareControl.GetCPUUsage());
        P.SensorService.CpuUsage = null;
        Assert.Null(HardwareControl.GetCPUUsage());
    }

    [Fact]
    public void CpuPowerCounterResetIsForwarded()
    {
        int before = P.SensorService.ResetCalls;
        HardwareControl.ResetCPUPowerCounter();
        Assert.Equal(before + 1, P.SensorService.ResetCalls);
    }

    [Fact]
    public void RecreateGpuControlUsesProviderAndLogsMissingGpu()
    {
        var gpu = new FakeGpuControl { IsNvidia = true, FullName = "NVIDIA RTX" };
        P.GpuProvider.Discrete = gpu;

        HardwareControl.RecreateGpuControl();
        Assert.Same(gpu, HardwareControl.GpuControl);
        Assert.Null(HardwareControl.GetNvidiaGpuControl());   // the fake is not the NVML based NvidiaGpuControl

        // GPU disappears (switched to eco): old control disposed and native libraries unloaded
        P.GpuProvider.Discrete = null;
        HardwareControl.RecreateGpuControl();

        Assert.Null(HardwareControl.GpuControl);
        Assert.True(gpu.Disposed);
        Assert.Equal(1, P.GpuProvider.Unloads);
    }

    [Fact]
    public void InvalidGpuIsDiscarded()
    {
        var gpu = new FakeGpuControl { IsValid = false };
        P.GpuProvider.Discrete = gpu;

        HardwareControl.RecreateGpuControl();

        Assert.Null(HardwareControl.GpuControl);
        Assert.True(gpu.Disposed);
    }

    [Fact]
    public void KillGpuAppsStopsLaunchersAndGpuClients()
    {
        var gpu = new FakeGpuControl();
        HardwareControl.GpuControl = gpu;
        AppConfig.Set("kill_gpu_apps", 1);
        try
        {
            HardwareControl.KillGPUApps();
        }
        finally
        {
            CleanConfig("kill_gpu_apps");
        }

        Assert.Contains("Kill:EADesktop", P.ProcessService.Calls);
        Assert.Contains("Kill:epicgameslauncher", P.ProcessService.Calls);
        Assert.True(gpu.Killed);
    }

    [Fact]
    public void IsUsedGpuNeedsTwoConsecutiveReadings()
    {
        HardwareControl.GpuControl = new FakeGpuControl { Use = 5 };
        Assert.False(HardwareControl.IsUsedGPU());

        HardwareControl.GpuControl = new FakeGpuControl { Use = 50 };
        Assert.True(HardwareControl.IsUsedGPU());
    }

    [Fact]
    public void SensorReadsAreSafeWithoutAcpi()
    {
        Program.acpi = default!;
        HardwareControl.ReadSensors();
        HardwareControl.ReadSensorsOverlay();
        Assert.Equal(-1, HardwareControl.gpuUse);
    }
}
