using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using GuGuGaGaTranslator.App;
using GuGuGaGaTranslator.Core.Capture;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Pipeline;
using GuGuGaGaTranslator.Core.Text;
using GuGuGaGaTranslator.Core.Translation;

internal static class LctaChecks
{
    public static async Task RunAsync(string scratch)
    {
        var gate = new TextStabilityGate();
        Program.Check(!gate.Observe("Hello", 0, 450), "LCTA: waits for stable text");
        Program.Check(!gate.Observe("Hello there", 300, 450), "LCTA: growing text resets wait");
        Program.Check(!gate.Observe("Hello there", 749, 450) && gate.Observe("Hello there", 750, 450), "LCTA: settles on monotonic deadline");
        gate.Reset();
        Program.Check(gate.Observe("Hello", 800, 0), "LCTA: zero wait supports static text");

        var glossary = new GlossaryEntry[] { new("Outis", "奥提斯"), new("Nest", "巢"), new("李箱", "Yi Sang") };
        Program.Check(GlossarySelector.Select("Outis left yesterday.", glossary).Count == 1, "LCTA: only matched terms sent");
        Program.Check(!GlossarySelector.Contains("The nesting birds fly.", "Nest"), "LCTA: term word boundary");
        Program.Check(GlossarySelector.Contains("李箱说话了。", "李箱"), "LCTA: CJK term matching");

        var terms = TermSheet.Parse("en: Name = 译名 | 出处: https://example.invalid/source | 核对: 待核对 | 启用: 否");
        var roundtrip = TermSheet.Parse(TermSheet.Format(terms));
        Program.Check(roundtrip[0].SourceUrl == terms[0].SourceUrl && !roundtrip[0].Enabled && roundtrip[0].ReviewStatus == "待核对", "LCTA: term metadata sheet round trip");
        var profile = new GameProfile { Id = "test", Name = "test", Terms = terms };
        Program.Check(GameProfiles.ForDirection(profile, "en", "zh-Hans").Count == 0, "LCTA: pending terms not enforced");
        var archive = GameProfileArchive.Clone(profile);
        Program.Check(archive.Terms[0].SourceUrl == terms[0].SourceUrl && !archive.Terms[0].Enabled, "LCTA: archive keeps term metadata");
        var legacy = GameProfileArchive.Deserialize("""{"format":"gugugaga-game-profile","schemaVersion":1,"profile":{"id":"old","name":"旧档案","terms":[{"source":"Name","target":"译名"}]}}""");
        Program.Check(legacy.Terms[0].Enabled && legacy.ProcessHints.Count == 0, "LCTA: version one profile remains importable");
        var defaults = GameProfiles.Default();
        Program.Check(defaults[0].Terms.Where(term => term.Note?.Contains("待核对") == true).All(term => !term.Enabled), "LCTA: builtin candidates disabled");
        Program.Check(GameProfiles.MatchWindow("Limbus Company wiki", "msedge", defaults) is null, "LCTA: browser title does not enable game profile");
        Program.Check(GameProfiles.MatchWindow("a different title", "LimbusCompany", defaults)?.Id == "limbus-company", "LCTA: exact game process detection");
        Program.Check(GameProfiles.MatchWindow("a different title", "LimbusCompanyHelper", defaults) is null, "LCTA: no partial process match");

        await using var session = new AppSession(Path.Combine(scratch, "lcta-targets"));
        var first = Window("LimbusCompany", "Limbus Company");
        var second = Window("msedge", "English Newspaper");
        session.SetTarget(first);
        session.Config.Target.Region = new RegionRect(10, 20, 300, 80);
        await session.ApplyProfileAsync(defaults[0]);
        await session.ApplyLanguagePresetAsync(new() { Label = "English", From = "en", To = "zh-Hans", Ocr = "en-US" });
        session.SetTarget(second);
        Program.Check(session.Config.Target.Region is null && session.ActiveProfile is null && session.Languages.From == "auto", "LCTA: new target starts clean");
        session.SetTarget(first);
        Program.Check(session.Config.Target.Region == new RegionRect(10,20,300,80) && session.Languages.From == "en" && session.ActiveProfile?.Id == "limbus-company", "LCTA: restores per-target settings");
        await session.AutoDetectProfileAsync("English Newspaper", "msedge");
        Program.Check(session.ActiveProfile?.Id == "limbus-company", "LCTA: manual profile wins automatic matching");
        session.SaveConfig();
        var config = session.Store.Load();
        Program.Check(config.SavedTargets.Count == 2 && config.Target.ManualLanguage, "LCTA: target settings persist without keys");
        session.ClearRegion();
        session.SaveConfig();
        Program.Check(session.Config.Target.Identity == first.Identity && session.Config.Target.Region is null
            && session.Languages.From == "en", "LCTA: clearing region retains target and manual language");
        session.SetTarget(second);
        session.SetTarget(first);
        Program.Check(session.Config.Target.Region is null && session.Languages.From == "en"
            && session.Store.Load().SavedTargets.Single(item => item.Identity == first.Identity).Region is null,
            "LCTA: cleared region stays cleared after target switching and persistence");
        var custom = new GameProfile { Id = "ja-test", Name = "Japanese", From = "ja", To = "zh-Hans", OcrLanguage = "ja" };
        await session.ApplyProfileAsync(custom);
        Program.Check(session.Languages.From == "en", "LCTA: profile cannot overwrite manual direction");
        for (var cycle = 0; cycle < 20; cycle++)
            await session.ApplyProfileAsync(cycle % 2 == 0 ? defaults[0] : null);
        Program.Check(session.ActiveProfile is null && session.Languages.From == "en", "LCTA: twenty profile switches preserve manual direction");
        roundtrip[0].Enabled = true;
        roundtrip[0].ReviewStatus = "已核对";
        roundtrip[0].Note = "旧记录：待核对";
        profile.Terms = roundtrip;
        Program.Check(GameProfiles.ForDirection(profile, "en", "zh-Hans").Count == 1, "LCTA: reviewed term can be enabled despite historical note");
        Program.Check(TranslationErrors.Describe(new HttpRequestException("secret", null, HttpStatusCode.Unauthorized)).Contains("密钥")
            && !TranslationErrors.Describe(new Exception("secret")).Contains("secret"), "LCTA: helpful errors without raw response data");

        await TypewriterAsync();
        await RetryLimitAsync();
    }

