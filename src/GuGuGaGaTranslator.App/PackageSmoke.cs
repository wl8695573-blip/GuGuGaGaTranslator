using System.IO;
using System.Text.Json;
using System.Windows;
using GuGuGaGaTranslator.Core.Capture;
using GuGuGaGaTranslator.Core.Translation;
using GuGuGaGaTranslator.Ocr.Rapid;
using GuGuGaGaTranslator.Storage.Sqlite;

namespace GuGuGaGaTranslator.App;

/// <summary>Unattended package acceptance: no game capture, UI display, account config, or network calls.</summary>
internal static class PackageSmoke
{
    public static int Run(string[] args)
    {
        static string Required(string[] arguments, string name)
        {
            var index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? Path.GetFullPath(arguments[index + 1])
                : throw new ArgumentException($"{name} requires a path");
        }
        string? report = null;
        string? database = null;
        try
        {
            report = Required(args, "--verify-installation");
            var directory = Required(args, "--config-dir");
            Directory.CreateDirectory(directory);
            database = Path.Combine(directory, "smoke-" + Guid.NewGuid().ToString("N") + ".sqlite");
            using (var storage = new SqliteTranslationCacheStore(database))
            {
                storage.Set("smoke", "示例缓存");
                if (storage.Get("smoke") != "示例缓存") throw new InvalidDataException("SQLite/DPAPI round trip failed");
            }
            using var recognizer = RapidOcrRecognizer.Create(new RapidOcrSettings());
            recognizer.RecognizeAsync(new Frame
            {
                Bgra = Enumerable.Repeat((byte)255, 320 * 80 * 4).ToArray(), Width = 320, Height = 80,
                SourceRegion = new Int32Rect(0, 0, 320, 80), CapturedAt = DateTimeOffset.UtcNow,
            }).GetAwaiter().GetResult();
            foreach (var profile in GameProfiles.Default())
                if (GameProfileArchive.Validate(profile).Count > 0) throw new InvalidDataException("Invalid built-in profile");
            // Construct the real views without Loaded events and without window handles.
            var session = new AppSession(directory);
            _ = new SetupWindow(session);
            _ = new MainWindow(session);
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            File.WriteAllText(report, JsonSerializer.Serialize(new
                { passed = true, version = typeof(App).Assembly.GetName().Version?.ToString(), checks = new[] { "SQLite", "DPAPI", "RapidOCR", "profiles", "WPF views" } }));
            return 0;
        }
        catch (Exception error)
        {
            if (report is not null) File.WriteAllText(report, JsonSerializer.Serialize(new { passed = false, error = error.GetType().Name }));
            return 1;
        }
        finally
        {
            if (database is not null) { File.Delete(database); File.Delete(database + "-journal"); }
        }
    }
}
