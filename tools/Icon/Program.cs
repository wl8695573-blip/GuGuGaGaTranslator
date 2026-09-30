using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GuGuGaGaTranslator.Icon;

/// <summary>绘制替代图标并输出 ICO 与 PNG。</summary>
internal static class Program
{
    private const double Canvas = 256;

    private static readonly Color DeepSea = Color.FromRgb(0x2C, 0x3E, 0x9E);
    private static readonly Color EyeInk = Color.FromRgb(0x24, 0x32, 0x5F);
    private static readonly Color BlushPink = Color.FromRgb(0xFF, 0x9E, 0xC4);
    private static readonly Color BowPink = Color.FromRgb(0xFF, 0x87, 0xB8);

    private static int Main(string[] args)
    {
        var output = Value(args, "--out") ?? Path.Combine("src", "GuGuGaGaTranslator.App", "Assets");
        // Seven sizes because Windows picks a different frame for the taskbar, alt-tab, and explorer.
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };

        Directory.CreateDirectory(output);
        var frames = sizes.Select(size => (Size: size, Bytes: Render(size))).ToList();

        var ico = Path.Combine(output, "drawn-mascot.ico");
        File.WriteAllBytes(ico, BuildIco(frames));
        File.WriteAllBytes(Path.Combine(output, "drawn-mascot.png"), frames[^1].Bytes);
        File.WriteAllBytes(Path.Combine(output, "drawn-mascot-64.png"), frames.First(frame => frame.Size == 64).Bytes);

        Console.WriteLine($"wrote {ico} ({frames.Count} frames, {new FileInfo(ico).Length} bytes)");
        foreach (var frame in frames)
            Console.WriteLine($"  {frame.Size,3}px  {frame.Bytes.Length,6} bytes");

        // Printing the artwork as text is the only way to check a drawing without looking at it.
        Console.WriteLine();
        Console.WriteLine("64px, as text (darker = more ink):");
        Preview(frames.First(frame => frame.Size == 64).Bytes, 44, 22);
        Console.WriteLine();
        Console.WriteLine("16px, as text:");
        Preview(frames.First(frame => frame.Size == 16).Bytes, 16, 16);

