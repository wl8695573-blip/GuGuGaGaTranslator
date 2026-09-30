using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GuGuGaGaTranslator.Core.Capture;

/// <summary>One captured image: tightly packed top-down BGRA8 pixels plus the screen rectangle they came from.</summary>
public sealed class Frame
{
    /// <summary>The pixels, 4 bytes each, top row first.</summary>
    public required byte[] Bgra { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>The screen rectangle this frame depicts, in physical pixels.</summary>
    public required Int32Rect SourceRegion { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    public int Stride => Width * 4;

    public int PixelCount => Width * Height;

    public BitmapSource ToBitmapSource() =>
        BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, Bgra, Stride);

    public override string ToString() =>
        $"{Width}×{Height} @ {SourceRegion.X},{SourceRegion.Y} ({CapturedAt:HH:mm:ss.fff})";
}
