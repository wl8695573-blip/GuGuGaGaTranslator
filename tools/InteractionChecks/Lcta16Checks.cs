using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GuGuGaGaTranslator.App;
using GuGuGaGaTranslator.Core.Config;

internal static partial class Program
{
    private static void CheckLcta16(string scratch)
    {
        var root = Path.Combine(scratch, "lcta16-ui-" + Guid.NewGuid().ToString("N"));
        var session = new AppSession(root);
        session.LoadConfig();
        foreach (var property in session.Config.Hotkeys.GetType().GetProperties())
            if (property.PropertyType == typeof(string)) property.SetValue(session.Config.Hotkeys, "");
        var main = new MainWindow(session) { Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            main.Show(); Pump();
            var ball = Field<FloatingBallWindow>(main, "_floatingBall");
            var menu = Field<ContextMenu>(ball, "_menu");
            Check(menu.Items.OfType<MenuItem>().Any(item => item.Header?.ToString() == "关闭悬浮球"), "1.6 floating icon menu includes close action");
            var visual = (FrameworkElement)ball.Content;
            visual.Measure(new Size(88, 40)); visual.Arrange(new Rect(0, 0, 88, 40));
            var rendered = new RenderTargetBitmap(88, 40, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(visual);
            var pixels = new byte[88 * 40 * 4];
            rendered.CopyPixels(pixels, 88 * 4, 0);
            Check(pixels[(1 * 88 + 1) * 4 + 3] > 240 && pixels[(1 * 88 + 43) * 4 + 3] == 0,
                "1.6 floating icon has orange outer edge and transparent center notch");
            ((TextBox)main.FindName("CacheDirectoryBox")).Text = "relative";
            var read = (bool)typeof(MainWindow).GetMethod("ReadUiIntoConfig", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
            Check(!read && session.Config.Storage.CacheDirectory == "", "1.6 UI refuses relative directory without changing configuration");
            ((TextBox)main.FindName("CacheDirectoryBox")).Text = Path.Combine(root, "custom-cache");
            ((TextBox)main.FindName("LogDirectoryBox")).Text = Path.Combine(root, "custom-logs");
            ((CheckBox)main.FindName("CacheEnabledCheck")).IsChecked = false;
            ((CheckBox)main.FindName("PersistentCacheCheck")).IsChecked = true;
            ((CheckBox)main.FindName("CacheContextCheck")).IsChecked = false;
            ((TextBox)main.FindName("CacheMemoryBox")).Text = "500";
            read = (bool)typeof(MainWindow).GetMethod("ReadUiIntoConfig", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
            session.SaveConfig();
            Check(read && !session.Config.Translation.Cache.Enabled && !session.PersistentCacheAvailable
                && !session.Config.Translation.Cache.MatchContext && session.Config.Translation.Cache.MemoryEntries == 500,
                "1.6 UI cache settings save disabled mode, context and memory limit");
            Check(session.CacheDatabasePath.StartsWith(Path.Combine(root, "custom-cache")), "1.6 UI applies custom cache directory");
            var preset = (ComboBox)main.FindName("PresetCombo");
            preset.SelectedItem = preset.Items.Cast<object>().Single(item => item.ToString()!.Contains("LM Studio"));
            Check(((TextBox)main.FindName("BaseUrlBox")).Text == "http://127.0.0.1:1234/v1"
                && ((TextBox)main.FindName("ModelBox")).Text == "", "1.6 LM Studio preset asks for actual loaded model");
            var models = (ComboBox)main.FindName("AvailableModelsCombo");
            models.ItemsSource = new[] { "local-model" }; models.SelectedIndex = 0;
            Check(((TextBox)main.FindName("ModelBox")).Text == "local-model", "1.6 selected available model fills translation model field");
            InvokeClick(main, "OnClearMemoryCache");
            Check(session.Cache.Count == 0, "1.6 memory clear action leaves a usable session");
            var close = menu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "关闭悬浮球");
            close.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            Check(main.IsVisible && !session.Config.FloatingBall.Enabled, "1.6 menu closes icon while main window remains usable");
            var setup = new SetupWindow(session) { MaxHeight = 500, Left = -10000, Top = -10000,
                WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false, ShowActivated = false };
            var scroll = (ScrollViewer)setup.FindName("SetupScrollViewer");
            scroll.MaxHeight = 498;
            try
            {
                setup.Show(); Pump();
                Check(setup.ActualHeight <= 500 && scroll.ExtentHeight > scroll.ViewportHeight,
                    "1.6 setup remains scrollable on short desktops");
                Check(((ComboBox)setup.FindName("ProviderCombo")).Items.Cast<object>().Any(item => item.ToString()!.Contains("Gemini")),
                    "1.6 initial setup shares extended service presets");
            }
            finally { setup.Close(); }
        }
        finally
        {
            main.Close();
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
