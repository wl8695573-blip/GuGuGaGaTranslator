using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GuGuGaGaTranslator.Icon;

/// <summary>将选定的 LCTA 字标转换为窗口 PNG 和多尺寸 Windows ICO。</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = Value(args, "--out") ?? Path.Combine("src", "GuGuGaGaTranslator.App", "Assets");
        var source = Value(args, "--source") ?? Path.Combine(output, "lcta-wordmark.png");
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(Path.GetFullPath(source), UriKind.Absolute);
        image.EndInit();
        image.Freeze();

        Directory.CreateDirectory(output);
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var frames = sizes.Select(size => (Size: size, Bytes: Render(image, size))).ToList();
        File.WriteAllBytes(Path.Combine(output, "icon.ico"), BuildIco(frames));
        File.WriteAllBytes(Path.Combine(output, "icon.png"), frames[^1].Bytes);
        Console.WriteLine($"已生成 LCTA icon.png 和 icon.ico（{frames.Count} 个尺寸）。");
        return 0;
    }

    private static byte[] Render(BitmapSource source, int size)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, size, size));
            // 按原比例居中，避免把横向字标拉伸成方形。
            var scale = Math.Min((double)size / source.PixelWidth, (double)size / source.PixelHeight);
            var width = source.PixelWidth * scale;
            var height = source.PixelHeight * scale;
            drawing.DrawImage(source, new Rect((size - width) / 2, (size - height) / 2, width, height));
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static byte[] BuildIco(IReadOnlyList<(int Size, byte[] Bytes)> frames)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);
        var offset = 6 + 16 * frames.Count;
        foreach (var (size, bytes) in frames)
        {
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(bytes.Length);
            writer.Write(offset);
            offset += bytes.Length;
        }
        foreach (var frame in frames) writer.Write(frame.Bytes);
        return stream.ToArray();
    }

    private static string? Value(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        return null;
    }
}
