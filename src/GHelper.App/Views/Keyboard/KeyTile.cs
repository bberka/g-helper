using Avalonia.Media;
using GHelper.App.ViewModels;

namespace GHelper.App.Views.Keyboard;

/// <summary>
/// One key of the per-key lighting grid. Position and size are in DIPs inside the grid; the border colour is the LED
/// colour (or the animated preview colour), the selected key gets a thicker border, a bound key an underlined legend
/// (legacy: FlatAppearance.BorderColor / BorderSize / Font underline of the key buttons).
/// </summary>
public sealed class KeyTile : ObservableObject
{
    private Color _borderColor = Colors.Gray;
    private bool _selected;
    private bool _bound;

    public KeyTile(int led, string name, string legend, double x, double y, double width, double height, ushort usage)
    {
        Led = led;
        Name = name;
        Legend = legend;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Usage = usage;
        ToolTip = $"{name} (LED {led})";
    }

    /// <summary>First LED id (grid position) of the key.</summary>
    public int Led { get; }

    /// <summary>Layout name ("Space", "Num 4", "M1").</summary>
    public string Name { get; }

    public string Legend { get; }

    public double X { get; }
    public double Y { get; }
    public double Width { get; set; }
    public double Height { get; }

    /// <summary>Wire code of the key for bindings, 0 = cannot be rebound.</summary>
    public ushort Usage { get; }

    /// <summary>Number of LEDs under a wide key that is drawn as one (the 3 parts of a split space bar).</summary>
    public int Span { get; set; } = 1;

    public string ToolTip { get; }

    public Color BorderColor { get => _borderColor; set => Set(ref _borderColor, value); }

    public bool Selected { get => _selected; set => Set(ref _selected, value); }

    /// <summary>A binding other than the factory one is active.</summary>
    public bool Bound { get => _bound; set => Set(ref _bound, value); }
}
