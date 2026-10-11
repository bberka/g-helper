using Avalonia.Media;
using GHelper.Peripherals.Keyboard;
using GHelper.USB;

namespace GHelper.App.Views.Keyboard;

/// <summary>
/// The software preview of the animated lighting modes on the key grid (legacy PreviewTimer_Tick): the border of every
/// key is coloured as the effect would light it. Pure maths, driven by a timer in the window.
/// </summary>
public sealed class KeyboardPreview
{
    private readonly Random _random;
    private float _phase;
    private float[] _glow = Array.Empty<float>();
    private Color[] _glowColor = Array.Empty<Color>();
    private float _rippleX, _rippleY, _rippleR;

    public KeyboardPreview(Random? random = null)
    {
        _random = random ?? new Random();
    }

    public float Phase => _phase;

    /// <summary>True for the modes that animate (everything but static and per key painting).</summary>
    public static bool IsAnimated(KeyboardLightingMode mode) =>
        mode is not KeyboardLightingMode.Static and not KeyboardLightingMode.Direct;

    /// <summary>Phase advance per tick (120 ms) for the animation speed.</summary>
    public static float Step(AuraSpeed speed) => speed switch
    {
        AuraSpeed.Slow => 0.02f,
        AuraSpeed.Fast => 0.09f,
        _ => 0.045f,
    };

    public void Reset(int keyCount)
    {
        _glow = new float[keyCount];
        _glowColor = new Color[keyCount];
        _phase = 0;
        _rippleR = 0;
    }

    /// <param name="centers">Centre of every key as a fraction (0..1) of the grid size.</param>
    /// <param name="output">Receives the colour of every key.</param>
    public void Tick(KeyboardLightingMode mode, AuraSpeed speed, Color c1, Color c2, Color background,
        IReadOnlyList<(float X, float Y)> centers, Color[] output)
    {
        if (_glow.Length != centers.Count) Reset(centers.Count);

        _phase = (_phase + Step(speed) + 1f) % 1f;
        Color c2eff = c2.R != 0 || c2.G != 0 || c2.B != 0 ? c2 : c1;

        bool glowing = mode is KeyboardLightingMode.Reactive or KeyboardLightingMode.StarryNight
            or KeyboardLightingMode.Current or KeyboardLightingMode.RainDrop;

        if (glowing && _glow.Length > 0)
        {
            for (int i = 0; i < _glow.Length; i++) _glow[i] = Math.Max(0, _glow[i] - 0.09f);

            int ignite = mode == KeyboardLightingMode.StarryNight ? 3 : 1;
            for (int i = 0; i < ignite; i++)
            {
                if (_random.NextDouble() < 0.7)
                {
                    int idx = _random.Next(_glow.Length);
                    _glow[idx] = 1f;
                    _glowColor[idx] = _random.Next(2) == 0 ? c1 : c2eff;
                }
            }
        }

        if (mode == KeyboardLightingMode.Ripple)
        {
            _rippleR += 0.06f;
            if (_rippleR > 1.3f)
            {
                _rippleR = 0;
                _rippleX = (float)_random.NextDouble();
                _rippleY = (float)_random.NextDouble();
            }
        }

        for (int i = 0; i < centers.Count && i < output.Length; i++)
        {
            float kx = centers[i].X, ky = centers[i].Y;
            output[i] = mode switch
            {
                KeyboardLightingMode.Breathing => Lerp(c2, c1, Tri(_phase)),
                KeyboardLightingMode.ColorCycle => Hsv(_phase),
                KeyboardLightingMode.Wave => Hsv(_phase + kx),
                KeyboardLightingMode.Quicksand => Hsv(ky - _phase),
                KeyboardLightingMode.Ripple => RippleColor(c1, kx, ky),
                _ when glowing => Lerp(background, _glowColor[i], _glow[i]),
                _ => c1,
            };
        }
    }

    private Color RippleColor(Color c1, float kx, float ky)
    {
        float dist = (float)Math.Sqrt((kx - _rippleX) * (kx - _rippleX) + (ky - _rippleY) * (ky - _rippleY));
        float band = Math.Abs(dist - _rippleR);
        return Dim(c1, band < 0.12f ? 0.15f + 0.85f * (1 - band / 0.12f) : 0.15f);
    }

    public static float Tri(float t) => t < 0.5f ? t * 2 : 2 - t * 2;

    public static Color Dim(Color c, float f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    public static Color Lerp(Color a, Color b, float t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public static Color Hsv(float hue)
    {
        float h = (hue % 1f + 1f) % 1f * 6f;
        int i = (int)h;
        float f = h - i;
        byte q = (byte)(255 * (1 - f)), t = (byte)(255 * f);
        return i switch
        {
            0 => Color.FromRgb(255, t, 0),
            1 => Color.FromRgb(q, 255, 0),
            2 => Color.FromRgb(0, 255, t),
            3 => Color.FromRgb(0, q, 255),
            4 => Color.FromRgb(t, 0, 255),
            _ => Color.FromRgb(255, 0, q),
        };
    }
}
