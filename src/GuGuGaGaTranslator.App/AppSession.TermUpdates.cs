using System.IO;
using GuGuGaGaTranslator.Core.Translation;

namespace GuGuGaGaTranslator.App;

public sealed partial class AppSession
{
    public string TermLibraryVersion => Config.Updates.TermBaseline?.ProfileVersion ?? "1.4.0";

    private void LoadBundledTermLibrary()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "terminology", "limbus-company.ggprofile.json");
        if (!File.Exists(path)) return;
        try
        {
            var profile = GameProfileArchive.Read(path);
            if (GuGuGaGaTranslator.Core.Updates.UpdateClient.Newer(profile.ProfileVersion ?? "", TermLibraryVersion))
                ApplyPublicTermLibrary(profile);
        }
        catch (Exception) { Notice?.Invoke("内置术语库加载失败，沿用已有术语；可在更新页重试。"); }
    }

    public Task UpdateTermLibraryAsync(GameProfile incoming) => ChangeSettingsAsync(() =>
    {
        ApplyPublicTermLibrary(incoming);
        ForgetContext();
        ProfileChanged?.Invoke(ActiveProfile);
        return Task.CompletedTask;
    });

    private void ApplyPublicTermLibrary(GameProfile incoming)
    {
        if (incoming.Id != "limbus-company") throw new InvalidDataException("公共词库 ID 不匹配。");
        if (!Version.TryParse(incoming.ProfileVersion, out _)) throw new InvalidDataException("公共词库版本无效。");
        if (!GuGuGaGaTranslator.Core.Updates.UpdateClient.Newer(incoming.ProfileVersion!, TermLibraryVersion)) return;
        var incomingProblems = GameProfileArchive.Validate(incoming);
        if (incomingProblems.Count > 0) throw new InvalidDataException(incomingProblems[0]);
        var previous = Config.Updates.TermBaseline ?? GameProfiles.Default().First(profile => profile.Id == incoming.Id);
        var local = Config.Translation.GameProfiles.FirstOrDefault(profile => profile.Id == incoming.Id);
        if (local is null)
        {
            // 保存新基线但保留整个档案的删除，避免反复提示同一份更新。
            var baseline = Config.Updates.TermBaseline;
            Config.Updates.TermBaseline = GameProfileArchive.Clone(incoming);
            try { SaveConfig(); }
            catch { Config.Updates.TermBaseline = baseline; throw; }
            return;
        }
        var merged = GameProfileArchive.Clone(local);
        merged.Terms = TermLibraryMerge.Merge(previous, incoming, local);
        merged.ProfileVersion = incoming.ProfileVersion;
        var problems = GameProfileArchive.Validate(merged);
        if (problems.Count > 0) throw new InvalidDataException(problems[0]);
        var index = Config.Translation.GameProfiles.IndexOf(local);
        var oldBaseline = Config.Updates.TermBaseline;
        Config.Translation.GameProfiles[index] = merged;
        Config.Updates.TermBaseline = GameProfileArchive.Clone(incoming);
        try { SaveConfig(); }
        catch
        {
            Config.Translation.GameProfiles[index] = local;
            Config.Updates.TermBaseline = oldBaseline;
            throw;
        }
    }
}
