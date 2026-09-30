using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GuGuGaGaTranslator.Core.Capture;

namespace GuGuGaGaTranslator.Core.Imaging;

/// <summary>Pixel operations on <see cref="Frame"/>: cropping, upscaling, grayscale, and PNG round-trips.
/// Upscaling is what makes small game text readable for OCR.</summary>
public static class ImageOps
{
    /// <summary>Crop a rectangle out of a frame; the result keeps screen coordinates, so a cropped frame
    /// still knows where on the desktop it came from.</summary>
    public static Frame Crop(Frame source, Int32Rect regionInFrame)
    {
        if (regionInFrame.X < 0 || regionInFrame.Y < 0
            || regionInFrame.X + regionInFrame.Width > source.Width
            || regionInFrame.Y + regionInFrame.Height > source.Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(regionInFrame),
                regionInFrame,
                $"crop {regionInFrame.X},{regionInFrame.Y} {regionInFrame.Width}×{regionInFrame.Height} is outside the {source.Width}×{source.Height} frame");
        }

        var pixels = new byte[regionInFrame.Width * regionInFrame.Height * 4];
        for (var row = 0; row < regionInFrame.Height; row++)
        {
            var sourceOffset = ((regionInFrame.Y + row) * source.Width + regionInFrame.X) * 4;
            var targetOffset = row * regionInFrame.Width * 4;
            Buffer.BlockCopy(source.Bgra, sourceOffset, pixels, targetOffset, regionInFrame.Width * 4);
        }

        return new Frame
        {
            Bgra = pixels,
            Width = regionInFrame.Width,
            Height = regionInFrame.Height,
            SourceRegion = new Int32Rect(
                source.SourceRegion.X + regionInFrame.X,
                source.SourceRegion.Y + regionInFrame.Y,
                regionInFrame.Width,
                regionInFrame.Height),
            CapturedAt = source.CapturedAt,
        };
    }

    /// <summary>Bilinear-upscale a frame; Windows OCR does noticeably better on dialogue text enlarged
    /// two to three times before recognition.</summary>
    public static Frame Upscale(Frame source, double scale)
    {
        if (scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "scale must be positive");
        if (Math.Abs(scale - 1.0) < 0.0001)
            return source;

        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var pixels = new byte[width * height * 4];

        var xRatio = (double)source.Width / width;
        var yRatio = (double)source.Height / height;

        for (var y = 0; y < height; y++)
        {
            var sourceY = Math.Min(source.Height - 1, (int)(y * yRatio));
            var nextY = Math.Min(source.Height - 1, sourceY + 1);
            var weightY = (y * yRatio) - sourceY;

            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Min(source.Width - 1, (int)(x * xRatio));
                var nextX = Math.Min(source.Width - 1, sourceX + 1);
                var weightX = (x * xRatio) - sourceX;

                var topLeft = ((sourceY * source.Width) + sourceX) * 4;
                var topRight = ((sourceY * source.Width) + nextX) * 4;
                var bottomLeft = ((nextY * source.Width) + sourceX) * 4;
                var bottomRight = ((nextY * source.Width) + nextX) * 4;
                var target = ((y * width) + x) * 4;

                for (var channel = 0; channel < 4; channel++)
                {
                    var top = (source.Bgra[topLeft + channel] * (1 - weightX)) + (source.Bgra[topRight + channel] * weightX);
                    var bottom = (source.Bgra[bottomLeft + channel] * (1 - weightX)) + (source.Bgra[bottomRight + channel] * weightX);
                    pixels[target + channel] = (byte)Math.Clamp((top * (1 - weightY)) + (bottom * weightY), 0, 255);
                }
            }
        }

        return new Frame
        {
            Bgra = pixels,
            Width = width,
            Height = height,
            SourceRegion = source.SourceRegion,
            CapturedAt = source.CapturedAt,
        };
    }

    /// <summary>Convert to grayscale, optionally stretching contrast around mid-gray for low-contrast
    /// text over a busy background.</summary>
    public static Frame ToGrayscale(Frame source, double contrast = 0)
    {
        var factor = 1 + Math.Clamp(contrast, 0, 1) * 2;
        // With no contrast stretch requested the conversion is pure cost, so it is skipped
        // rather than performed and discarded.
        if (contrast <= 0)
            return source;

        var pixels = new byte[source.Bgra.Length];

        for (var i = 0; i < source.Bgra.Length; i += 4)
        {
            var blue = source.Bgra[i];
            var green = source.Bgra[i + 1];
            var red = source.Bgra[i + 2];
            var luminance = ((0.114 * blue) + (0.587 * green) + (0.299 * red) - 128) * factor + 128;

            var value = (byte)Math.Clamp(luminance, 0, 255);
            pixels[i] = value;
            pixels[i + 1] = value;
            pixels[i + 2] = value;
            pixels[i + 3] = source.Bgra[i + 3];
        }

        return new Frame
        {
            Bgra = pixels,
            Width = source.Width,
            Height = source.Height,
            SourceRegion = source.SourceRegion,
            CapturedAt = source.CapturedAt,
        };
    }

    public static void SavePng(Frame frame, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame.ToBitmapSource()));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static Frame LoadPng(string path, Int32Rect sourceRegion = default)
    {
        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var source = decoder.Frames[0];

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        return new Frame
        {
            Bgra = pixels,
            Width = converted.PixelWidth,
            Height = converted.PixelHeight,
            SourceRegion = sourceRegion,
            CapturedAt = DateTimeOffset.Now,
        };
    }
}
