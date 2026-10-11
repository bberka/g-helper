using System.Drawing;

namespace GHelper.AnimeMatrix
{
    /// <summary>Same numeric values as System.Drawing.Drawing2D.InterpolationMode so that stored config values (matrix_quality) keep working.</summary>
    public enum InterpolationMode
    {
        Invalid = -1,
        Default = 0,
        Low = 1,
        High = 2,
        Bilinear = 3,
        Bicubic = 4,
        NearestNeighbor = 5,
        HighQualityBilinear = 6,
        HighQualityBicubic = 7,
    }

    /// <summary>
    /// Portable replacement for System.Drawing.Bitmap in the matrix / ambient code: 8 bit RGBA, row major, straight (not
    /// premultiplied) alpha, origin top-left. The UI layer converts its bitmaps (Avalonia/Skia) to and from this type.
    /// </summary>
    public sealed class PixelBuffer
    {
        public int Width { get; }
        public int Height { get; }

        /// <summary>Width * Height * 4 bytes, R G B A per pixel.</summary>
        public byte[] Data { get; }

        public PixelBuffer(int width, int height)
        {
            if (width < 0 || height < 0) throw new ArgumentOutOfRangeException(nameof(width));
            Width = width;
            Height = height;
            Data = new byte[width * height * 4];
        }

        public PixelBuffer(int width, int height, byte[] rgba)
        {
            if (width < 0 || height < 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (rgba.Length != width * height * 4) throw new ArgumentException("RGBA buffer has the wrong size", nameof(rgba));
            Width = width;
            Height = height;
            Data = rgba;
        }

        /// <summary>Convert from a BGRA buffer (Skia / WriteableBitmap / GDI order). Alpha is taken as is.</summary>
        public static PixelBuffer FromBgra(int width, int height, ReadOnlySpan<byte> bgra, int stride = 0)
        {
            if (stride <= 0) stride = width * 4;
            var buffer = new PixelBuffer(width, height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int s = y * stride + x * 4;
                    int d = (y * width + x) * 4;
                    buffer.Data[d] = bgra[s + 2];
                    buffer.Data[d + 1] = bgra[s + 1];
                    buffer.Data[d + 2] = bgra[s];
                    buffer.Data[d + 3] = bgra[s + 3];
                }
            }
            return buffer;
        }

        public Color GetPixel(int x, int y)
        {
            int i = (y * Width + x) * 4;
            return Color.FromArgb(Data[i + 3], Data[i], Data[i + 1], Data[i + 2]);
        }

        public void SetPixel(int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            int i = (y * Width + x) * 4;
            Data[i] = color.R;
            Data[i + 1] = color.G;
            Data[i + 2] = color.B;
            Data[i + 3] = color.A;
        }

        /// <summary>Legacy matrix brightness source: plain (R+G+B)/3, alpha ignored (what a 24bpp conversion of the GDI+ bitmap gave).</summary>
        public byte Gray(int x, int y)
        {
            int i = (y * Width + x) * 4;
            return (byte)((Data[i] + Data[i + 1] + Data[i + 2]) / 3);
        }

        /// <summary>FillRectangle equivalent: covers [x, x+w) x [y, y+h), clipped.</summary>
        public void FillRectangle(Color color, int x, int y, int w, int h)
        {
            for (int yy = Math.Max(0, y); yy < Math.Min(Height, y + h); yy++)
                for (int xx = Math.Max(0, x); xx < Math.Min(Width, x + w); xx++)
                    SetPixel(xx, yy, color);
        }

        /// <summary>DrawRectangle equivalent with a 1px pen: GDI+ covers x..x+w and y..y+h inclusive.</summary>
        public void DrawRectangle(Color color, int x, int y, int w, int h)
        {
            for (int xx = x; xx <= x + w; xx++)
            {
                SetPixel(xx, y, color);
                SetPixel(xx, y + h, color);
            }
            for (int yy = y; yy <= y + h; yy++)
            {
                SetPixel(x, yy, color);
                SetPixel(x + w, yy, color);
            }
        }

        /// <summary>Source-over copy of <paramref name="source"/> at 1:1 (legacy Graphics.DrawImage(image, x, y)).</summary>
        public void Draw(PixelBuffer source, int x, int y)
        {
            for (int sy = 0; sy < source.Height; sy++)
            {
                int dy = y + sy;
                if (dy < 0 || dy >= Height) continue;
                for (int sx = 0; sx < source.Width; sx++)
                {
                    int dx = x + sx;
                    if (dx < 0 || dx >= Width) continue;

                    int s = (sy * source.Width + sx) * 4;
                    int a = source.Data[s + 3];
                    if (a == 0) continue;

                    int d = (dy * Width + dx) * 4;
                    if (a == 255 || Data[d + 3] == 0)
                    {
                        Data[d] = source.Data[s];
                        Data[d + 1] = source.Data[s + 1];
                        Data[d + 2] = source.Data[s + 2];
                        Data[d + 3] = source.Data[s + 3];
                        continue;
                    }

                    // straight alpha source-over
                    int da = Data[d + 3];
                    int outA = a + da * (255 - a) / 255;
                    for (int c = 0; c < 3; c++)
                        Data[d + c] = (byte)((source.Data[s + c] * a + Data[d + c] * da * (255 - a) / 255) / Math.Max(1, outA));
                    Data[d + 3] = (byte)outA;
                }
            }
        }

