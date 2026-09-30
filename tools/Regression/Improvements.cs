using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using GuGuGaGaTranslator.Core.Capture;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Diagnostics;
using GuGuGaGaTranslator.Core.Pipeline;
using GuGuGaGaTranslator.Core.Translation;
using GuGuGaGaTranslator.Storage.Sqlite;
using Microsoft.Data.Sqlite;

internal static class Improvements
{
    public static void CheckAll(string scratch)
    {
        var nullConfig = System.Text.Json.JsonSerializer.Deserialize<AppConfig>("""{"Translation":{"Cache":null,"Translator":null},"Ocr":null,"Overlay":null}""")!;
        nullConfig.NormalizeLanguages();
        Program.Check(nullConfig.Translation.Cache is not null && nullConfig.Ocr.Fallbacks.Length > 0, "explicit-null config defaults repaired");
        Program.Check(!DefaultLanguagePresets.IsSupportedOcr("zh-Hant") && !DefaultLanguagePresets.IsSupportedOcr("jazz"), "OCR language rejects unsupported prefixes");
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
        {
            var region = TargetRegionResolver.Resolve(new(100, 400, 600, 100),
                new(-1200, -100, (int)(800 * scale), (int)(600 * scale)), 800, 600);
            Program.Check(region == new Int32Rect(-1200 + (int)(100 * scale), -100 + (int)(400 * scale),
                (int)(600 * scale), (int)(100 * scale)), $"region follows client size at {scale:P0}");
        }
        Program.Check(TargetRegionResolver.Resolve(new(900, 0, 100, 50), new(0, 0, 800, 600)) is null,
            "region outside client rejected");
        Program.Check(TargetRegionResolver.Resolve(new(-10, -20, 50, 60), new(0, 0, 800, 600)) == new Int32Rect(0, 0, 40, 40),
            "legacy region clipped without changing scale");

        var profile = new GameProfile { Id = "example", Name = "示例", Author = "作者", SourceUrl = "https://example.com/",
            License = "CC0-1.0", ProfileVersion = "1.0", From = "ja", To = "en",
            Terms = [new() { Language = "ja", Source = "朝", Target = "早晨" }] };
        var restored = GameProfileArchive.Deserialize(GameProfileArchive.Serialize(profile));
        Program.Check(restored.Terms[0].Language == "ja" && restored.Author == "作者" && restored.To == "en", "profile round trip preserves language and provenance");
        var clone = GameProfileArchive.Clone(profile);
        clone.Terms[0].Target = "morning";
        Program.Check(profile.Terms[0].Target == "早晨" && clone.Terms[0].Language == "ja", "profile editor clone is independent and preserves language");
        clone.Terms.Add(new() { Language = "ja", Source = "朝", Target = "other" });
        Program.Check(GameProfileArchive.Validate(clone).Count > 0, "profile conflicts rejected");
        foreach (var data in new[] { "{}", GameProfileArchive.Serialize(profile).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99"),
            GameProfileArchive.Serialize(profile).Replace("\"profile\":", "\"apiKey\":\"should-not-import\",\"profile\":") })
        {
            var rejected = false;
            try { GameProfileArchive.Deserialize(data); } catch (InvalidDataException) { rejected = true; }
            Program.Check(rejected, "profile rejects unsupported envelope");
        }
        foreach (var builtIn in GameProfiles.Default())
            Program.Check(GameProfileArchive.Validate(builtIn).Count == 0,
                "built-in profile valid: " + builtIn.Name + " " + string.Join(";", GameProfileArchive.Validate(builtIn)));

        var database = Path.Combine(scratch, "cache.sqlite");
        var now = DateTimeOffset.UtcNow;
        using (var storage = new SqliteTranslationCacheStore(database, 1, 2, () => now))
        {
            storage.Set("one", "PRIVATE-TRANSLATION");
            now = now.AddSeconds(1); storage.Set("two", "二");
            now = now.AddSeconds(1); storage.Get("one");
            now = now.AddSeconds(1); storage.Set("three", "三");
            Program.Check(storage.Get("two") is null && storage.Get("one") == "PRIVATE-TRANSLATION", "disk cache LRU bounded");
        }
        Program.Check(!Encoding.UTF8.GetString(File.ReadAllBytes(database)).Contains("PRIVATE-TRANSLATION"), "disk cache values encrypted");
        using (var storage = new SqliteTranslationCacheStore(database, 1, 2, () => now))
        {
            Program.Check(storage.Get("one") == "PRIVATE-TRANSLATION", "disk cache survives restart");
            now = now.AddDays(2);
            Program.Check(storage.Get("one") is null, "disk cache expires");
            storage.Set("fresh", "缓存"); storage.Clear();
            Program.Check(storage.Get("fresh") is null, "disk cache clear");
        }
        using (var sql = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            sql.Open(); using var command = sql.CreateCommand();
            command.CommandText = "INSERT INTO translations VALUES ('corrupt','dpapi:v1:broken',$now,$now);";
            command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds()); command.ExecuteNonQuery();
        }
        using (var storage = new SqliteTranslationCacheStore(database, clock: () => now))
            Program.Check(storage.Get("corrupt") is null, "unreadable cache becomes miss");
        using (var cache = new TranslationCache())
        {
            var failures = 0; cache.StorageFailed += _ => failures++;
            cache.ConfigureStorage(new FailedCache());
            var request = new TranslationRequest { Text = "hello", From = "en", To = "zh-Hans" };
            cache.Set("engine", request, "memory");
            Program.Check(cache.TryGet("engine", request, out var value) && value == "memory" && failures == 1,
                "disk failure falls back to memory once");
        }

