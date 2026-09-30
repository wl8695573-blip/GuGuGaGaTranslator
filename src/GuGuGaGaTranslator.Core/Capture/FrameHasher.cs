using System.Numerics;
using GuGuGaGaTranslator.Core.Capture;

namespace GuGuGaGaTranslator.Core.Capture;

/// <summary>A 256-bit perceptual signature of a frame: a 16×16 grid where each bit says whether that
/// block is brighter than the frame average. Comparing signatures is how the pipeline decides which
/// frames are worth OCR-ing.</summary>
/// <param name="Rows">Bits for rows 0-3.</param>
/// <param name="Rows2">Bits for rows 4-7.</param>
/// <param name="Rows3">Bits for rows 8-11.</param>
/// <param name="Rows4">Bits for rows 12-15.</param>
public readonly record struct FrameSignature(ulong Rows, ulong Rows2, ulong Rows3, ulong Rows4)
{
    public const int BitCount = 256;

    public int DistanceTo(FrameSignature other) =>
        BitOperations.PopCount(Rows ^ other.Rows)
        + BitOperations.PopCount(Rows2 ^ other.Rows2)
        + BitOperations.PopCount(Rows3 ^ other.Rows3)
        + BitOperations.PopCount(Rows4 ^ other.Rows4);

    public double ChangeRatioTo(FrameSignature other) => DistanceTo(other) / (double)BitCount;
}

/// <summary>Computes <see cref="FrameSignature"/> values, and the block averages they are built from.</summary>
public static class FrameHasher
{
    private const int Grid = 16;

    public static FrameSignature Compute(Frame frame)
    {
        var blocks = BlockLuminance(frame);
        double total = 0;
        for (var i = 0; i < blocks.Length; i++)
            total += blocks[i];
        var mean = total / blocks.Length;

        ulong[] words = new ulong[4];
        for (var i = 0; i < blocks.Length; i++)
        {
            if (blocks[i] <= mean)
                continue;
            var word = i / 64;
            words[word] |= 1UL << (i % 64);
        }

        return new FrameSignature(words[0], words[1], words[2], words[3]);
    }

    /// <summary>Count how many grid cells actually changed between two frames: deliberately not the Hamming
    /// distance of whole-image signatures, since a bright dialogue band in a dark region flips too few bits.</summary>
    public static int ChangedCellCount(double[] previous, double[] current, double epsilon = 4)
    {
        if (previous.Length != current.Length)
            return int.MaxValue;

        var changed = 0;
        for (var i = 0; i < previous.Length; i++)
        {
            if (Math.Abs(previous[i] - current[i]) > epsilon)
                changed++;
        }

        return changed;
    }

    /// <summary>The mean luminance of each cell of a 16×16 grid, in row-major order.</summary>
    public static double[] BlockLuminance(Frame frame)
    {
        var blocks = new double[Grid * Grid];
        var counts = new int[Grid * Grid];

        for (var y = 0; y < frame.Height; y++)
        {
            var blockY = Math.Min(Grid - 1, y * Grid / frame.Height);
            for (var x = 0; x < frame.Width; x++)
            {
                var blockX = Math.Min(Grid - 1, x * Grid / frame.Width);
                var index = (blockY * Grid) + blockX;
                var offset = ((y * frame.Width) + x) * 4;

                var blue = frame.Bgra[offset];
                var green = frame.Bgra[offset + 1];
                var red = frame.Bgra[offset + 2];
                blocks[index] += (0.114 * blue) + (0.587 * green) + (0.299 * red);
                counts[index]++;
            }
        }

        for (var i = 0; i < blocks.Length; i++)
        {
            if (counts[i] > 0)
                blocks[i] /= counts[i];
        }

        return blocks;
    }
}
