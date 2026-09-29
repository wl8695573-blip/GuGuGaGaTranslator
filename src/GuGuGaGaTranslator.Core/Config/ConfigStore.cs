using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuGuGaGaTranslator.Core.Config;

/// <summary>Reads and writes the single configuration file under the user's application data. A corrupt
/// file never stops the tool: it is moved aside, the defaults are used, and the reason is reported.</summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public ConfigStore(string? directory = null)
    {
        Directory = directory ?? DefaultDirectory();
        FilePath = Path.Combine(Directory, "config.json");
    }

    public string Directory { get; }

    public string FilePath { get; }

    /// <summary>Why the last load fell back to defaults, or null when it succeeded.</summary>
    public string? LastLoadError { get; private set; }

    public static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GuGuGaGaTranslator");

    public AppConfig Load()
    {
        LastLoadError = null;
        if (!File.Exists(FilePath)) return new AppConfig();

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // Keep the unreadable file: it is the only copy of hand-edited settings.
            LastLoadError = $"{exception.GetType().Name}: {exception.Message}";
            TryBackup();
            return new AppConfig();
        }
    }

    /// <summary>Save the configuration; it is written to a temporary file first so an interrupted write cannot leave a half-written file.</summary>
    public void Save(AppConfig config)
    {
        // Fully qualified: this class's own Directory property shadows the type.
        System.IO.Directory.CreateDirectory(Directory);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(config, Options));
        File.Move(temporary, FilePath, overwrite: true);
    }

    private void TryBackup()
    {
        try
        {
            var target = $"{FilePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(FilePath, target, overwrite: true);
            LastLoadError += $" (kept as {Path.GetFileName(target)})";
        }
        catch (IOException)
        {
            // Reporting the original error matters more than the backup.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
