using System.Runtime.InteropServices;
using GHelper.AnimeMatrix;
using SkiaSharp;

namespace GHelper.App.Services;

/// <summary>
/// Picture decoder for the matrix picture mode (png / jpg / bmp / gif / webp). Animated pictures are decoded frame by
/// frame with disposal handled by Skia; <see cref="MatrixImage.FrameDelayMs"/> is the delay of the first frame (legacy:
/// GDI+ property item 0x5100 of the first frame).
/// </summary>
public sealed class SkiaMatrixImageDecoder : IMatrixImageDecoder
{
    /// <summary>
    /// The matrix has a few dozen LEDs per axis, so frames are shrunk to this longest side while decoding to keep
    /// long GIFs cheap. Matches what the zoom (max 200%) of the picture editor can ever show.
    /// </summary>
    public const int MaxSide = 320;

    public MatrixImage? Load(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName)) return null;

        byte[] bytes = File.ReadAllBytes(fileName);
        return Decode(bytes);
    }

    public MatrixImage? Decode(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null) return null;

        int width = codec.Info.Width, height = codec.Info.Height;
        if (width <= 0 || height <= 0) return null;

        // decode into premultiplied pixels (what Skia composes frames in), hand out straight alpha
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        int count = Math.Max(1, codec.FrameCount);
        var frameInfo = codec.FrameInfo;
        if (frameInfo is null || frameInfo.Length < count) count = Math.Max(1, frameInfo?.Length ?? 1);

        // frames that later frames are composed on top of must stay alive until then
        var required = new HashSet<int>();
        if (count > 1)
            for (int i = 0; i < count; i++)
                if (frameInfo![i].RequiredFrame >= 0) required.Add(frameInfo[i].RequiredFrame);

        var kept = new Dictionary<int, SKBitmap>();
        var frames = new List<PixelBuffer>(count);

        try
        {
            for (int i = 0; i < count; i++)
            {
                var bitmap = new SKBitmap(info);
                var options = new SKCodecOptions(i);

                if (count > 1)
                {
                    int prior = frameInfo![i].RequiredFrame;
                    if (prior >= 0 && kept.TryGetValue(prior, out var priorBitmap))
                    {
                        priorBitmap.GetPixelSpan().CopyTo(bitmap.GetPixelSpan());
                        options = new SKCodecOptions(i, prior);
                    }
                }

                var result = codec.GetPixels(info, bitmap.GetPixels(), options);
                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                {
                    bitmap.Dispose();
                    if (i == 0) return null;
                    break;
                }

                frames.Add(ToPixelBuffer(bitmap));

                if (required.Contains(i)) kept[i] = bitmap;
                else bitmap.Dispose();
            }
        }
        finally
        {
            foreach (var bitmap in kept.Values) bitmap.Dispose();
        }

        if (frames.Count == 0) return null;

        int delay = 0;
        if (frames.Count > 1 && frameInfo is { Length: > 0 }) delay = Math.Max(0, frameInfo[0].Duration);

        return new MatrixImage(frames, delay);
    }

    private static PixelBuffer ToPixelBuffer(SKBitmap source)
    {
        int width = source.Width, height = source.Height;
        int w = width, h = height;

        int longest = Math.Max(width, height);
        if (longest > MaxSide)
        {
            double scale = (double)MaxSide / longest;
            w = Math.Max(1, (int)Math.Round(width * scale));
            h = Math.Max(1, (int)Math.Round(height * scale));
        }

        SKBitmap? resized = null;
        try
        {
            SKBitmap from = source;
            if (w != width || h != height)
            {
                resized = source.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                if (resized is not null) from = resized;
                else { w = width; h = height; }
            }

            var buffer = new PixelBuffer(w, h);
            var target = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var pixmap = from.PeekPixels();
            var handle = GCHandle.Alloc(buffer.Data, GCHandleType.Pinned);
            try
            {
                if (pixmap is null || !pixmap.ReadPixels(target, handle.AddrOfPinnedObject(), w * 4))
                    throw new InvalidOperationException("Cannot read decoded picture pixels");
            }
            finally
            {
                handle.Free();
            }

            return buffer;
        }
        finally
        {
            resized?.Dispose();
        }
    }
}
