using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GuGuGaGaTranslator.App;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Pipeline;

internal static class Program
{
    // Render the actual view resources without showing windows, registering hotkeys, or capturing the desktop.
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var output = Path.GetFullPath(args.FirstOrDefault() ?? ".artifacts/ui");
            Directory.CreateDirectory(output);
            var app = new App();
            app.InitializeComponent();
            var settingsSession = new AppSession(Path.Combine(output, "setup-check-config"));
            settingsSession.Config.Translation.Translator = settingsSession.Config.Translation.Translator with
            {
                Provider = "openai-compatible",
                BaseUrl = "https://example.invalid/custom/v1",
                Model = "custom-model",
                ApiKey = "TEST-SETUP-KEY"
            };
            var settings = new SetupWindow(settingsSession);
            if (((TextBox)settings.FindName("BaseUrlBox")).Text != "https://example.invalid/custom/v1"
                || ((TextBox)settings.FindName("ModelBox")).Text != "custom-model")
                throw new InvalidOperationException("Setup overwrote custom configuration");
            ((ComboBox)settings.FindName("ProviderCombo")).SelectedIndex = 0;
            if (((PasswordBox)settings.FindName("ApiKeyBox")).Password.Length != 0
                || ((TextBox)settings.FindName("ApiKeyPlainBox")).Text.Length != 0)
                throw new InvalidOperationException("Setup retained another provider's key");
            settingsSession.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Console.WriteLine("PASS setup preserves custom fields and clears keys when changing provider.");
            var session = new AppSession(Path.Combine(output, "preview-config"));
            session.Config.Translation.Translator = session.Config.Translation.Translator with
            {
                Provider = "openai-compatible",
                BaseUrl = "https://api.deepseek.com",
                Model = "deepseek-flash"
            };
            var main = new MainWindow(session);
            ((Panel)main.Content).Background = main.Background;
            foreach (var name in new[] { "PopulateChoices", "LoadConfigIntoUi" })
                typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
            ((ListView)main.FindName("WindowList")).ItemsSource = new[] { new { Display = "示例游戏 · 窗口模式（合成预览）" } };
            ((TextBlock)main.FindName("TargetSummary")).Text = "示例游戏 · 1280 × 720 · 区域随窗口尺寸调整";
            ((TextBox)main.FindName("SourceBox")).Text = "明日もここで会いましょう。";
            ((TextBox)main.FindName("TranslationBox")).Text = "明天我们还在这里见面吧。";
            ((TextBlock)main.FindName("StatusText")).Text = "界面预览 · 示例台词";
            Render((FrameworkElement)main.Content, 1120, 740, Path.Combine(output, "screenshot-main.png"));
            var tabs = (TabControl)main.FindName("SettingsTabs");
            tabs.SelectedIndex = 1;
            Render((FrameworkElement)main.Content, 1120, 740, Path.Combine(output, "screenshot-provider.png"));
            var providers = (ComboBox)main.FindName("ProviderCombo");
            providers.SelectedIndex = 2;
            providers.BringIntoView();
            Render((FrameworkElement)main.Content, 1120, 740, Path.Combine(output, "screenshot-classic-provider.png"));
            if (((FrameworkElement)main.FindName("ClassicCredentialsPanel")).Visibility != Visibility.Visible
                || ((FrameworkElement)main.FindName("AISettingsPanel")).Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Classic provider panel visibility failed");
            providers.SelectedIndex = 1;
            tabs.SelectedIndex = tabs.Items.Count - 2;
            Render((FrameworkElement)main.Content, 1120, 740, Path.Combine(output, "screenshot-support.png"));
            var setup = new SetupWindow(session);
            ((TextBlock)setup.FindName("StorageHint")).Text = "密钥使用 Windows 当前账户加密保存。预览不保存配置。";
            Render((FrameworkElement)setup.Content, 440, null, Path.Combine(output, "screenshot-setup.png"));
            var combo = (ComboBox)setup.FindName("ProviderCombo");
            combo.ApplyTemplate();
            if (combo.Template.FindName("Chrome", combo) is not FrameworkElement)
                throw new InvalidOperationException("Missing custom ComboBox chrome");
            var overlay = new OverlayWindow();
            overlay.Configure(new OverlayConfig { ShowSource = true, ShowLanguageBar = false, TextOutline = false }, new LanguagePair("ja", "zh-Hans"));
            ((TextBlock)typeof(OverlayWindow).GetField("_sourceText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!).Text = "明日もここで会いましょう。";
            ((TextBlock)typeof(OverlayWindow).GetField("_translationText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!).Text = "明天我们还在这里见面吧。";
            Render((FrameworkElement)overlay.Content, 650, 130, Path.Combine(output, "screenshot-overlay.png"));
            var bar = new LanguageBarWindow();
            bar.Configure(DefaultLanguagePresets.Create(), new LanguagePair("auto", "zh-Hans"), false, false, true, "边狱巴士");
            var barContent = (FrameworkElement)bar.Content;
            barContent.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Render(barContent, barContent.DesiredSize.Width, barContent.DesiredSize.Height, Path.Combine(output, "screenshot-bar.png"));
            // Exercise narrow layouts and larger pixel output using the same controls.
            Render((FrameworkElement)main.Content, 880, 520, Path.Combine(output, "screenshot-minimum.png"), 1.5);
            var editor = new GameProfileWindow(session, session.Config.Translation.GameProfiles[0]);
            ((Panel)editor.Content).Background = editor.Background;
            Render((FrameworkElement)editor.Content, 880, 720, Path.Combine(output, "screenshot-profile.png"));
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Console.WriteLine("PASS real WPF views rendered; custom ComboBox template applied.");
            app.Shutdown();
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Render(FrameworkElement content, double width, double? height, string path, double scale = 1)
    {
        content.Measure(new Size(width, height ?? double.PositiveInfinity));
        var size = new Size(width, height ?? content.DesiredSize.Height);
        content.Arrange(new Rect(size));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
            drawing.DrawRectangle((Brush)Application.Current.Resources["WindowBackgroundBrush"], null, new Rect(size));
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
