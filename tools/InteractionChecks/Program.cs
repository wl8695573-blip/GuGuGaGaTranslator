using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GuGuGaGaTranslator.App;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Pipeline;
using GuGuGaGaTranslator.Core.Translation;

internal static class Program
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
            foreach (var preset in DefaultLanguagePresets.Create())
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
}
