using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace GHelper.App.ViewModels;

/// <summary>
/// Everything the legacy code set on one RButton (Text, Visible, Enabled, Activated, BorderColor, BackColor).
/// Bound to <c>RButton.State</c>.
/// </summary>
public class ButtonState : ObservableObject
{
    private string _text = "";
    private bool _isVisible = true;
    private bool _isEnabled = true;
    private bool _activated;
    private Color _borderColor = Colors.Transparent;
    private Color? _fillColor;
    private string? _accessibleName;
    private string? _toolTip;
    private Bitmap? _icon;
    private int _badge;

    public ButtonState() { }

    public ButtonState(string text, bool visible = true)
    {
        _text = text;
        _isVisible = visible;
    }

    public string Text { get => _text; set => Set(ref _text, value); }
    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }
    public bool IsEnabled { get => _isEnabled; set => Set(ref _isEnabled, value); }

    /// <summary>"This is the active mode": coloured outline + tint (legacy RButton.Activated).</summary>
    public bool Activated { get => _activated; set => Set(ref _activated, value); }

    /// <summary>Accent colour used for the outline when <see cref="Activated"/> (legacy RButton.BorderColor).</summary>
    public Color BorderColor { get => _borderColor; set => Set(ref _borderColor, value); }

    /// <summary>Solid fill with light text (legacy "BackColor = colorEco; ForeColor = ControlLightLight" on flag buttons).</summary>
    public Color? FillColor { get => _fillColor; set => Set(ref _fillColor, value); }

    public string? AccessibleName { get => _accessibleName; set => Set(ref _accessibleName, value); }
    public string? ToolTip { get => _toolTip; set => Set(ref _toolTip, value); }

    /// <summary>Optional icon override (peripherals compose theirs at runtime). Null keeps the icon from XAML.</summary>
    public Bitmap? Icon { get => _icon; set => Set(ref _icon, value); }

    /// <summary>Badge number (Donate button), 0 = none.</summary>
    public int Badge { get => _badge; set => Set(ref _badge, value); }

    /// <summary>Raised for a Click; wired by <see cref="MainViewModel"/>.</summary>
    public ICommand? Command { get; set; }

    /// <summary>Raised for a right click (legacy: Overlay toggle, Donate menu).</summary>
    public ICommand? SecondaryCommand { get; set; }
}

/// <summary>Entry of a combo box (legacy BindingSource of KeyValuePair).</summary>
public sealed record ChoiceItem(int Key, string Text)
{
    public override string ToString() => Text;
}
