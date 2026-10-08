using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GuGuGaGaTranslator.Core.Config;

namespace GuGuGaGaTranslator.Core.Translation;

/// <summary>A portable profile format with versioning and no connection configuration or credentials.</summary>
public static class GameProfileArchive
{
    private const int MaximumBytes = 1024 * 1024;
    private sealed record Envelope(string Format, int SchemaVersion, GameProfile Profile);
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static GameProfile Clone(GameProfile profile) =>
        JsonSerializer.Deserialize<GameProfile>(JsonSerializer.Serialize(profile, Json), Json)!;

    public static string Serialize(GameProfile profile)
    {
        RequireValid(profile);
        var json = JsonSerializer.Serialize(new Envelope("gugugaga-game-profile", 2, profile), Json);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes)
            throw new InvalidDataException("档案超过 1 MB，请精简术语和说明。");
        return json;
    }

    public static GameProfile Deserialize(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes)
            throw new InvalidDataException("档案超过 1 MB。");
        Envelope? archive;
        try
        {
            archive = JsonSerializer.Deserialize<Envelope>(json, Json);
        }
        catch (JsonException error) { throw new InvalidDataException("档案格式不正确，或含有当前版本不支持的字段。", error); }
        if (archive is null || archive.Format != "gugugaga-game-profile" || archive.SchemaVersion is not (1 or 2) || archive.Profile is null)
            throw new InvalidDataException("不支持此档案格式或版本。");
        RequireValid(archive.Profile);
        return archive.Profile;
    }

    public static GameProfile Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumBytes)
            throw new InvalidDataException("档案超过 1 MB。");
        using var reader = new StreamReader(stream);
        return Deserialize(reader.ReadToEnd());
    }

    public static void Write(string path, GameProfile profile)
    {
        var text = Serialize(profile);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static List<string> Validate(GameProfile profile)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 200)
            problems.Add("名称须为 1–200 个字符。");
        if (profile.Id is null || profile.Id.Length > 200)
            problems.Add("档案 ID 过长。");
        if (profile.WindowHints is null || profile.WindowHints.Count > 50
            || profile.WindowHints.Any(hint => string.IsNullOrWhiteSpace(hint) || hint.Length > 200))
            problems.Add("窗口关键字须为 1–200 个字符，最多 50 个。");
        if (profile.ProcessHints is null || profile.ProcessHints.Count > 50
            || profile.ProcessHints.Any(hint => string.IsNullOrWhiteSpace(hint) || hint.Length > 200))
            problems.Add("进程名称须为 1–200 个字符，最多 50 个。");
        if (profile.Terms is null || profile.Terms.Count > 5000)
            problems.Add("术语表最多包含 5000 条。");
        if (!string.IsNullOrEmpty(profile.From) && !DefaultLanguagePresets.IsSupported(profile.From, true))
            problems.Add("源语言仅支持中、日、英或 auto。");
        if (!string.IsNullOrEmpty(profile.To) && !DefaultLanguagePresets.IsSupported(profile.To, false))
            problems.Add("目标语言仅支持中、日、英。");
        if (!string.IsNullOrEmpty(profile.OcrLanguage) && !DefaultLanguagePresets.IsSupportedOcr(profile.OcrLanguage))
            problems.Add("识别语言不受支持。");
        foreach (var text in new[] { profile.Note, profile.Worldview, profile.StyleHint })
            if (text?.Length > 20000)
                problems.Add("档案说明过长，单项最多 20000 个字符。");
        foreach (var text in new[] { profile.Author, profile.License, profile.ProfileVersion })
            if (text?.Length > 500)
                problems.Add("作者、许可或版本信息过长。");
        if (!string.IsNullOrWhiteSpace(profile.SourceUrl)
            && (profile.SourceUrl.Length > 2048 || !Uri.TryCreate(profile.SourceUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || !string.IsNullOrEmpty(uri.UserInfo)))
            problems.Add("来源须为不含账户密码的 http/https 地址。");
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in profile.Terms ?? [])
        {
            if (term is null || string.IsNullOrWhiteSpace(term.Source) || string.IsNullOrWhiteSpace(term.Target)
                || term.Source.Length > 1000 || term.Target.Length > 1000
                || term.Forbidden is null || term.Forbidden.Count > 100
                || term.Forbidden.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 1000)
                || term.Note?.Length > 2000)
            {
                problems.Add("术语含有空原文、空译名或过长的字段。");
                continue;
            }
            var tag = string.IsNullOrWhiteSpace(term.Language) ? "*" : GameProfiles.LanguageOf(term.Language);
            if (!string.IsNullOrWhiteSpace(term.SourceUrl)
                && (term.SourceUrl.Length > 2048 || !Uri.TryCreate(term.SourceUrl, UriKind.Absolute, out var source)
                    || source.Scheme is not ("http" or "https")))
                problems.Add("术语出处须为 HTTP/HTTPS 地址。");
            if (term.ReviewStatus?.Length > 100)
                problems.Add("术语核对状态过长。");
            if (tag is null)
            {
                problems.Add("术语的语言标签仅支持 en / ja / zh。");
                continue;
            }
            var key = tag + ":" + term.Source.Trim();
            if (seen.TryGetValue(key, out var target))
                problems.Add(target == term.Target.Trim() ? $"重复术语：{key}" : $"术语译名冲突：{key}");
            else
                seen.Add(key, term.Target.Trim());
        }
        return problems.Distinct().Take(20).ToList();
    }

    private static void RequireValid(GameProfile profile)
    {
        var problems = Validate(profile);
        if (problems.Count > 0)
            throw new InvalidDataException(string.Join(Environment.NewLine, problems));
    }
}
