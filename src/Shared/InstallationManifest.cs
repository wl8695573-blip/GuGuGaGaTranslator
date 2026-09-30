using System.IO;
using System.Text.Json;

namespace GuGuGaGaTranslator.Installation;

public sealed record InstallationManifest(string Product, string Id, string[] Files)
{
    public const string ProductName = "GuGuGaGaTranslator";
    public const string FileName = ".gugugaga-install.json";

    public static string UserFolder(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException($"无法定位当前用户目录：{folder}。");
        return path;
    }

    public static string ValidateDirectory(string directory)
    {
        var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(full, Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("不能使用磁盘根目录作为安装目录。");
        for (var current = new DirectoryInfo(full); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("安装目录不能经过符号链接或目录联接。");
        return full;
    }

    public static string ResolveFile(string directory, string relative)
    {
        var root = ValidateDirectory(directory);
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new InvalidOperationException("安装文件路径无效。");
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("安装文件超出目标目录。");
        ValidateDirectory(Path.GetDirectoryName(full)!);
        if (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("安装文件不能是符号链接。");
        return full;
    }

    public static InstallationManifest Read(string directory)
    {
        var manifest = JsonSerializer.Deserialize<InstallationManifest>(
            File.ReadAllText(ResolveFile(directory, FileName)))
            ?? throw new InvalidOperationException("安装清单无效。");
        if (manifest.Product != ProductName || !Guid.TryParse(manifest.Id, out _) ||
            manifest.Files is null || !manifest.Files.Contains(ProductName + ".exe", StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("安装清单不属于此产品。");
        foreach (var file in manifest.Files) ResolveFile(directory, file);
        return manifest;
    }

    public void Save(string directory) =>
        File.WriteAllText(ResolveFile(directory, FileName), JsonSerializer.Serialize(this));
}
