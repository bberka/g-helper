using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GHelper.App.Theme;
using GHelper.App.ViewModels;

namespace GHelper.App.Controls;

/// <summary>
/// Flat rounded button (port of app/UI/RButton.cs). The fill comes from <see cref="TemplatedControl.Background"/>
/// (theme: ButtonMain, or ButtonSecond when <see cref="Secondary"/>), the rest border from
/// <see cref="TemplatedControl.BorderBrush"/> and the "selected mode" outline/tint from <see cref="BorderColor"/>
/// when <see cref="Activated"/> is set.
/// </summary>
public class RButton : Button
{
    private const double HoverShift = 0.04;
    private const double ActiveTopLighten = 0.15;
    private const double RestTopLighten = 0.1;
    private const byte ActiveTintAlpha = 32;
    private const double ActiveTintEnd = 0.20;

    /// <summary>Thickness of the "activated" outline in DIPs (legacy: 5px design size at 192 dpi, 3px at 96).</summary>
    public const double ActiveBorderThickness = 3;

    public static readonly StyledProperty<bool> ActivatedProperty =
        AvaloniaProperty.Register<RButton, bool>(nameof(Activated));

    public static readonly StyledProperty<Color> BorderColorProperty =
        AvaloniaProperty.Register<RButton, Color>(nameof(BorderColor), Colors.Transparent);

    public static readonly StyledProperty<bool> SecondaryProperty =
        AvaloniaProperty.Register<RButton, bool>(nameof(Secondary));

    public static readonly StyledProperty<bool> BorderlessProperty =
        AvaloniaProperty.Register<RButton, bool>(nameof(Borderless));

    public static readonly StyledProperty<Bitmap?> IconProperty =
        AvaloniaProperty.Register<RButton, Bitmap?>(nameof(Icon));

    /// <summary>Top: image above the text (performance / GPU buttons). Left: image before the text.</summary>
    public static readonly StyledProperty<Orientation> IconOrientationProperty =
        AvaloniaProperty.Register<RButton, Orientation>(nameof(IconOrientation), Orientation.Horizontal);

    public static readonly StyledProperty<double> IconSpacingProperty =
        AvaloniaProperty.Register<RButton, double>(nameof(IconSpacing), 6);

    /// <summary>Solid fill override (flag buttons: Fn-Lock on, Overlay on ...). Text switches to light.</summary>
    public static readonly StyledProperty<Color?> FillColorProperty =
        AvaloniaProperty.Register<RButton, Color?>(nameof(FillColor));

    /// <summary>
    /// Optional view model of the button (text, visibility, enabled, activated, colours, command). One binding instead of
    /// six: the legacy code configured buttons through those properties.
    /// </summary>
    public static readonly StyledProperty<ButtonState?> StateProperty =
        AvaloniaProperty.Register<RButton, ButtonState?>(nameof(State));

    static RButton()
    {
        AffectsRender<RButton>(ActivatedProperty, BorderColorProperty, SecondaryProperty, BorderlessProperty,
            BackgroundProperty, BorderBrushProperty, CornerRadiusProperty, IsEnabledProperty, FillColorProperty);
    }

    public RButton()
    {
        Focusable = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        AppTheme.Changed += OnThemeChanged;
    }

