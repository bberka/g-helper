using Avalonia;

namespace GHelper.App.Theme;

/// <summary>
/// Control size / spacing design tokens, in DIPs. One source for the styles in Controls.axaml
/// (<c>{x:Static t:UiMetrics.ControlHeight}</c>), the views and the layout tests, so a button, a combo box, a text box and a
/// numeric box placed in the same row are always the same height.
/// </summary>
/// <remarks>
/// Legacy WinForms design at 192 dpi: combo 40 px, button 48 px, screen-mode button 72 px, mode buttons 120 px (= 20 / 24 / 36 /
/// 60 DIP). The port uses 28 DIP for every single-line form control (usable touch/mouse target, still dense) and keeps the
/// big icon buttons of the main window on their own scale.
/// </remarks>
public static class UiMetrics
{
    /// <summary>Height of every single-line form control: RButton, RComboBox, RTextBox, RNumericUpDown, RColorButton.</summary>
    public const double ControlHeight = 28;

    /// <summary>Compact variant (<c>Classes="compact"</c>) for dense grids; identical for all control types.</summary>
    public const double CompactControlHeight = 24;

    /// <summary>Large action buttons of the main window (screen mode, Ally strip).</summary>
    public const double LargeButtonHeight = 36;

    /// <summary>Icon-over-text buttons (performance / GPU mode).</summary>
    public const double ModeButtonHeight = 60;

    /// <summary>Small status flags (Fn-Lock, Overlay, Energy Saver ...).</summary>
    public const double FlagHeight = 20;

    /// <summary>Gutter between the buttons / combo boxes of one row = 2 * <see cref="GridMargin"/>.</summary>
    public const double GridMargin = 2;

    /// <summary>Height of a UniformGrid row holding <see cref="ControlHeight"/> controls with <see cref="GridMargin"/>.</summary>
    public const double ControlRowHeight = ControlHeight + 2 * GridMargin;

    /// <summary>Height of a UniformGrid row holding <see cref="LargeButtonHeight"/> buttons.</summary>
    public const double LargeRowHeight = LargeButtonHeight + 2 * GridMargin;

    /// <summary>Height of a UniformGrid row holding <see cref="ModeButtonHeight"/> buttons.</summary>
    public const double ModeRowHeight = ModeButtonHeight + 2 * GridMargin;

    /// <summary>Vertical gap above and below a row of controls (minimum gap between two rows = 2 * this).</summary>
    public const double RowGap = 2;

    /// <summary>Gap between two sections (header of the next section included).</summary>
    public const double SectionGap = 12;

    /// <summary>Height of a section header row (icon + bold title).</summary>
    public const double HeaderHeight = 26;

    /// <summary>Section / row icon size.</summary>
    public const double IconSize = 16;

    /// <summary>Corner radius of buttons and input fields.</summary>
    public const double Radius = 3;

    /// <summary>Horizontal text padding of input fields (combo, text, numeric).</summary>
    public const double FieldPaddingX = 8;

    /// <summary>Side of the check box square.</summary>
    public const double CheckBoxSize = 18;

    /// <summary>Minimum height of one check box row (box + text); check boxes are centred in it.</summary>
    public const double CheckRowHeight = 24;

    /// <summary>Gap between the check box square and its text.</summary>
    public const double CheckTextGap = 8;

    public static readonly Thickness SectionIconMargin = new(4, 0, 6, 0);
    public static readonly Thickness FieldPadding = new(FieldPaddingX, 0);
    public static readonly Thickness ComboPadding = new(FieldPaddingX, 0, 4, 0);
    public static readonly Thickness ButtonPadding = new(8, 0);
    public static readonly Thickness CheckBoxContentPadding = new(CheckTextGap, 0, 0, 0);
    public static readonly Thickness RowMargin = new(0, RowGap);
    public static readonly Thickness GridItemMargin = new(GridMargin);
    public static readonly CornerRadius CornerRadiusAll = new(Radius);
}
