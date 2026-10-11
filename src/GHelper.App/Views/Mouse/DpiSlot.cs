using System.Windows.Input;
using Avalonia.Media;
using GHelper.App.ViewModels;

namespace GHelper.App.Views.Mouse;

/// <summary>
/// One of the four DPI profile buttons: a profile (dot in its colour, "DPI 2" + value), the "+" slot that adds a
/// profile, or hidden. Port of the legacy dpiButtons[] state set by VisualizeDPIButtons.
/// </summary>
public sealed class DpiSlot : ObservableObject
{
    private bool _isVisible;
    private string _text = "";
    private bool _activated;
    private bool _isAdd;
    private bool _canDelete;
    private Color? _dotColor;
    private Color _borderColor = Colors.Transparent;

    public DpiSlot(int index)
    {
        Index = index;
    }

    public int Index { get; }

    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }

    /// <summary>"DPI 2\n1600" or "+".</summary>
    public string Text { get => _text; set => Set(ref _text, value); }

    /// <summary>The selected DPI profile (coloured outline).</summary>
    public bool Activated { get => _activated; set => Set(ref _activated, value); }

    /// <summary>The "+" slot (secondary look, no dot).</summary>
    public bool IsAdd
    {
        get => _isAdd;
        set
        {
            if (Set(ref _isAdd, value)) Raise(nameof(Secondary));
        }
    }

    public bool Secondary => _isAdd;

    /// <summary>Hover shows a small X in the corner that removes the profile.</summary>
    public bool CanDelete { get => _canDelete; set => Set(ref _canDelete, value); }

    /// <summary>Colour of the dot in front of the text; null = no dot.</summary>
    public Color? DotColor
    {
        get => _dotColor;
        set
        {
            if (Set(ref _dotColor, value))
            {
                Raise(nameof(HasDot));
                Raise(nameof(DotBrush));
            }
        }
    }

    public bool HasDot => _dotColor is not null;

    /// <summary>Brush of the dot (bound by the template).</summary>
    public IBrush? DotBrush => _dotColor is { } c ? new SolidColorBrush(c) : null;

    /// <summary>Accent of the active outline (legacy BorderColor).</summary>
    public Color BorderColor { get => _borderColor; set => Set(ref _borderColor, value); }

    public ICommand? Command { get; set; }

    public ICommand? DeleteCommand { get; set; }
}