    private void OnThemeChanged() => InvalidateVisual();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        AppTheme.Changed -= OnThemeChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AppTheme.Changed -= OnThemeChanged;
        AppTheme.Changed += OnThemeChanged;
    }

    public bool Activated { get => GetValue(ActivatedProperty); set => SetValue(ActivatedProperty, value); }
    public Color BorderColor { get => GetValue(BorderColorProperty); set => SetValue(BorderColorProperty, value); }
    public bool Secondary { get => GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }
    public bool Borderless { get => GetValue(BorderlessProperty); set => SetValue(BorderlessProperty, value); }
    public Bitmap? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public Orientation IconOrientation { get => GetValue(IconOrientationProperty); set => SetValue(IconOrientationProperty, value); }
    public double IconSpacing { get => GetValue(IconSpacingProperty); set => SetValue(IconSpacingProperty, value); }
    public Color? FillColor { get => GetValue(FillColorProperty); set => SetValue(FillColorProperty, value); }
    public ButtonState? State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }

    private ButtonState? _state;
    private System.Windows.Input.ICommand? _secondary;

    private void AttachState(ButtonState? state)
    {
        if (_state is not null) _state.PropertyChanged -= OnStatePropertyChanged;
        _state = state;
        if (state is null) return;
        state.PropertyChanged += OnStatePropertyChanged;
        SyncState(null);
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) SyncState(e.PropertyName);
        else Avalonia.Threading.Dispatcher.UIThread.Post(() => SyncState(e.PropertyName));
    }

    /// <summary>Copy state to the control. <paramref name="property"/> null = everything.</summary>
    protected virtual void SyncState(string? property)
    {
        var s = _state;
        if (s is null) return;
        bool all = property is null;
        if (all || property == nameof(ButtonState.Text)) Content = s.Text;
        if (all || property == nameof(ButtonState.IsVisible)) IsVisible = s.IsVisible;
        if (all || property == nameof(ButtonState.IsEnabled)) IsEnabled = s.IsEnabled;
        if (all || property == nameof(ButtonState.Activated)) Activated = s.Activated;
        if (all || property == nameof(ButtonState.BorderColor)) BorderColor = s.BorderColor;
        if (all || property == nameof(ButtonState.FillColor)) FillColor = s.FillColor;
        if (all || property == nameof(ButtonState.Command)) Command = s.Command;
        if (all || property == nameof(ButtonState.SecondaryCommand)) _secondary = s.SecondaryCommand;
        if (all || property == nameof(ButtonState.Icon)) { if (s.Icon is not null) Icon = s.Icon; }
        if (all || property == nameof(ButtonState.ToolTip)) ToolTip.SetTip(this, s.ToolTip);
        if (all || property == nameof(ButtonState.AccessibleName) || property == nameof(ButtonState.Text))
            AutomationProperties.SetName(this, s.AccessibleName ?? (s.Text is { Length: > 0 } t ? t : null));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Right && _secondary?.CanExecute(null) == true)
        {
            _secondary.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StateProperty)
            AttachState(change.GetNewValue<ButtonState?>());
        else if (change.Property == FillColorProperty)
            PseudoClasses.Set(":filled", FillColor is not null);
        else if (change.Property == SecondaryProperty)
            PseudoClasses.Set(":secondary", Secondary);
        else if (change.Property == ActivatedProperty)
            PseudoClasses.Set(":activated", Activated);
        else if (change.Property == IsPointerOverProperty || change.Property == IsPressedProperty)
            InvalidateVisual();
    }

    protected virtual bool IsDark => ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;

    public override void Render(DrawingContext ctx)
    {
        var rect = new Rect(Bounds.Size);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        double radius = CornerRadius.TopLeft;
        bool dark = IsDark;
        bool flat = AppTheme.Flat;

        Color back = FillColor ?? ChromeHelper.ColorOf(Background, Colors.Transparent);
        if (IsPressed) back = ChromeHelper.Shift(back, HoverShift * 2);
        else if (IsPointerOver && IsEnabled) back = ChromeHelper.Shift(back, HoverShift);

        // Legacy ButtonEnabled(): disabled buttons keep their colour with alpha 100/255.
        if (!IsEnabled) back = ChromeHelper.WithAlpha(back, back.A * 100.0 / 255);

        ctx.DrawRectangle(new SolidColorBrush(back), null, rect, radius, radius);

        Color accent = BorderColor;
        bool drawActive = Activated && !Borderless && accent.A > 0;
        bool drawRest = !Activated && !Borderless && IsEnabled && !flat;

        if (drawActive)
        {
            double t = ActiveBorderThickness;
            Rect inner = rect.Deflate(t);
            if (!flat && inner.Width > 0 && inner.Height > 0)
            {
                var tint = new Color[] { ChromeHelper.WithAlpha(accent, ActiveTintAlpha), ChromeHelper.WithAlpha(accent, 0) };
                // dark theme: tint fades from the top, light theme: from the bottom (legacy behaviour)
                var brush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, dark ? 0 : 1, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, dark ? 1 : 0, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(tint[0], 0),
                        new GradientStop(tint[1], ActiveTintEnd),
                        new GradientStop(tint[1], 1),
                    },
                };
                ctx.DrawRectangle(brush, null, inner, Math.Max(0, radius - 1), Math.Max(0, radius - 1));
            }

            // legacy DrawGradientBorder(..., PenAlignment.Outset) around the inset rect == a stroke filling the margin
            ChromeHelper.DrawGradientBorder(ctx, inner, accent, radius, t, dark, inset: false, topLighten: flat ? 0 : ActiveTopLighten);
        }
        else if (drawRest)
        {
            Color side = ChromeHelper.ColorOf(BorderBrush, Colors.Transparent);
            if (side.A > 0)
                ChromeHelper.DrawGradientBorder(ctx, rect, side, radius, 1, dark, inset: true, topLighten: RestTopLighten);
        }
    }
}
