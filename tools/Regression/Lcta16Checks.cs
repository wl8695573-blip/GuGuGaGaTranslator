using System.IO;
using System.Net;
using System.Net.Http;
using GuGuGaGaTranslator.App;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Translation;
using GuGuGaGaTranslator.Storage.Sqlite;

internal static class Lcta16Checks
{
    public static async Task RunAsync(string scratch)
    {
        var now = DateTimeOffset.UtcNow;
        var line = new TranslationRequest { Text = "Hello", From = "en", To = "zh-Hans" };
        using (var cache = new TranslationCache(2, () => now))
        {
            cache.Set("model", line, "你好");
            Program.Check(cache.TryGet("model", line, out var value) && value == "你好", "1.6 memory cache returns stored translation");
            cache.Configure(false, 2, 1, true);
            cache.Set("model", line, "disabled");
            Program.Check(cache.Count == 0 && !cache.TryGet("model", line, out _), "1.6 disabled cache neither stores nor reads");
            cache.Configure(true, 2, 1, true);
            cache.Set("model", line, "你好");
            cache.Set("model", line with { Text = "two" }, "二");
            cache.Set("model", line with { Text = "three" }, "三");
            Program.Check(cache.Count == 2 && !cache.TryGet("model", line, out _), "1.6 memory capacity evicts least recent entry");
            now = now.AddDays(2);
            Program.Check(!cache.TryGet("model", line with { Text = "two" }, out _), "1.6 memory entries expire");
            Program.Check(cache.CleanExpired() == 2 && cache.Count == 0, "1.6 expired memory cleanup removes entries");
            cache.ResetStatistics();
            Program.Check(cache.Hits == 0 && cache.Misses == 0, "1.6 cache statistics reset");
            cache.Set("model", line, "你好");
            var context = line with { Context = [new("prior", "previous")] };
            Program.Check(!cache.TryGet("model", context, out _), "1.6 default cache separates context");
            cache.Configure(true, 2, 30, false);
            cache.Set("model", line, "你好");
            Program.Check(cache.TryGet("model", context, out value) && value == "你好", "1.6 optional source reuse ignores only context");
            Program.Check(!cache.TryGet("other model", context, out _), "1.6 source reuse still separates models");
            Program.Check(!cache.TryGet("model", context with { To = "ko" }, out _), "1.6 source reuse still separates directions");
            Program.Check(!cache.TryGet("model", context with { Glossary = [new("Hello", "您好")] }, out _), "1.6 source reuse still separates terms");
        }
        var diskPath = Path.Combine(scratch, "1.6-cache.sqlite");
        using (var disk = new SqliteTranslationCacheStore(diskPath, retentionDays: 1, clock: () => now))
        {
            disk.Set("one", "第一条");
            var info = disk.GetStatistics();
            Program.Check(info.Entries == 1 && info.Bytes > 0, "1.6 disk cache reports count and size");
            now = now.AddDays(2);
            Program.Check(disk.GetStatistics().Entries == 0 && disk.CleanExpired() == 1, "1.6 disk cache cleanup removes expired record");
        }
        using (var cache = new TranslationCache(clock: () => now))
        {
            cache.ConfigureStorage(new SqliteTranslationCacheStore(diskPath, retentionDays: 1, clock: () => now));
            cache.Set("model", line, "你好");
            cache.ClearMemory();
            now = now.AddHours(23);
            Program.Check(cache.TryGet("model", line, out _), "1.6 persisted entry promotes into memory");
            now = now.AddHours(2);
            Program.Check(!cache.TryGet("model", line, out _), "1.6 promotion retains original disk expiration");
        }
        var oldRoot = Path.Combine(scratch, "1.6-old-data");
        var newRoot = Path.Combine(scratch, "1.6-new-data 中文");
        var occupied = Path.Combine(scratch, "1.6-occupied");
        Directory.CreateDirectory(occupied);
        File.WriteAllText(Path.Combine(occupied, "keep.txt"), "keep");
        await using (var session = new AppSession(oldRoot))
        {
            session.LoadConfig();
            session.Config.Translation.Translator = new() { ApiKey = "test-private-key", Provider = "mock" };
            session.Config.Translation.PersonalTerms.Add(new() { Source = "name", Target = "名字" });
            session.Config.Translation.Cache.Persist = true;
            session.SaveConfig();
            session.Cache.Set("model", line, "你好");
            Directory.CreateDirectory(session.DumpDirectory);
            File.WriteAllText(Path.Combine(session.DumpDirectory, "keep.json"), "evidence");
            Program.Check(session.CacheDatabasePath.StartsWith(oldRoot) && session.DumpDirectory.StartsWith(oldRoot), "1.6 explicit data directory contains cache and default evidence");
            var failed = false;
            try { await session.RelocateDataAsync(occupied); } catch (IOException) { failed = true; }
            Program.Check(failed && session.Store.Directory == oldRoot && File.ReadAllText(Path.Combine(occupied, "keep.txt")) == "keep", "1.6 occupied migration destination is preserved");
            Program.Check(session.Cache.TryGet("model", line, out _), "1.6 failed migration restores disk cache");
            await session.RelocateDataAsync(newRoot);
            Program.Check(session.Store.Directory == newRoot && File.Exists(Path.Combine(oldRoot, "config.json")), "1.6 migration switches directory and preserves original backup");
            Program.Check(session.DumpDirectory.StartsWith(newRoot) && File.ReadAllText(Path.Combine(session.DumpDirectory, "keep.json")) == "evidence", "1.6 migration moves default evidence location");
            Program.Check(session.Cache.TryGet("model", line, out _), "1.6 migrated disk cache remains reusable");
            var loaded = new ConfigStore(newRoot).Load();
            Program.Check(loaded.Translation.Translator.ApiKey == "test-private-key" && loaded.Translation.PersonalTerms.Count == 1, "1.6 migration preserves key and personal terms");
            Program.Check(!File.ReadAllText(Path.Combine(newRoot, "config.json")).Contains("test-private-key"), "1.6 migrated config retains encrypted credentials");
            session.Config.Storage.CacheDirectory = Path.Combine(scratch, "1.6-custom-cache");
            session.Config.Storage.LogDirectory = Path.Combine(scratch, "1.6-custom-logs");
            session.SaveConfig();
            Program.Check(session.Cache.TryGet("model", line, out _) && session.CacheDatabasePath.Contains("custom-cache"), "1.6 changing cache location preserves reusable cache");
            session.Diagnostics.Record(new GuGuGaGaTranslator.Core.Diagnostics.DiagnosticEvent { Kind = GuGuGaGaTranslator.Core.Diagnostics.DiagnosticEventKind.Started });
            Program.Check(File.Exists(Path.Combine(session.Config.Storage.LogDirectory, "runtime.jsonl")), "1.6 logs use configured directory");
            Directory.CreateDirectory(session.ExportDirectory);
            session.ExportDiagnostics(Path.Combine(session.ExportDirectory, "diagnostics.zip"));
            Program.Check(File.Exists(Path.Combine(session.ExportDirectory, "diagnostics.zip")), "1.6 diagnostics can export beneath selected data directory");
            session.ClearCache();
            Program.Check(!session.Cache.TryGet("model", line, out _), "1.6 deleting cache prevents disk reuse");
        }
        var nested = false;
        try { DataDirectory.CopyForMigration(oldRoot, Path.Combine(oldRoot, "nested")); } catch (ArgumentException) { nested = true; }
        Program.Check(nested, "1.6 migration refuses nesting");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var cancelled = false;
            try { DataDirectory.CopyForMigration(oldRoot, Path.Combine(scratch, "cancelled-data"), cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Program.Check(cancelled && File.Exists(Path.Combine(oldRoot, "config.json")), "1.6 cancelled migration preserves original data");
        }
        var badConfig = new AppConfig();
        badConfig.Storage.CacheDirectory = "relative";
        badConfig.Debug.DumpDirectory = "relative";
        badConfig.NormalizeLanguages();
        Program.Check(badConfig.Storage.CacheDirectory == "" && badConfig.Debug.DumpDirectory == "", "1.6 hand-edited invalid directories recover to defaults");
        Program.Check(ModelServices.Presets.Any(item => item.Name.Contains("千问")) && ModelServices.Presets.Any(item => item.Name.Contains("Gemini")) && ModelServices.Presets.Any(item => item.Name.Contains("LM Studio")), "1.6 presets include new cloud and local model services");
        Program.Check(ModelServices.ModelsEndpoint("https://example.invalid/v1/chat/completions").AbsoluteUri == "https://example.invalid/v1/models", "1.6 model list normalizes full completion endpoint");
        Program.Check(ModelServices.ModelsEndpoint("https://generativelanguage.googleapis.com/v1beta/openai/").AbsoluteUri.EndsWith("/openai/models"), "1.6 model list preserves nested Gemini root");
        var rejected = false;
        try { ModelServices.ModelsEndpoint("https://user:secret@example.invalid/v1"); } catch (ArgumentException) { rejected = true; }
        Program.Check(rejected, "1.6 model list rejects embedded credentials");
        using var http = new HttpClient(new ModelsHandler());
        var models = await ModelServices.ListModelsAsync("https://example.invalid/v1", "test-key", CancellationToken.None, http);
        Program.Check(models.SequenceEqual(new[] { "model-a", "model-b" }), "1.6 model list filters malformed entries and duplicates");
        using var unsupported = new HttpClient(new ModelsHandler(HttpStatusCode.NotFound));
        var unsupportedCaught = false;
        try { await ModelServices.ListModelsAsync("https://example.invalid/v1", "", CancellationToken.None, unsupported); }
        catch (InvalidOperationException error) { unsupportedCaught = error.Message.Contains("手动") || error.Message.Contains("控制台"); }
        Program.Check(unsupportedCaught, "1.6 unsupported model listing explains manual entry");
    }

    private sealed class ModelsHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Program.Check(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/v1/models", "1.6 model listing sends GET without dialogue");
            if (status == HttpStatusCode.OK) Program.Check(request.Headers.Authorization?.Parameter == "test-key", "1.6 model listing uses request-scoped credentials");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("""{"data":[{"id":"model-b"},null,{}, {"id":"model-a"},{"id":"model-a"}]}""") });
        }
    }
}
