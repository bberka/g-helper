using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;

namespace GHelper.App.Controls;

/// <summary>Button with a small numeric badge in the top right (legacy RBadgeButton: the Donate button).</summary>
public class RBadgeButton : RButton
{
    private static readonly double[] PopTargets = { 1.25, 1, 1.2, 1 };

    public static readonly StyledProperty<int> BadgeProperty =
        AvaloniaProperty.Register<RBadgeButton, int>(nameof(Badge));

    private double _pop = 1;
    private int _popStep;
    private DispatcherTimer? _timer;

    static RBadgeButton()
    {
        AffectsRender<RBadgeButton>(BadgeProperty);
    }

    public int Badge
    {
        get => GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    /// <summary>Set the badge with the "pop" animation (legacy RBadgeButton.Pop).</summary>
    public void Pop(int value)
    {
        if (Badge == 0) _pop = 0;
        Badge = value;
        _popStep = 0;
        _timer ??= new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        double target = PopTargets[_popStep];
        _pop += (target - _pop) * 0.3;
        if (Math.Abs(target - _pop) < 0.03)
        {
            _pop = target;
            if (++_popStep == PopTargets.Length)
            {
                _popStep = 0;
                _timer?.Stop();
            }
        }
        InvalidateVisual();
    }

    protected override void SyncState(string? property)
    {
        base.SyncState(property);
        if (State is { } st && (property is null || property == nameof(ViewModels.ButtonState.Badge)))
        {
            if (property is not null && st.Badge > Badge && Badge == 0) Pop(st.Badge);
            else Badge = st.Badge;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        int badge = Badge;
        if (badge <= 0) return;

        double radius = 7 * _pop;
        var center = new Point(Bounds.Width - Bounds.Height / 2, Bounds.Height / 2);
        var accent = BorderColor.A > 0 ? BorderColor : Colors.Red;
        ctx.DrawEllipse(new SolidColorBrush(accent), null, center, radius, radius);

        var text = new FormattedText(badge.ToString(), System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyle.Normal, FontWeight.Bold),
            Math.Max(1, radius * 1.3), Brushes.White);
        ctx.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }
}
