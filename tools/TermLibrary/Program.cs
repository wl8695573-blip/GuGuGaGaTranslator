using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GuGuGaGaTranslator.Core.Translation;

var positional = args.Where(value => !value.StartsWith("--", StringComparison.Ordinal)).ToArray();
var directory = Path.GetFullPath(positional.Length > 0 ? positional[0] : "terminology");
Directory.CreateDirectory(directory);
const string name = "limbus-company.ggprofile.json";
var path = Path.Combine(directory, name);
var profile = File.Exists(path) && !args.Contains("--from-code")
    ? GameProfileArchive.Read(path) : GameProfiles.Default().First(profile => profile.Id == "limbus-company");
if (profile.Id != "limbus-company") throw new InvalidDataException("公共词库 ID 须为 limbus-company。");
if (args.Contains("--check"))
{
    using var existing = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
    var root = existing.RootElement;
    var checksum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("file").GetString() != name
        || root.GetProperty("version").GetString() != profile.ProfileVersion || root.GetProperty("sha256").GetString() != checksum
        || !Version.TryParse(profile.ProfileVersion, out _)) throw new InvalidDataException("公共词库版本或校验清单不匹配。");
    Console.WriteLine($"PASS {profile.Terms.Count} terms, version {profile.ProfileVersion}, matching SHA256 manifest.");
    return;
}
var version = positional.Length > 1 ? positional[1] : profile.ProfileVersion ?? "2026.10.8.1";
if (!Version.TryParse(version, out var requested)) throw new ArgumentException("词库版本须为数字版本号。");
if (Version.TryParse(profile.ProfileVersion, out var current) && requested < current)
    throw new InvalidDataException("不能降低公共词库版本。");
profile.ProfileVersion = version;
var bytes = new UTF8Encoding(false).GetBytes(GameProfileArchive.Serialize(profile));
File.WriteAllBytes(path, bytes);
var manifest = new { schemaVersion = 1, version, file = name, sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() };
File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
Console.WriteLine($"Exported {profile.Terms.Count} terms, version {version}, SHA256 verified manifest generated.");