        var configRoot = Path.Combine(scratch, "private-config");
        var config = new AppConfig();
        config.Target.TitleHint = "PRIVATE-TITLE";
        config.Translation.Translator = config.Translation.Translator with
            { ApiKey = "PRIVATE-KEY", BaseUrl = "https://PRIVATE-ENDPOINT.invalid", Provider = "PRIVATE-PROVIDER" };
        config.Translation.GameProfiles[0].Worldview = "PRIVATE-LORE";
        var diagnostics = new DiagnosticsService(configRoot);
        diagnostics.Record(new PipelineUpdate { At = now, Status = PipelineStatus.Error, Error = "PRIVATE-ERROR", Translation = "PRIVATE-RESULT" });
        File.AppendAllText(Path.Combine(configRoot, "logs", "runtime.jsonl"), """{"kind":"Started","extra":"PRIVATE-EDITED-LOG"}""" + Environment.NewLine);
        File.WriteAllText(Path.Combine(configRoot, "config.json"), "PRIVATE-FILE");
        var zip = Path.Combine(scratch, "diagnostics.zip");
        diagnostics.Export(zip, config);
        using var archive = ZipFile.OpenRead(zip);
        var contents = string.Join("\n", archive.Entries.Select(entry => { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); }));
        Program.Check(archive.Entries.Count == 3 && !contents.Contains("PRIVATE-"), "diagnostics whitelist excludes all private content");
        var blocked = false;
        try { diagnostics.Export(Path.Combine(configRoot, "config.json"), config); } catch (InvalidOperationException) { blocked = true; }
        Program.Check(blocked && File.ReadAllText(Path.Combine(configRoot, "config.json")) == "PRIVATE-FILE", "diagnostics cannot overwrite config");
    }

    private sealed class FailedCache : ITranslationCacheStore
    {
        public string? Get(string key) => throw new IOException("test");
        public void Set(string key, string translation) => throw new IOException("test");
        public void Clear() => throw new IOException("test");
        public void Dispose() { }
    }

    public static async Task CheckApi()
    {
        using var client = new HttpClient(new CaiyunHandler());
        using var translator = new CaiyunTranslator(new ClassicApiOptions(AppSecret: "TEST-SECRET", Token: "OLD-TOKEN"), client);
        var result = await translator.TranslateAsync(new TranslationRequest { Text = "Hello", From = "en", To = "zh-Hans" });
        Program.Check(result == "你好", "Caiyun uses encrypted AppSecret field before legacy token");
        using var different = new CaiyunTranslator(new ClassicApiOptions(EndpointOverride: "https://test.invalid/alternate"));
        Program.Check(translator.Id != different.Id, "classic cache distinguishes custom endpoints");
        using var general = new YoudaoTranslator(new ClassicApiOptions(AppId: "account", Domain: "general"));
        using var game = new YoudaoTranslator(new ClassicApiOptions(AppId: "account", Domain: "game"));
        Program.Check(general.Id != game.Id, "classic cache distinguishes translation domains");
    }

    private sealed class CaiyunHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.GetValues("x-authorization").Single() != "token TEST-SECRET")
                throw new InvalidDataException("Wrong credential field");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"target":["你好"]}""") });
        }
    }
}
