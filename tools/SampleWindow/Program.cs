using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace GuGuGaGaTranslator.SampleWindow;

/// <summary>合成对话窗口，支持定时切换文本。</summary>
internal static class Program
{
    private const string DefaultText =
        "「ねえ、こんな時間にどこに行くの？」\n" +
        "\"Hey, where are you going at this hour?\"\n" +
        "「喂，这个时间你要去哪里？」";

    private static readonly string[] CycleLines =
    [
        "「ねえ、こんな時間にどこに行くの？」",
        "「ちょっと、コンビニまで…」",
        "「そんな格好で行くつもり？」",
        "「文句あるなら一緒に来れば？」",
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        var options = Options.Parse(args);
        var text = options.Get("text")?.Replace("\\n", "\n") ?? DefaultText;
        var customCycle = options.Get("cycle-lines")?.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (customCycle is { Length: > 0 })
            text = customCycle[0];
        var width = options.GetInt("w", 1000);
        var height = options.GetInt("h", 300);
        var left = options.GetInt("x", 200);
        var top = options.GetInt("y", 200);
        var fontSize = options.GetDouble("font", 30);
        var framed = options.Has("framed");
        var cycleSeconds = options.GetDouble("cycle", 0);
        var lifeSeconds = options.GetDouble("life", 1800);

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var label = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = fontSize * 1.6,
            // --align bottom reproduces a visual novel's bottom dialogue box for overlay alignment.
            VerticalAlignment = options.Get("align")?.Equals("bottom", StringComparison.OrdinalIgnoreCase) == true
                ? VerticalAlignment.Bottom
                : VerticalAlignment.Top,
        };

        var window = new Window
        {
            Title = options.Get("title") ?? "gugugaga sample — 对话窗测试",
            Width = width,
            Height = height,
            Left = left,
            Top = top,
            WindowStyle = framed ? WindowStyle.SingleBorderWindow : WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Topmost = options.Has("notopmost") is false,
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x12, 0x1A)),
            Content = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x6A, 0xA8)),
                BorderThickness = new Thickness(framed ? 0 : 3),
                Padding = new Thickness(28, 24, 28, 24),
                Child = label,
            },
        };

        if (cycleSeconds > 0)
        {
            var lines = customCycle is { Length: > 1 } ? customCycle : CycleLines;
            var index = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(cycleSeconds) };
            timer.Tick += (_, _) =>
            {
                index = (index + 1) % lines.Length;
                label.Text = customCycle is { Length: > 1 }
                    ? lines[index]
                    : lines[index] + (text.Contains('\n') ? text[text.IndexOf('\n')..] : string.Empty);
            };
            timer.Start();
        }

        if (lifeSeconds > 0)
        {
            var life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(lifeSeconds) };
            life.Tick += (_, _) => app.Shutdown();
            life.Start();
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"sample window: {width}×{height} at {left},{top}, framed={framed}, cycle={cycleSeconds}s, life={lifeSeconds}s"));
        app.Run(window);
        return 0;
    }

    private sealed class Options
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public static Options Parse(IEnumerable<string> args)
        {
            var options = new Options();
            string? pending = null;
            foreach (var arg in args)
            {
                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    if (pending is not null)
                        options._values[pending] = "true";
                    pending = arg[2..];
                }
                else if (pending is not null)
                {
                    options._values[pending] = arg;
                    pending = null;
                }
            }

            if (pending is not null)
                options._values[pending] = "true";
            return options;
        }

        public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

        public bool Has(string key) => _values.ContainsKey(key);

        public int GetInt(string key, int fallback) =>
            int.TryParse(Get(key), CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

        public double GetDouble(string key, double fallback) =>
            double.TryParse(Get(key), CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }
}
