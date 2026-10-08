using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GuGuGaGaTranslator.Core.Updates;

public sealed record TermUpdate(string Version, Uri Url, string Sha256);
public sealed record AppUpdate(string Version, Uri Page, Uri Installer, Uri Checksums, string InstallerName);

/// <summary>只读取维护者仓库的公开更新数据，不发送用户配置或密钥。</summary>
public sealed class UpdateClient
{
    public const string Repository = "wl8695573-blip/GuGuGaGaTranslator";
    public const string RawRoot = "https://raw.githubusercontent.com/" + Repository + "/main/terminology/";
    private static readonly HttpClient SharedHttp = CreateClient();
    private readonly HttpClient Http;
    public UpdateClient(HttpClient? client = null) => Http = client ?? SharedHttp;
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LCTA-Updater/1.5");
        return client;
    }

    public static bool Newer(string candidate, string current) =>
        System.Version.TryParse(candidate.TrimStart('v', 'V'), out var next)
        && (!System.Version.TryParse(current.TrimStart('v', 'V'), out var installed) || next > installed);

    public async Task<TermUpdate> CheckTermsAsync(CancellationToken cancellationToken)
    {
        var bytes = await ReadAsync(new Uri(RawRoot + "manifest.json"), 64 * 1024, cancellationToken).ConfigureAwait(false);
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        var version = root.GetProperty("version").GetString() ?? "";
        var name = root.GetProperty("file").GetString() ?? "";
        var sha = root.GetProperty("sha256").GetString() ?? "";
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || !System.Version.TryParse(version, out _)
            || name != "limbus-company.ggprofile.json" || sha.Length != 64 || !sha.All(Uri.IsHexDigit))
            throw new InvalidDataException("公共词库更新说明无效。");
        return new(version, new Uri(RawRoot + name), sha);
    }

    public async Task<Translation.GameProfile> DownloadTermsAsync(TermUpdate update, CancellationToken cancellationToken)
    {
        if (!update.Url.AbsoluteUri.StartsWith(RawRoot, StringComparison.Ordinal)) throw new InvalidDataException("术语来源不受支持。");
        var bytes = await ReadAsync(update.Url, 1024 * 1024, cancellationToken).ConfigureAwait(false);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("术语库校验失败，未修改已有词库。");
        var profile = Translation.GameProfileArchive.Deserialize(Encoding.UTF8.GetString(bytes));
        if (profile.Id != "limbus-company" || profile.ProfileVersion != update.Version)
            throw new InvalidDataException("词库版本或 ID 不匹配。");
        return profile;
    }

    public async Task<AppUpdate?> CheckAppAsync(string currentVersion, CancellationToken cancellationToken)
    {
        var bytes = await ReadAsync(new Uri("https://api.github.com/repos/" + Repository + "/releases/latest"), 1024 * 1024, cancellationToken).ConfigureAwait(false);
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        var version = (root.GetProperty("tag_name").GetString() ?? "").TrimStart('v', 'V');
        if (!Newer(version, currentVersion) || root.GetProperty("prerelease").GetBoolean()) return null;
        Uri? installer = null, checksums = null;
        var name = $"LCTA-Setup-{version}.exe";
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var assetName = asset.GetProperty("name").GetString();
            if (assetName != name && assetName != $"SHA256SUMS-{version}.txt") continue;
            var uri = new Uri(asset.GetProperty("browser_download_url").GetString()!);
            if (!uri.AbsoluteUri.StartsWith("https://github.com/" + Repository + "/releases/download/", StringComparison.Ordinal))
                throw new InvalidDataException("安装包来源不受支持。");
            if (assetName == name) installer = uri; else checksums = uri;
        }
        return installer is null || checksums is null ? null : new(version,
            new Uri("https://github.com/" + Repository + "/releases/tag/v" + version), installer, checksums, name);
    }

    public async Task DownloadInstallerAsync(AppUpdate update, string destination, CancellationToken cancellationToken)
    {
        var prefix = "https://github.com/" + Repository + "/releases/download/";
        if (!update.Installer.AbsoluteUri.StartsWith(prefix, StringComparison.Ordinal)
            || !update.Checksums.AbsoluteUri.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException("安装包来源不受支持。");
        var checksumText = Encoding.ASCII.GetString(await ReadAsync(update.Checksums, 64 * 1024, cancellationToken).ConfigureAwait(false));
        var expected = checksumText.Split('\n').Select(line => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && parts[1] == update.InstallerName).Select(parts => parts[0]).SingleOrDefault();
        if (expected is null || expected.Length != 64 || !expected.All(Uri.IsHexDigit)) throw new InvalidDataException("缺少安装包校验值。");
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            using var response = await Http.GetAsync(update.Installer, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 350 * 1024 * 1024) throw new InvalidDataException("安装包超过限制。");
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > 350 * 1024 * 1024) throw new InvalidDataException("安装包超过限制。");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装包校验失败，没有启动安装。");
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task<byte[]> ReadAsync(Uri uri, int limit, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("更新数据超过限制。");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("更新数据超过限制。");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