    private static WindowInfo Window(string process, string title) => new()
    {
        Handle = 0, Title = title, ProcessName = process, ClassName = "test", ProcessId = 0,
        ClientRect = new(0, 0, 800, 600), FrameRect = new(0, 0, 800, 600), IsMinimized = false, IsOwned = false
    };

    private static TranslationPipeline Pipeline(FakeRecognizer recognizer, CountingTranslator translator) => new(new()
    {
        RegionProvider = () => new Int32Rect(0, 0, 32, 32),
        Capture = region => new Frame { Bgra = new byte[32 * 32 * 4], Width = 32, Height = 32, SourceRegion = region, CapturedAt = DateTimeOffset.Now },
        Recognizer = recognizer, Translator = translator, Languages = () => new("en", "zh-Hans"),
        Options = new() { PollIntervalMs = 15, OcrScale = 1, ForceRefreshMs = 0, MaskOwnWindows = false, ScriptGuard = false, TextSettleMs = 100, ErrorBackoffMs = 30 }
    });

    private static async Task TypewriterAsync()
    {
        var recognizer = new FakeRecognizer { Text = "Outis" };
        var translator = new CountingTranslator();
        await using var pipeline = Pipeline(recognizer, translator);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.Updated += update => { if (update.Status == PipelineStatus.Translated) done.TrySetResult(); };
        pipeline.Start();
        foreach (var text in new[] { "Outis opens", "Outis opens the", "Outis opens the door." })
        {
            await Task.Delay(45);
            recognizer.Text = text;
        }
        await done.Task.WaitAsync(TimeSpan.FromSeconds(4));
        await Task.Delay(150);
        await pipeline.StopAsync();
        Program.Check(translator.Calls == 1 && translator.LastText == "Outis opens the door.", "LCTA: typewriter translates completed line once");
    }

    private static async Task RetryLimitAsync()
    {
        var translator = new CountingTranslator { Fail = true };
        await using var pipeline = Pipeline(new FakeRecognizer(), translator);
        pipeline.Start();
        var deadline = Environment.TickCount64 + 4000;
        while (translator.Calls < 3 && Environment.TickCount64 < deadline) await Task.Delay(20);
        await Task.Delay(200);
        Program.Check(translator.Calls == 3, "LCTA: same line retries capped at three");
        translator.Fail = false;
        pipeline.InvalidateTranslation();
        deadline = Environment.TickCount64 + 4000;
        while (pipeline.Stats.Translations == 0 && Environment.TickCount64 < deadline) await Task.Delay(20);
        await pipeline.StopAsync();
        Program.Check(pipeline.Stats.Translations == 1, "LCTA: explicit retry recovers stopped request");
    }

    private sealed class CountingTranslator : ITranslator
    {
        public string Id => "lcta-test";
        public bool RequiresNetwork => false;
        public int Calls;
        public volatile bool Fail;
        public string? LastText;
        public Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastText = request.Text;
            Interlocked.Increment(ref Calls);
            if (Fail) throw new HttpRequestException("test");
            return Task.FromResult("测试译文");
        }
    }
}
