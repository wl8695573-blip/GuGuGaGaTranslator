using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using GuGuGaGaTranslator.App;
using GuGuGaGaTranslator.Core.Capture;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Interop;
using GuGuGaGaTranslator.Core.Pipeline;
using GuGuGaGaTranslator.Core.Translation;
using GuGuGaGaTranslator.Ocr.Rapid;
using Frame = GuGuGaGaTranslator.Core.Capture.Frame;

internal static partial class Program
{
    private static int _passed;

    [STAThread]
    private static int Main(string[] args)
    {
        var scratch = Path.GetFullPath(args.FirstOrDefault() ?? ".artifacts/interactions");
        Directory.CreateDirectory(scratch);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            var root = Directory.GetParent(Directory.GetParent(FindModels())!.FullName)!.FullName;
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var resources = XDocument.Load(Path.Combine(root, "src", "GuGuGaGaTranslator.App", "App.xaml"))
                .Root!.Element(xaml + "Application.Resources")!;
            app.Resources = (ResourceDictionary)XamlReader.Parse(
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">"
                + string.Join("", resources.Elements().Select(element => element.ToString())) + "</ResourceDictionary>");
            // 显示真实模态窗口但放在屏幕外，不抓屏、不注册快捷键，也不加载用户配置。
            for (var cycle = 0; cycle < 4; cycle++)
            {
                foreach (var chooserType in new[] { typeof(DirectionChooserWindow), typeof(ListChooserWindow) })
                {
                    foreach (var action in new[] { "select", "escape", "deactivate", "close" })
                        CheckChooser(chooserType, action);
                }
            }
            CheckSession(scratch);
            CheckCaptureMigration();
            CheckAppearance(scratch);
            CheckOverlay();
            CheckLcta15(scratch);
            if (args.Contains("--capture"))
            {
                CheckCapture(scratch);
                CheckLiveSession(scratch, root);
            }
            Console.WriteLine($"Passed {_passed} interaction checks.");
            app.Shutdown();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            app.Shutdown(1);
            return 1;
        }
    }

    private static void CheckCaptureMigration()
    {
        var config = new AppConfig();
        config.Target.CaptureBackend = "screen";
        config.NormalizeLanguages();
        Check(config.Target.CaptureBackend == "window" && config.Target.CaptureSettingsVersion == 1,
            "old default screen capture migrates to window capture once");
        config.Target.CaptureBackend = "screen";
        config.NormalizeLanguages();
        Check(config.Target.CaptureBackend == "screen", "manual screen capture choice survives later loads");
        config.Target.CaptureSettingsVersion = 0;
        config.Target.CaptureBackend = "printwindow";
        config.NormalizeLanguages();
        Check(config.Target.CaptureBackend == "printwindow", "old PrintWindow choice is preserved");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        _passed++;
        Console.WriteLine("PASS " + message);
    }

    private static void CheckChooser(Type type, string action)
    {
        var window = (Window)Activator.CreateInstance(type, nonPublic: true)!;
        window.Left = -10000;
        window.Top = -10000;
        window.ShowActivated = false;
        object[] buildArguments = type == typeof(DirectionChooserWindow)
            ? [new LanguagePreset[] { new() { Label = "英 → 中", From = "en", To = "zh-Hans", Ocr = "en-US" } }, new LanguagePair("ja", "zh-Hans")]
            : [new ListChooserWindow.Item[] { new("通用翻译") }, -1];
        type.GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, buildArguments);

        var closingCount = 0;
        window.Closing += (_, _) =>
        {
            closingCount++;
            // 重现用户日志中的顺序：关闭尚未结束时，又收到失焦事件。
            Deactivate(window);
        };
        Exception? failure = null;
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                switch (action)
                {
                    case "select":
                        var list = (StackPanel)((Border)window.Content).Child;
                        ((Border)list.Children[0]).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                        {
                            RoutedEvent = UIElement.MouseLeftButtonUpEvent
                        });
                        break;
                    case "escape":
                        window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Escape)
                        {
                            RoutedEvent = Keyboard.KeyDownEvent
                        });
                        break;
                    case "deactivate":
                        Deactivate(window);
                        break;
                    default:
                        window.Close();
                        break;
                }
            }
            catch (Exception error)
            {
                failure = error;
                Application.Current.Shutdown(1);
            }
        }));
        var accepted = window.ShowDialog();
        if (failure is not null)
            throw new InvalidOperationException($"{type.Name}: {action} failed", failure);
        var field = type.GetField("_chosen", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var chosen = field.GetValue(window);
        var selectionCorrect = type == typeof(DirectionChooserWindow)
            ? action == "select" ? chosen is LanguagePreset { From: "en" } : chosen is null
            : (int)chosen! == (action == "select" ? 0 : -1);
        Check(closingCount == 1 && accepted == (action == "select") && selectionCorrect,
            $"{type.Name}: {action} closes once and returns the expected choice");
    }

    private static void Deactivate(Window window) =>
        typeof(Window).GetMethod("OnDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [EventArgs.Empty]);

    private static void CheckSession(string scratch)
    {
        var session = new AppSession(Path.Combine(scratch, "config-" + Guid.NewGuid().ToString("N")));
        try
        {
            session.LoadConfig();
            Check(session.Languages == new LanguagePair("auto", "zh-Hans") && session.ActiveProfile is null,
                "fresh configuration uses automatic source detection and general translation");
            var limbus = session.FindProfile("limbus-company")!;
            session.AutoDetectProfileAsync("Limbus Company").GetAwaiter().GetResult();
            Check(session.ActiveProfile == limbus, "matching game title selects its profile");
            session.AutoDetectProfileAsync("English Newspaper").GetAwaiter().GetResult();
            Check(session.ActiveProfile is null, "unmatched window clears the previous game's profile");
            session.Config.Translation.AutoDetectProfile = false;
            session.ApplyProfileAsync(limbus).GetAwaiter().GetResult();
            session.AutoDetectProfileAsync("English Newspaper").GetAwaiter().GetResult();
            Check(session.ActiveProfile == limbus, "disabled automatic matching preserves the manually chosen profile");
            var terms = GameProfiles.ForDirection(limbus, "auto", "zh-Hans");
            Check(terms.Any(term => term.Source == "Identity") && terms.Any(term => term.Source == "人格"),
                "automatic source detection retains multilingual profile terms");
            session.ApplyProfileAsync(null).GetAwaiter().GetResult();
            session.Config.Target.Region = new RegionRect(0, 0, 100, 100);
            session.Config.Target.Identity = null;
            session.Config.Target.TitleHint = null;
            session.Config.Translation.Translator = session.Config.Translation.Translator with { Provider = "mock" };
            session.Config.Ocr.RapidModelDirectory = FindModels();
            session.Start();
            Check(session.IsRunning, "isolated RapidOCR pipeline starts without a capture target");
            var recognizer = session.Recognizer;
            var pipeline = session.Pipeline;
            foreach (var preset in Enumerable.Range(0, 4).SelectMany(_ => DefaultLanguagePresets.Create()))
            {
                session.ApplyLanguagePresetAsync(preset).GetAwaiter().GetResult();
                Check(session.Languages == new LanguagePair(preset.From, preset.To)
                    && session.Config.Ocr.Language == preset.Ocr && session.IsRunning
                    && ReferenceEquals(recognizer, session.Recognizer) && ReferenceEquals(pipeline, session.Pipeline),
                    $"live RapidOCR language switch: {preset.Label} keeps the existing models and pipeline");
            }
        }
        finally
        {
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static string FindModels()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var models = Path.Combine(directory.FullName, "models", "v6");
            if (File.Exists(Path.Combine(models, "PP-OCRv6_det_small.onnx")))
                return models;
        }
        throw new DirectoryNotFoundException("Repository OCR models not found.");
    }

    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static void Invoke(object instance, string name) =>
        instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, null);

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static T Await<T>(Task<T> task)
    {
        var deadline = Environment.TickCount64 + 20000;
        while (!task.IsCompleted)
        {
            Pump();
            Thread.Sleep(10);
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("Capture check timed out");
        }
        return task.GetAwaiter().GetResult();
    }

    private static void Click(Border button) => button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
    {
        RoutedEvent = UIElement.MouseLeftButtonUpEvent,
    });

    private static void CheckAppearance(string scratch)
    {
        var session = new AppSession(Path.Combine(scratch, "appearance-" + Guid.NewGuid().ToString("N")));
        session.LoadConfig();
        var main = new MainWindow(session);
        try
        {
            Invoke(main, "PopulateChoices");
            Invoke(main, "LoadConfigIntoUi");
            ((TextBox)main.FindName("ModelBox")).Text = "custom-unsaved-model";
            var chooser = (ComboBox)main.FindName("OverlayPresetCombo");
            for (var cycle = 0; cycle < 5; cycle++)
                for (var index = 0; index < session.Config.Overlay.Presets.Count; index++)
                {
                    chooser.SelectedIndex = index;
                    var preset = session.Config.Overlay.Presets[index];
                    Check(session.Config.Overlay.FontSize == preset.FontSize && session.Config.Overlay.Placement == preset.Placement
                        && chooser.SelectedIndex == index && !Field<bool>(main, "_loadingUi"),
                        $"appearance preset {preset.Name}, cycle {cycle}: selection completes without recursion");
                }
            Check(((TextBox)main.FindName("ModelBox")).Text == "custom-unsaved-model",
                "appearance changes preserve other unsaved settings");
            var profiles = (ComboBox)main.FindName("GameProfileCombo");
            for (var cycle = 0; cycle < 20; cycle++)
            {
                profiles.SelectedIndex = cycle % 2;
                Pump();
                Check(session.Config.Translation.ActiveProfile == (cycle % 2 == 0 ? "" : "limbus-company")
                    && !Field<bool>(main, "_loadingUi"), $"profile selector cycle {cycle}: no recursive selection");
            }
        }
        finally { main.Close(); session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private static void CheckOverlay()
    {
        var config = new OverlayConfig { ClickThrough = true, Placement = OverlayPlacement.Below };
        var overlay = new OverlayWindow { ShowActivated = false };
        try
        {
            overlay.Configure(config, new LanguagePair("en", "zh-Hans"));
            Check(Field<LanguageBarWindow?>(overlay, "_bar") is null, "configuration does not open an orphan control bar");
            overlay.Open();
            overlay.ShowTranslated("Example source", "测试译文", new Int32Rect(250, 200, 420, 90));
            Pump();
            var bar = Field<LanguageBarWindow>(overlay, "_bar");
            WindowEnumerator.TryReadFrameRect(new WindowInteropHelper(overlay).Handle, out var before);
            var handle = Field<Thumb>(bar, "_dragHandle");
            handle.RaiseEvent(new DragDeltaEventArgs(30, 20) { RoutedEvent = Thumb.DragDeltaEvent });
            Pump();
            WindowEnumerator.TryReadFrameRect(new WindowInteropHelper(overlay).Handle, out var after);
            Check(after.X > before.X && after.Y > before.Y
                && bar.ScreenBounds.X == after.X && config.ClickThrough,
                "control bar grip moves the bar and translation together while click-through stays enabled");
            config.Placement = OverlayPlacement.Above;
            config.OffsetY = 0;
            overlay.Configure(config, new LanguagePair("en", "zh-Hans"));
            handle.RaiseEvent(new DragDeltaEventArgs(0, 10) { RoutedEvent = Thumb.DragDeltaEvent });
            Check(config.OffsetY < 0, "above placement follows the drag direction");
            var dismissals = 0;
            overlay.DismissRequested += () => dismissals++;
            var buttons = Field<StackPanel>(bar, "_buttons");
            Click((Border)buttons.Children[buttons.Children.Count - 1]);
            overlay.ShowTranslated("Next source", "关闭后的迟到译文", new Int32Rect(250, 200, 420, 90));
            overlay.Configure(config, new LanguagePair("en", "zh-Hans"));
            Check(!overlay.IsVisible && !bar.IsVisible && dismissals == 1, "close hides both windows and late updates cannot reopen them");
            overlay.Open();
            overlay.ShowTranslated("Source", "重新启动", new Int32Rect(250, 200, 420, 90));
            Check(overlay.IsVisible && bar.IsVisible, "translation can reopen after dismissal");
            bar.Close();
            Pump();
            Check(!overlay.IsVisible && !bar.IsVisible && dismissals == 2, "control bar system close safely dismisses both windows");
            overlay.Open();
            config.Placement = OverlayPlacement.Below;
            config.OffsetY = 8;
            var workArea = OverlayWindowInterop.WorkAreaAt(new Int32Rect(250, 200, 420, 90));
            var bottomSource = new Int32Rect(workArea.X + 100, workArea.Y + workArea.Height - 95, 420, 90);
            overlay.ShowTranslated("Source", "底部字幕的译文应避让原文。", bottomSource);
            Pump();
            WindowEnumerator.TryReadFrameRect(new WindowInteropHelper(overlay).Handle, out var fallback);
            Check(fallback.Y + fallback.Height <= bottomSource.Y, "bottom dialogue falls back above without covering source");
            handle.RaiseEvent(new DragDeltaEventArgs(0, 10) { RoutedEvent = Thumb.DragDeltaEvent });
            WindowEnumerator.TryReadFrameRect(new WindowInteropHelper(overlay).Handle, out var dragged);
            Check(dragged.Y == fallback.Y + (int)Math.Round(10 * VisualTreeHelper.GetDpi(bar).DpiScaleY)
                && config.Placement == OverlayPlacement.Above, "drag after automatic placement follows the pointer at current DPI");
            config.Placement = OverlayPlacement.Below;
            config.FontSize = 28;
            overlay.ShowTranslated("Source", string.Join('\n', Enumerable.Repeat("长译文需要滚动查看，不能落到屏幕外。", 80)), new Int32Rect(250, 900, 420, 90));
            Pump();
            WindowEnumerator.TryReadFrameRect(new WindowInteropHelper(overlay).Handle, out var info);
            var area = OverlayWindowInterop.WorkAreaAt(info);
            Check(info.Y >= area.Y && info.Y + info.Height <= area.Y + area.Height
                && Field<ScrollViewer>(overlay, "_scroll").ScrollableHeight > 0,
                "long translation remains within the work area and can scroll");
            overlay.Close();
            Pump();
            Check(!overlay.IsVisible && !bar.IsVisible && dismissals == 3, "translation panel system close safely dismisses both windows");
        }
        finally { overlay.ClosePermanently(); }
    }

    private static void CheckCapture(string scratch)
    {
        DpiAwareness.EnablePerMonitorV2();
        var label = new TextBlock { Text = "A new day begins.", FontSize = 34, Foreground = Brushes.White, Margin = new Thickness(20) };
        var target = new Window
        {
            Title = "GGGT capture fixture",
            Left = 180,
            Top = 150,
            Width = 640,
            Height = 250,
            ShowActivated = false,
            Background = Brushes.Navy,
            Content = label,
        };
        var cover = new Window
        {
            Title = "GGGT covering fixture",
            Left = 180,
            Top = 150,
            Width = 640,
            Height = 250,
            ShowActivated = false,
            Topmost = true,
            Background = Brushes.Magenta,
        };
        WindowCapture? capture = null;
        OverlayWindow? overlay = null;
        try
        {
            target.Show();
            Pump();
            var info = WindowEnumerator.TryDescribe(new WindowInteropHelper(target).Handle)!;
            capture = new WindowCapture(info.Handle);
            var baseline = Await(capture.CaptureAsync(info, info.ClientRect, CancellationToken.None));
            cover.Show();
            label.Text = "The journey continues.";
            Pump();
            Await(Task.Delay(500).ContinueWith(_ => true));
            var covered = Await(capture.CaptureAsync(info, info.ClientRect, CancellationToken.None));
            using var recognizer = RapidOcrRecognizer.Create(new() { ModelDirectory = FindModels() });
            var ocr = Await(recognizer.RecognizeAsync(covered, CancellationToken.None));
            Console.WriteLine("Covered fixture OCR: " + ocr.Text);
            File.WriteAllBytes(Path.Combine(scratch, "covered-frame.png"), Encode(covered));
            Check(ocr.Text.Contains("journey", StringComparison.OrdinalIgnoreCase), "window capture reads updated source text behind another window");
            cover.Hide();
            overlay = new OverlayWindow { ShowActivated = false };
            var config = new OverlayConfig { Placement = OverlayPlacement.Over, ExcludeFromCapture = false, BackgroundOpacity = 1 };
            overlay.Configure(config, new LanguagePair("en", "zh-Hans"));
            overlay.Open();
            overlay.ShowTranslated("Wrong original", "这段译文不能被识别为原文。", info.ClientRect);
            label.Text = "A new day begins.";
            Pump();
            Await(Task.Delay(500).ContinueWith(_ => true));
            var overlapped = Await(capture.CaptureAsync(info, info.ClientRect, CancellationToken.None));
            ocr = Await(recognizer.RecognizeAsync(overlapped, CancellationToken.None));
            Check(ocr.Text.Contains("new day", StringComparison.OrdinalIgnoreCase) && !ocr.Text.Contains("译文"),
                "overlapping recorded overlay leaves the target source intact");
            config.ExcludeFromCapture = true;
            overlay.Configure(config, new LanguagePair("en", "zh-Hans"));
            label.Text = "Translation should preserve the source.";
            Await(Task.Delay(500).ContinueWith(_ => true));
            var excluded = Await(capture.CaptureAsync(info, info.ClientRect, CancellationToken.None));
            ocr = Await(recognizer.RecognizeAsync(excluded, CancellationToken.None));
            Check(ocr.Text.Contains("preserve", StringComparison.OrdinalIgnoreCase),
                "capture exclusion toggle still permits fresh target text");
            target.Width = 740;
            target.Height = 300;
            Pump();
            Await(Task.Delay(500).ContinueWith(_ => true));
            info = WindowEnumerator.TryDescribe(info.Handle)!;
            var resized = Await(capture.CaptureAsync(info, info.ClientRect, CancellationToken.None));
            Check(resized.Width == info.ClientRect.Width && resized.Height == info.ClientRect.Height,
                "capture pool follows target resize and client crop");
            target.Left += 40;
            target.Top += 20;
            Pump();
            info = WindowEnumerator.TryDescribe(info.Handle)!;
            var moved = Await(capture.CaptureAsync(info, info.ClientRect, CancellationToken.None));
            Check(moved.SourceRegion == info.ClientRect, "window capture coordinates follow the moved target");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var cancelled = false;
            try
            { Await(capture.CaptureAsync(info, info.ClientRect, cancellation.Token)); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "window capture honours cancellation");
            target.Close();
            Pump();
            Await(Task.Delay(200).ContinueWith(_ => true));
            var closed = false;
            try
            { Await(capture.CaptureAsync(info, info.ClientRect, CancellationToken.None)); }
            catch (InvalidOperationException) { closed = true; }
            Check(closed, "closed target produces a recoverable capture error");
        }
        finally { overlay?.ClosePermanently(); capture?.Dispose(); cover.Close(); target.Close(); }
    }

    private static byte[] Encode(Frame frame)
    {
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(frame.ToBitmapSource()));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void WaitFor(Func<bool> ready, string message)
    {
        var deadline = Environment.TickCount64 + 20000;
        while (!ready())
        {
            Pump();
            Thread.Sleep(10);
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException(message);
        }
        Pump();
    }

    private static void CheckLiveSession(string scratch, string root)
    {
        var title = "GGGT-132-live-" + Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(Path.Combine(root, "tools", "SampleWindow", "bin", "Release", "net10.0-windows10.0.19041.0", "gugugaga-sample-window.dll"));
        foreach (var argument in new[] { "--title", title, "--text", "A new day begins.", "--framed", "--notopmost", "--life", "90", "--w", "720", "--h", "260", "--x", "200", "--y", "170" })
            start.ArgumentList.Add(argument);
        using var sample = Process.Start(start)!;
        var session = new AppSession(Path.Combine(scratch, "live-" + Guid.NewGuid().ToString("N")));
        MainWindow? main = null;
        try
        {
            WindowInfo? info = null;
            WaitFor(() => (info = WindowEnumerator.List().FirstOrDefault(window => window.Title == title)) is not null,
                "Sample window did not open");
            session.LoadConfig();
            session.Config.Ocr.RapidModelDirectory = FindModels();
            session.Config.Translation.AutoDetectProfile = false;
            session.Config.Translation.Translator = session.Config.Translation.Translator with { Provider = "mock" };
            session.ApplyLanguagePresetAsync(new() { From = "en", To = "zh-Hans", Ocr = "en-US" }).GetAwaiter().GetResult();
            session.Config.Overlay.Placement = OverlayPlacement.Over;
            session.Config.Overlay.ExcludeFromCapture = false;
            session.Config.Overlay.Width = 420;
            session.Config.Overlay.Height = 100;
            session.SetRegion(info!, info!.ClientRect);
            session.ApplyLanguagePresetAsync(new() { From = "en", To = "zh-Hans", Ocr = "en-US" }).GetAwaiter().GetResult();
            foreach (var property in session.Config.Hotkeys.GetType().GetProperties())
                if (property.PropertyType == typeof(string))
                    property.SetValue(session.Config.Hotkeys, "");
            main = new MainWindow(session) { ShowActivated = false, Left = -10000, Top = -10000, ShowInTaskbar = false };
            main.Show();
            Pump();
            Check(main.StartAutomatically(), "main window starts the window capture pipeline");
            var translation = (TextBox)main.FindName("TranslationBox");
            WaitFor(() => translation.Text.Contains("new day", StringComparison.OrdinalIgnoreCase), "Live capture did not reach the main view");
            Check(((TextBox)main.FindName("SourceBox")).Text.Contains("new day", StringComparison.OrdinalIgnoreCase)
                && translation.Text.Contains("mock"), "live window capture, RapidOCR, mock translation and WPF result update complete");
            ((TextBox)main.FindName("QuickTermSource")).Text = "new day";
            ((TextBox)main.FindName("QuickTermTarget")).Text = "新一天";
            InvokeClick(main, "OnAddQuickTerm");
            WaitFor(() => translation.Text.Contains("新一天"), "Current line did not adopt added personal term");
            Check(translation.Text.Contains("新一天"), "quick term immediately retranslates unchanged captured line");
            ((TextBox)main.FindName("QuickTermSource")).Text = "new day";
            ((TextBox)main.FindName("QuickTermTarget")).Text = "全新一天";
            InvokeClick(main, "OnAddQuickTerm");
            WaitFor(() => translation.Text.Contains("全新一天"), "Current line kept obsolete personal term");
            Check(translation.Text.Contains("全新一天"), "quick term edit invalidates previous translation of current line");
            session.Config.Translation.PersonalTerms.Clear();
            session.SaveConfig();
            session.ForgetContext();
            WaitFor(() => translation.Text.Contains("new day", StringComparison.OrdinalIgnoreCase), "Current line did not recover after removing personal term");
            Check(!translation.Text.Contains("全新一天"), "removing personal term restores current line without waiting for scene change");
            var overlay = Field<OverlayWindow>(main, "_overlay");
            var chooser = (ComboBox)main.FindName("OverlayPresetCombo");
            for (var index = 0; index < session.Config.Overlay.Presets.Count; index++)
            {
                chooser.SelectedIndex = index;
                Pump();
                Check(session.IsRunning && overlay.IsVisible, $"live appearance preset {index} leaves the pipeline running");
            }
            var bar = Field<LanguageBarWindow>(overlay, "_bar");
            var buttons = Field<StackPanel>(bar, "_buttons");
            Click((Border)buttons.Children[buttons.Children.Count - 1]);
            WaitFor(() => !session.IsRunning, "Close did not stop the pipeline");
            Check(!overlay.IsVisible && !bar.IsVisible, "control bar close stops the live pipeline and hides both windows");
            main.StartAutomatically();
            WaitFor(() => session.IsRunning, "Main window did not restart after closing the overlay");
            Check(session.IsRunning, "main window restarts after closing the overlay");
            WaitFor(() => overlay.IsVisible && bar.IsVisible, "Overlay did not reopen");
            Check(overlay.IsVisible && bar.IsVisible, "both live windows reopen after restart");
            for (var cycle = 1; cycle < 10; cycle++)
            {
                var closingButtons = Field<StackPanel>(bar, "_buttons");
                Click((Border)closingButtons.Children[closingButtons.Children.Count - 1]);
                WaitFor(() => !session.IsRunning && !session.IsStopping, "Repeated close did not stop the pipeline");
                Check(!overlay.IsVisible && !bar.IsVisible, $"close cycle {cycle}: both windows hidden");
                WaitFor(() => main.StartAutomatically(), "Repeated start did not restart the pipeline");
                WaitFor(() => overlay.IsVisible && bar.IsVisible, "Repeated start did not show both windows");
                Check(session.IsRunning && overlay.IsVisible && bar.IsVisible, $"restart cycle {cycle}: pipeline and overlay reopened");
            }
            var stop = typeof(MainWindow).GetMethod("StopAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Await(((Task)stop.Invoke(main, null)!).ContinueWith(_ => true));
            Check(!overlay.IsVisible && !bar.IsVisible && !session.IsRunning, "main stop button hides the entire overlay");
            File.WriteAllText(Path.Combine(scratch, "live-result.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                passed = true,
                capture = "Windows Graphics Capture",
                translator = "mock",
                source = "A new day begins.",
                checks = new[] { "MainWindow Loaded", "window capture", "OCR", "result update", "four live presets", "close", "restart", "stop" },
            }));
        }
        finally
        {
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            main?.Close();
            if (!sample.HasExited)
            {
                sample.CloseMainWindow();
                if (!sample.WaitForExit(3000))
                    sample.Kill();
            }
        }
    }
}
