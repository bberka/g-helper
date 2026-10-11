using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GHelper.App.Controls;

namespace GHelper.App.Views.Fans;

/// <summary>
/// The shared <see cref="FanCurveChart"/> plus the one thing the legacy chart did that the base control does not:
/// a click on the Y axis (labels) raises <see cref="YAxisClicked"/> to switch between RPM and percent.
/// The plot margins mirror FanCurveChart.PlotArea (left 40, top 30, bottom 24).
/// </summary>
internal sealed class FanChartView : FanCurveChart
{
    private const double AxisWidth = 40;
    private const double TopMargin = 30;
    private const double BottomMargin = 24;

    protected override Type StyleKeyOverride => typeof(FanCurveChart);

    public event EventHandler? YAxisClicked;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this);
        bool onAxis = p.X >= 0 && p.X < AxisWidth && p.Y >= TopMargin - 6 && p.Y <= Bounds.Height - BottomMargin + 6;

        base.OnPointerPressed(e);

        if (onAxis && !e.Handled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            YAxisClicked?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