        return 0;
    }

    private static byte[] Render(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / Canvas, size / Canvas));
            Draw(dc);
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void Draw(DrawingContext dc)
    {
        var outline = new Pen(new SolidColorBrush(DeepSea), 5) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };

        var tile = new LinearGradientBrush(
            Color.FromRgb(0x93, 0xB6, 0xFF), Color.FromRgb(0x3C, 0x55, 0xE4), new Point(0.3, 0), new Point(0.7, 1));
        dc.DrawRoundedRectangle(tile, null, new Rect(1, 1, Canvas - 2, Canvas - 2), 56, 56);

        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)), null, new Point(120, 150), 104, 80);

        var tail = new StreamGeometry();
        using (var g = tail.Open())
        {
            g.BeginFigure(new Point(192, 130), isFilled: true, isClosed: true);
            g.QuadraticBezierTo(new Point(248, 86), new Point(232, 152), isStroked: true, isSmoothJoin: true);
            g.QuadraticBezierTo(new Point(250, 212), new Point(192, 176), isStroked: true, isSmoothJoin: true);
        }

        tail.Freeze();
        dc.DrawGeometry(White(), outline, tail);

        var body = new LinearGradientBrush(
            Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xE4, 0xEC, 0xFF), new Point(0.4, 0), new Point(0.6, 1));
        dc.DrawEllipse(body, outline, new Point(122, 152), 98, 70);

        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xD8, 0xE4, 0xFF)), null, new Point(122, 192), 58, 26);

        var band = Arc(new Point(122, 152), 98, 70, 196, 344);
        band.Freeze();
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(DeepSea), 26) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat }, band);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xC2, 0xDA)), 18) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat }, band);
        dc.DrawGeometry(null, new Pen(White(), 5) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat }, Arc(new Point(122, 152), 98, 70, 196, 344));

        foreach (var angle in new double[] { 203, 238, 273, 308, 341 })
        {
            var point = OnArc(new Point(122, 152), 98, 70, angle);
            dc.DrawEllipse(White(), new Pen(new SolidColorBrush(DeepSea), 4), point, 11, 11);
        }

        var bowCentre = OnArc(new Point(122, 152), 98, 70, 199);
        var bow = new SolidColorBrush(BowPink);
        var bowPen = new Pen(new SolidColorBrush(DeepSea), 4) { LineJoin = PenLineJoin.Round };
        dc.DrawEllipse(bow, bowPen, new Point(bowCentre.X - 14, bowCentre.Y + 6), 15, 10);
        dc.DrawEllipse(bow, bowPen, new Point(bowCentre.X + 12, bowCentre.Y + 14), 15, 10);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xC2, 0xDA)), bowPen, new Point(bowCentre.X - 2, bowCentre.Y + 10), 6, 6);

        var ink = new SolidColorBrush(EyeInk);
        var blush = new SolidColorBrush(BlushPink) { Opacity = 0.55 };
        foreach (var x in new double[] { 88, 152 })
        {
            dc.DrawEllipse(blush, null, new Point(x - 18, 182), 16, 10);
            dc.DrawEllipse(ink, null, new Point(x, 158), 13, 15);
            dc.DrawEllipse(White(), null, new Point(x - 4, 152), 4.5, 4.5);
            dc.DrawEllipse(White(), null, new Point(x + 3, 163), 2.5, 2.5);
        }

        var smile = new StreamGeometry();
        using (var g = smile.Open())
        {
            g.BeginFigure(new Point(110, 184), isFilled: false, isClosed: false);
            g.QuadraticBezierTo(new Point(122, 196), new Point(134, 184), isStroked: true, isSmoothJoin: true);
        }

        smile.Freeze();
        dc.DrawGeometry(null, new Pen(ink, 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, smile);

        var spout = new StreamGeometry();
        using (var g = spout.Open())
        {
            g.BeginFigure(new Point(94, 84), isFilled: false, isClosed: false);
            g.QuadraticBezierTo(new Point(120, 40), new Point(146, 84), isStroked: true, isSmoothJoin: true);
        }

        spout.Freeze();
        dc.DrawGeometry(null, new Pen(White(), 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, spout);

        var drop = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
        dc.DrawEllipse(drop, null, new Point(103, 34), 7, 7);
        dc.DrawEllipse(drop, null, new Point(124, 22), 8.5, 8.5);
        dc.DrawEllipse(drop, null, new Point(145, 34), 7, 7);
    }

    private static SolidColorBrush White() => new(Colors.White);

    /// <summary>Build an arc of an ellipse between two angles, in degrees (0° is to the right, clockwise on screen).</summary>
    private static StreamGeometry Arc(Point centre, double rx, double ry, double from, double to)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(OnArc(centre, rx, ry, from), isFilled: false, isClosed: false);
        g.ArcTo(OnArc(centre, rx, ry, to), new Size(rx, ry), 0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        return geometry;
    }

    private static Point OnArc(Point centre, double rx, double ry, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(centre.X + (rx * Math.Cos(radians)), centre.Y + (ry * Math.Sin(radians)));
    }

    /// <summary>Assemble a multi-size .ico; Windows Vista and later accept PNG data inside an icon.</summary>
    private static byte[] BuildIco(IReadOnlyList<(int Size, byte[] Bytes)> frames)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);                  // reserved
        writer.Write((ushort)1);                  // 1 = icon
        writer.Write((ushort)frames.Count);

        var offset = 6 + (16 * frames.Count);
        foreach (var (size, bytes) in frames)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);                // palette size
            writer.Write((byte)0);                // reserved
            writer.Write((ushort)1);              // colour planes
            writer.Write((ushort)32);             // bits per pixel
            writer.Write(bytes.Length);
            writer.Write(offset);
            offset += bytes.Length;
        }

        foreach (var (_, bytes) in frames)
            writer.Write(bytes);
        writer.Flush();
        return stream.ToArray();
    }

    private static void Preview(byte[] png, int columns, int rows)
    {
        var decoder = new PngBitmapDecoder(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);

        const string ramp = " .:-=+*#%@";
        for (var row = 0; row < rows; row++)
        {
            var line = new System.Text.StringBuilder(columns);
            for (var column = 0; column < columns; column++)
            {
                var x0 = column * width / columns;
                var x1 = Math.Max(x0 + 1, (column + 1) * width / columns);
                var y0 = row * height / rows;
                var y1 = Math.Max(y0 + 1, (row + 1) * height / rows);

                double total = 0;
                var count = 0;
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        var at = ((y * width) + x) * 4;
                        var b = pixels[at];
                        var g = pixels[at + 1];
                        var r = pixels[at + 2];
                        total += (0.114 * b) + (0.587 * g) + (0.299 * r);
                        count++;
                    }
                }

                var luminance = total / Math.Max(1, count);
                line.Append(ramp[(int)Math.Clamp((255 - luminance) / 25.6, 0, ramp.Length - 1)]);
            }

            Console.WriteLine(line.ToString());
        }
    }

    private static string? Value(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