        /// <summary>
        /// Scaled draw into the rectangle (destX, destY, destW, destH) (legacy Graphics.DrawImage with float destination).
        /// Pixels outside the rectangle stay untouched. Nearest neighbour for <see cref="InterpolationMode.NearestNeighbor"/>,
        /// otherwise bilinear when enlarging and area averaging when shrinking.
        /// </summary>
        public void DrawScaled(PixelBuffer source, float destX, float destY, float destW, float destH, InterpolationMode mode = InterpolationMode.Default)
        {
            if (source.Width == 0 || source.Height == 0 || destW <= 0 || destH <= 0) return;

            int x0 = Math.Max(0, (int)Math.Floor(destX));
            int y0 = Math.Max(0, (int)Math.Floor(destY));
            int x1 = Math.Min(Width, (int)Math.Ceiling(destX + destW));
            int y1 = Math.Min(Height, (int)Math.Ceiling(destY + destH));

            double scaleX = source.Width / (double)destW;
            double scaleY = source.Height / (double)destH;

            for (int y = y0; y < y1; y++)
            {
                double cy = y + 0.5;
                if (cy < destY || cy > destY + destH) continue;

                for (int x = x0; x < x1; x++)
                {
                    double cx = x + 0.5;
                    if (cx < destX || cx > destX + destW) continue;

                    double u = (cx - destX) * scaleX;
                    double v = (cy - destY) * scaleY;

                    (double r, double g, double b, double a) px;
                    if (mode == InterpolationMode.NearestNeighbor)
                        px = source.Sample(Math.Min(source.Width - 1, (int)u), Math.Min(source.Height - 1, (int)v));
                    else if (scaleX > 1 || scaleY > 1)
                        px = source.Average(u - scaleX / 2, v - scaleY / 2, u + scaleX / 2, v + scaleY / 2);
                    else
                        px = source.Bilinear(u - 0.5, v - 0.5);

                    int d = (y * Width + x) * 4;
                    Data[d] = (byte)Math.Clamp(Math.Round(px.r), 0, 255);
                    Data[d + 1] = (byte)Math.Clamp(Math.Round(px.g), 0, 255);
                    Data[d + 2] = (byte)Math.Clamp(Math.Round(px.b), 0, 255);
                    Data[d + 3] = (byte)Math.Clamp(Math.Round(px.a), 0, 255);
                }
            }
        }

        /// <summary>Area-averaged resize of the whole buffer (used by the ambient light 4x2 reduction).</summary>
        public PixelBuffer Resize(int width, int height)
        {
            var result = new PixelBuffer(width, height);
            if (Width == 0 || Height == 0 || width == 0 || height == 0) return result;
            result.DrawScaled(this, 0, 0, width, height, InterpolationMode.HighQualityBilinear);
            return result;
        }

        /// <summary>Mean colour of all pixels (legacy ColorUtils.GetDominantColor).</summary>
        public Color AverageColor()
        {
            long r = 0, g = 0, b = 0;
            int total = Width * Height;
            if (total == 0) return Color.Black;

            for (int i = 0; i < Data.Length; i += 4)
            {
                r += Data[i];
                g += Data[i + 1];
                b += Data[i + 2];
            }

            return Color.FromArgb((int)(r / total), (int)(g / total), (int)(b / total));
        }

        private (double r, double g, double b, double a) Sample(int x, int y)
        {
            int i = (y * Width + x) * 4;
            return (Data[i], Data[i + 1], Data[i + 2], Data[i + 3]);
        }

        // colour channels are weighted by alpha so that transparent pixels do not bleed black into their neighbours
        private (double r, double g, double b, double a) Bilinear(double x, double y)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            double fx = x - xi, fy = y - yi;

            double r = 0, g = 0, b = 0, a = 0, wsum = 0;
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                {
                    double w = (i == 0 ? 1 - fx : fx) * (j == 0 ? 1 - fy : fy);
                    if (w <= 0) continue;
                    var p = Sample(Math.Clamp(xi + i, 0, Width - 1), Math.Clamp(yi + j, 0, Height - 1));
                    double wa = w * p.a;
                    r += p.r * wa; g += p.g * wa; b += p.b * wa;
                    a += wa;
                    wsum += w;
                }

            if (a <= 0) return (0, 0, 0, 0);
            return (r / a, g / a, b / a, a / wsum);
        }

        private (double r, double g, double b, double a) Average(double left, double top, double right, double bottom)
        {
            int xs = Math.Clamp((int)Math.Floor(left), 0, Width - 1);
            int xe = Math.Clamp((int)Math.Ceiling(right) - 1, xs, Width - 1);
            int ys = Math.Clamp((int)Math.Floor(top), 0, Height - 1);
            int ye = Math.Clamp((int)Math.Ceiling(bottom) - 1, ys, Height - 1);

            double r = 0, g = 0, b = 0, a = 0, wsum = 0;
            for (int y = ys; y <= ye; y++)
            {
                double wy = Math.Min(y + 1, bottom) - Math.Max(y, top);
                if (wy <= 0) wy = 1e-6;
                for (int x = xs; x <= xe; x++)
                {
                    double wx = Math.Min(x + 1, right) - Math.Max(x, left);
                    if (wx <= 0) wx = 1e-6;
                    double w = wx * wy;

                    var p = Sample(x, y);
                    double wa = w * p.a;
                    r += p.r * wa; g += p.g * wa; b += p.b * wa;
                    a += wa;
                    wsum += w;
                }
            }

            if (a <= 0) return (0, 0, 0, 0);
            return (r / a, g / a, b / a, a / wsum);
        }
    }
}
