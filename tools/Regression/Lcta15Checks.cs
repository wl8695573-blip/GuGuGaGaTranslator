using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GuGuGaGaTranslator.App;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Translation;
using GuGuGaGaTranslator.Core.Updates;

internal static class Lcta15Checks
{
    public static async Task RunAsync(string scratch)
    {
        var directions = DefaultLanguagePresets.Create().Where(pair => pair.From != "auto").ToArray();
        Program.Check(directions.Length == 12 && directions.Select(pair => pair.From + "|" + pair.To).Distinct().Count() == 12,
            "LCTA 1.5: all twelve four-language directions");
        Program.Check(DefaultLanguagePresets.IsSupported("ko", false) && DefaultLanguagePresets.IsSupportedOcr("ko-KR"), "LCTA 1.5: Korean survives config validation");
        Program.Check(TermSheet.Parse("ko: 오티스 = 奥提斯")[0].Language == "ko", "LCTA 1.5: Korean term sheet round trip");
        var config = new AppConfig { Target = new() { Region = new(10, 400, 500, 100) } };
        config.NormalizeLanguages();
        Program.Check(config.Target.ManualRegion, "LCTA 1.5: old saved region remains manual");

        var baseline = new GameProfile { Id = "limbus-company", Name = "test", ProfileVersion = "1.0", Terms =
            [new() { Source = "Alice", Target = "旧甲" }, new() { Source = "Bob", Target = "旧乙" }] };
        var local = GameProfileArchive.Clone(baseline);
        local.Terms[0].Target = "个人甲";
        local.Terms.RemoveAt(1);
        var incoming = GameProfileArchive.Clone(baseline);
        incoming.ProfileVersion = "2.0";
        incoming.Terms[0].Target = "公共新甲";
        incoming.Terms.Add(new() { Source = "Carol", Target = "丙" });
        var merged = TermLibraryMerge.Merge(baseline, incoming, local);
        Program.Check(merged.Single(term => term.Source == "Alice").Target == "个人甲", "LCTA 1.5: public update preserves user edit");
        Program.Check(merged.All(term => term.Source != "Bob"), "LCTA 1.5: public update preserves user deletion");
        Program.Check(merged.Any(term => term.Source == "Carol"), "LCTA 1.5: public update adds new term");
        Program.Check(TermLibraryMerge.Merge(baseline, incoming, baseline).Single(term => term.Source == "Alice").Target == "公共新甲",
            "LCTA 1.5: unchanged term follows public update");
        await using var session = new AppSession(Path.Combine(scratch, "lcta15"));
        await session.ApplyLanguagePresetAsync(new() { From = "en", To = "zh-Hans", Ocr = "en-US" });
        session.Config.Translation.PersonalTerms.Add(new() { From = "en", To = "zh-Hans", Source = "Outis", Target = "个人奥提斯" });
        await session.ApplyProfileAsync(GameProfiles.Default()[0]);
        Program.Check(session.CurrentProfile.Glossary.Single(term => term.Source == "Outis").Target == "个人奥提斯", "LCTA 1.5: personal term overrides profile immediately");
        await session.ApplyLanguagePresetAsync(new() { From = "ja", To = "zh-Hans", Ocr = "ja" });
        Program.Check(session.CurrentProfile.Glossary.All(term => term.Target != "个人奥提斯"), "LCTA 1.5: personal term isolated by direction");
        Program.Check(UpdateClient.Newer("v1.5.0", "1.4.0") && !UpdateClient.Newer("1.3.2", "1.5.0"), "LCTA 1.5: update comparison rejects downgrade");

        var bytes = Encoding.UTF8.GetBytes(GameProfileArchive.Serialize(incoming));
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var http = new HttpClient(new Handler(uri => uri.AbsolutePath.EndsWith("manifest.json")
            ? Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { schemaVersion = 1, version = "2.0", file = "limbus-company.ggprofile.json", sha256 = hash }))
            : bytes));
        var client = new UpdateClient(http);
        var offer = await client.CheckTermsAsync(CancellationToken.None);
        var downloaded = await client.DownloadTermsAsync(offer, CancellationToken.None);
        Program.Check(downloaded.ProfileVersion == "2.0", "LCTA 1.5: public library download validates hash and version");
        try { await client.DownloadTermsAsync(offer with { Sha256 = new string('0', 64) }, CancellationToken.None); throw new Exception("Unexpected success"); }
        catch (InvalidDataException) { Program.Check(true, "LCTA 1.5: mismatched library hash rejected"); }
        try { await client.DownloadTermsAsync(offer with { Url = new Uri("https://example.invalid/profile.json") }, CancellationToken.None); throw new Exception("Unexpected success"); }
        catch (InvalidDataException) { Program.Check(true, "LCTA 1.5: foreign library source rejected"); }
        await CheckInstallerUpdatesAsync(scratch);
        await CheckTermPersistenceAsync(scratch);
        foreach (var direction in directions)
            Program.Check(CaiyunTranslator.SupportsDirection(direction.From, direction.To)
                == (direction.From == "zh-Hans" || direction.To == "zh-Hans"),
                $"LCTA 1.5: Caiyun direction availability {direction.From}->{direction.To}");
        Program.Check(CaiyunTranslator.SupportsDirection("auto", "zh-Hans"), "LCTA 1.5: Caiyun automatic Chinese translation retained");
        using var forbiddenHttp = new HttpClient(new Handler(_ => throw new Exception("Unexpected HTTP request")));
        using var caiyun = new CaiyunTranslator(new(Token: "TEST"), forbiddenHttp);
        try { await caiyun.TranslateAsync(new() { Text = "Hello", From = "en", To = "ja" }); throw new Exception("Unexpected success"); }
        catch (UnsupportedTranslationDirectionException)
        { Program.Check(true, "LCTA 1.5: unsupported Caiyun direction rejected before network request"); }
    }

    private static async Task CheckInstallerUpdatesAsync(string scratch)
    {
        const string version = "1.6.0";
        const string name = "LCTA-Setup-1.6.0.exe";
        var root = "https://github.com/" + UpdateClient.Repository + "/releases/download/v" + version + "/";
        var installer = Encoding.ASCII.GetBytes("LCTA MOCK INSTALLER, NEVER EXECUTED");
        var hash = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        var corrupted = false;
        using var http = new HttpClient(new Handler(uri =>
        {
            if (uri.Host == "api.github.com") return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                tag_name = "v" + version, prerelease = false,
                assets = new[]
                {
                    new { name, browser_download_url = root + name },
                    new { name = "SHA256SUMS-" + version + ".txt", browser_download_url = root + "SHA256SUMS-" + version + ".txt" }
                }
            }));
            if (uri.AbsolutePath.EndsWith(".txt")) return Encoding.ASCII.GetBytes(hash + "  " + name + "\n");
            return corrupted ? Encoding.ASCII.GetBytes("INVALID") : installer;
        }));
        var client = new UpdateClient(http);
        var offer = await client.CheckAppAsync("1.5.0", CancellationToken.None);
        Program.Check(offer is { Version: version, InstallerName: name }, "LCTA 1.5: discovers matching official installer and checksum assets");
        Program.Check(await client.CheckAppAsync("1.6.0", CancellationToken.None) is null, "LCTA 1.5: installed version does not offer itself again");
        var directory = Path.Combine(scratch, "installer-update");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, name);
        await client.DownloadInstallerAsync(offer!, destination, CancellationToken.None);
        Program.Check(File.ReadAllBytes(destination).SequenceEqual(installer), "LCTA 1.5: installer downloads atomically after SHA256 validation");
        corrupted = true;
        try { await client.DownloadInstallerAsync(offer!, destination, CancellationToken.None); throw new Exception("Unexpected success"); }
        catch (InvalidDataException)
        { Program.Check(File.ReadAllBytes(destination).SequenceEqual(installer) && !Directory.EnumerateFiles(directory, "*.download").Any(),
            "LCTA 1.5: bad installer hash preserves old file and removes partial download"); }
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        try { await client.DownloadInstallerAsync(offer!, destination, cancel.Token); throw new Exception("Unexpected success"); }
        catch (OperationCanceledException)
        { Program.Check(File.ReadAllBytes(destination).SequenceEqual(installer), "LCTA 1.5: canceled installer download preserves existing file"); }
        try { await client.DownloadInstallerAsync(offer! with { Installer = new Uri("https://example.invalid/setup.exe") }, destination, CancellationToken.None); throw new Exception("Unexpected success"); }
        catch (InvalidDataException) { Program.Check(true, "LCTA 1.5: foreign installer URL rejected before download"); }
    }

    private static async Task CheckTermPersistenceAsync(string scratch)
    {
        var directory = Path.Combine(scratch, "public-terms-persistence");
        var baseline = new GameProfile { Id = "limbus-company", Name = "test", ProfileVersion = "2026.10.8.1",
            Terms = [new() { Source = "Outis", Target = "旧译名" }, new() { Source = "Nest", Target = "巢" }] };
        var incoming = GameProfileArchive.Clone(baseline);
        incoming.ProfileVersion = "2026.10.8.2";
        incoming.Terms[0].Target = "公共新译名";
        incoming.Terms[1].Target = "公共新巢";
        incoming.Terms.Add(new() { Source = "NewName", Target = "新术语" });
        await using (var session = new AppSession(directory))
        {
            session.Config.Updates.TermBaseline = GameProfileArchive.Clone(baseline);
            var local = GameProfileArchive.Clone(baseline);
            local.Terms[0].Enabled = false;
            session.Config.Translation.GameProfiles = [local];
            session.Config.Translation.PersonalTerms = [new() { From = "en", To = "zh-Hans", Source = "Outis", Target = "个人译名" }];
            await session.UpdateTermLibraryAsync(incoming);
            Program.Check(session.TermLibraryVersion == "2026.10.8.2" && !session.FindProfile("limbus-company")!.Terms[0].Enabled,
                "LCTA 1.5: public update advances baseline while retaining user disabled term");
            Program.Check(session.FindProfile("limbus-company")!.Terms.Single(term => term.Source == "Nest").Target == "公共新巢"
                && session.Config.Translation.PersonalTerms.Single().Target == "个人译名", "LCTA 1.5: public update retains personal list and updates unchanged term");
            await session.UpdateTermLibraryAsync(baseline);
            Program.Check(session.TermLibraryVersion == "2026.10.8.2", "LCTA 1.5: term update cannot downgrade baseline");
        }
        await using (var restored = new AppSession(directory))
        {
            restored.LoadConfig();
            Program.Check(restored.TermLibraryVersion == "2026.10.8.2" && restored.FindProfile("limbus-company")!.Terms.Any(term => term.Source == "NewName"),
                "LCTA 1.5: public library and baseline survive restarting session");
            restored.Config.Translation.GameProfiles.Clear();
            incoming.ProfileVersion = "2026.10.8.3";
            await restored.UpdateTermLibraryAsync(incoming);
            Program.Check(restored.FindProfile("limbus-company") is null && restored.TermLibraryVersion == "2026.10.8.3",
                "LCTA 1.5: deleted game profile stays deleted without repeat update prompt");
        }
        var failedDirectory = Path.Combine(scratch, "public-terms-save-failure");
        await using var failed = new AppSession(failedDirectory);
        failed.Config.Updates.TermBaseline = baseline;
        failed.Config.Translation.GameProfiles = [GameProfileArchive.Clone(baseline)];
        Directory.CreateDirectory(Path.Combine(failedDirectory, "config.json"));
        try { await failed.UpdateTermLibraryAsync(incoming); throw new Exception("Unexpected success"); }
        catch (IOException)
        { Program.Check(failed.TermLibraryVersion == baseline.ProfileVersion
            && failed.FindProfile("limbus-company")!.Terms[0].Target == "旧译名", "LCTA 1.5: failed term save rolls back profile and baseline"); }
        catch (UnauthorizedAccessException)
        { Program.Check(failed.TermLibraryVersion == baseline.ProfileVersion
            && failed.FindProfile("limbus-company")!.Terms[0].Target == "旧译名", "LCTA 1.5: denied term save rolls back profile and baseline"); }
    }

    private sealed class Handler(Func<Uri, byte[]> content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content(request.RequestUri!)) });
        }
    }
}
