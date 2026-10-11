using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using GHelper.App.Controls;
using GHelper.App.Services;

namespace GHelper.App.Views.Fans;

/// <summary>
/// "Fans and Power" window (legacy Fans form). Layout in FansWindow.axaml, state and rules in
/// <see cref="FansViewModel"/>; this file wires the parts that are view concerns: slider ranges, chart callbacks,
/// "released" events that make Core apply the values, and the refresh requests coming from Core.
/// </summary>
public partial class FansWindow : SideWindow
{
    [ModuleInitializer]
    internal static void RegisterWindow() => WindowRegistry.Register("fans", arg => new FansWindow(arg));

    private readonly FansViewModel _vm;
    private AvaloniaUiHostSubscription? _subscription;

    /// <summary>Designer / loader constructor.</summary>
    public FansWindow() : this(0, null)
    {
    }

    /// <param name="tab">Legacy FansToggle(index): 0 CPU / power, 1 GPU, 2 advanced (undervolting).</param>
    public FansWindow(int tab) : this(tab, null)
    {
    }

    internal FansWindow(int tab, IFansBackend? backend)
    {
        InitializeComponent();

        _vm = new FansViewModel(backend ?? new CoreFansBackend());
        _vm.SelectTab(tab);

        ConfigureRanges();
        WireSliders();
        WireCharts();

        DataContext = _vm;

        foreach (var curve in _vm.Curves) PushPoints(curve);

        // legacy: Activated += VisualiseAdvanced (PawnIO may have been installed meanwhile)
        Activated += (_, _) => _vm.VisualiseAdvanced();

        Opened += (_, _) =>
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is not null)
                MaxHeight = Math.Max(300, screen.WorkingArea.Height / screen.Scaling);

            _subscription = AvaloniaUiHostSubscription.Create(_vm.OnAreaRefreshed);
        };

        Closed += (_, _) =>
        {
            _subscription?.Dispose();
            _subscription = null;
        };
    }

    internal FansViewModel ViewModel => _vm;

    private void ConfigureRanges()
    {
        void Range(SliderRow row, (int min, int max) range)
        {
            row.Minimum = range.min;
            row.Maximum = range.max;
        }

        Range(SliderTotal, _vm.PowerTotalRange);
        Range(SliderSlow, _vm.PowerTotalRange);
        Range(SliderFast, _vm.PowerTotalRange);
        Range(SliderCpu, _vm.PowerCpuRange);
        Range(SliderCrossLoad, _vm.CrossLoadRange);
        Range(SliderGpuToCpu, _vm.GpuToCpuRange);
        SliderGpuToCpu.Step = _vm.GpuToCpuStep;
        Range(SliderCpuTemp, _vm.CpuTempRange);

        Range(SliderGpuClock, _vm.GpuClockRange);
        Range(SliderGpuCore, _vm.GpuCoreRange);
        Range(SliderGpuMemory, _vm.GpuMemoryRange);
        Range(SliderGpuBoost, _vm.GpuBoostRange);
        Range(SliderGpuTemp, _vm.GpuTempRange);
        Range(SliderGpuPower, _vm.GpuPowerRange);

        Range(SliderUv, _vm.UvRange);
        Range(SliderIGpuUv, _vm.IGpuUvRange);
        Range(SliderTemp, _vm.TempRange);

        // the GPU power maximum is raised once nvidia-smi answered: the maximum must be in place before the value
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FansViewModel.GpuPowerMax))
                SliderGpuPower.Maximum = _vm.GpuPowerMax;
        };
    }

    private void WireSliders()
    {
        void Power(object? s, EventArgs e) => _vm.CommitPower();
        foreach (var row in new[] { SliderTotal, SliderSlow, SliderFast, SliderCpu, SliderCrossLoad, SliderGpuToCpu, SliderCpuTemp })
            row.ValueCommitted += Power;

        void Clocks(object? s, EventArgs e) => _vm.CommitGpuClocks();
        foreach (var row in new[] { SliderGpuClock, SliderGpuCore, SliderGpuMemory })
            row.ValueCommitted += Clocks;

        void GpuPower(object? s, EventArgs e) => _vm.CommitGpuPower();
        foreach (var row in new[] { SliderGpuBoost, SliderGpuTemp, SliderGpuPower })
            row.ValueCommitted += GpuPower;

        SliderHysteresisUp.ValueCommitted += (_, _) => _vm.CommitHysteresis();
        SliderHysteresisDown.ValueCommitted += (_, _) => _vm.CommitHysteresis();
    }

    private void WireCharts()
    {
        Wire(ChartCpu, _vm.Cpu);
        Wire(ChartGpu, _vm.Gpu);
        Wire(ChartMid, _vm.Mid);
        Wire(ChartXgm, _vm.Xgm);

        _vm.AxisChanged += () =>
        {
            foreach (var chart in Charts) chart.InvalidateVisual();
        };
    }

    private FanChartView[] Charts => new[] { ChartCpu, ChartGpu, ChartMid, ChartXgm };

    private void Wire(FanChartView chart, FanCurveModel model)
    {
        var device = model.Device;

        chart.YMin = 0;
        chart.YMax = FansViewModel.FanMaxPercent;
        chart.XMin = FansViewModel.TempMin;
        chart.XMax = FansViewModel.TempMax;
        chart.XInterval = 10;
        chart.YInterval = 10;

        chart.XLabel = _vm.AxisXLabel;
        chart.YLabel = v => _vm.AxisYLabel(device, v);
        chart.TipFormatter = p => _vm.TipText(device, p.X, p.Y);

        chart.CurveCommitted += (_, _) =>
            _vm.CommitCurve(device, chart.Points.Select(p => (p.X, p.Y)).ToArray());

        chart.HintChanged += (_, show) => _vm.FanDragHint(show);
        chart.YAxisClicked += (_, _) => _vm.ToggleFanUnits();

        model.PointsReloaded += () => PushPoints(model);
    }

    private void PushPoints(FanCurveModel model)
    {
        var chart = model.Device switch
        {
            AsusFan.GPU => ChartGpu,
            AsusFan.Mid => ChartMid,
            AsusFan.XGM => ChartXgm,
            _ => ChartCpu,
        };

        chart.SetPoints(model.Points);
    }
}

/// <summary>Subscribes a window to the host's coalesced Refresh(area) notifications (null host in tests / designer).</summary>
internal sealed class AvaloniaUiHostSubscription : IDisposable
{
    private readonly AvaloniaUiHost _host;
    private readonly Action<string> _handler;

    private AvaloniaUiHostSubscription(AvaloniaUiHost host, Action<string> handler)
    {
        _host = host;
        _handler = handler;
        host.AreaRefreshed += handler;
    }

    public static AvaloniaUiHostSubscription? Create(Action<string> handler) =>
        App.Instance?.Host is { } host ? new AvaloniaUiHostSubscription(host, handler) : null;

    public void Dispose() => _host.AreaRefreshed -= _handler;
}
