using System.IO;
using Microsoft.Win32;

namespace GuGuGaGaTranslator.Core.Config;

/// <summary>注册表只记录数据目录位置，配置和密钥均留在所选目录。</summary>
public static class DataDirectory
{
    private const string RegistryPath = @"Software\LCTA";
    public static string LegacyDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GuGuGaGaTranslator");

    public static string Resolve()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            if (key?.GetValue("DataDirectory") is string path && Path.IsPathFullyQualified(path))
                return Path.GetFullPath(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
        return LegacyDirectory;
    }

    public static void Remember(string directory)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        key.SetValue("DataDirectory", Path.GetFullPath(directory), RegistryValueKind.String);
    }

    public static string Location(string configured, string root, string child) =>
        string.IsNullOrWhiteSpace(configured) ? Path.Combine(root, child) : Validate(configured);

    public static string Validate(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("请使用完整目录路径，例如 X:\\LCTA-data。");
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (full.Length == 0 || full == Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar))
            throw new ArgumentException("请选择磁盘中的文件夹，不能直接使用磁盘根目录。");
        if (File.Exists(full)) throw new ArgumentException("此路径指向文件，请选择文件夹。");
        return full;
    }

    public static void CopyForMigration(string source, string destination, CancellationToken cancellationToken = default)
    {
        var root = Validate(source);
        var next = Validate(destination);
        if (next.Equals(root, StringComparison.OrdinalIgnoreCase)
            || next.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || root.StartsWith(next + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("新旧数据目录不能相同，也不能互相包含。");
        for (var item = new DirectoryInfo(next); item is not null; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("数据迁移目录不能经过目录联接或符号链接。");
        if (Directory.Exists(next) && Directory.EnumerateFileSystemEntries(next).Any())
            throw new IOException("请选择空目录，避免覆盖已有文件。");
        if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("旧数据目录包含链接，请先移除链接再迁移。");
        var entries = new List<string>();
        var directories = new Stack<string>();
        if (Directory.Exists(root)) directories.Push(root);
        while (directories.TryPop(out var current))
            foreach (var item in Directory.EnumerateFileSystemEntries(current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("旧数据目录包含链接，请先移除链接再迁移。");
                entries.Add(item);
                if (Directory.Exists(item)) directories.Push(item);
            }
        Directory.CreateDirectory(next);
        foreach (var item in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(next, Path.GetRelativePath(root, item));
            if (Directory.Exists(item)) Directory.CreateDirectory(target);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = File.OpenRead(item);
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[131072];
                int read;
                while ((read = input.Read(buffer)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                }
            }
        }
    }
}
