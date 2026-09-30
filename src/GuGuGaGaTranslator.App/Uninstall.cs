using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using GuGuGaGaTranslator.Installation;

namespace GuGuGaGaTranslator.App;

internal static class Uninstall
{
    internal const string ProductName = "GuGuGaGaTranslator";
    internal const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + ProductName;
    internal static string InstallDirectory => AppContext.BaseDirectory.TrimEnd('\\');
    private static InstallationManifest? _manifest;

    internal static bool Run(Action<string>? onMessage = null)
    {
        try
        {
            var directory = InstallationManifest.ValidateDirectory(InstallDirectory);
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
            if (key?.GetValue("InstallLocation") is not string registered
                || !Path.GetFullPath(registered).TrimEnd('\\').Equals(directory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("此目录没有登记为安装目录。免安装版请自行删除应用文件夹。");
            var manifest = InstallationManifest.Read(directory);
            if (key.GetValue("InstallationId") as string != manifest.Id)
                throw new InvalidOperationException("安装标记与卸载登记不匹配。");
            _manifest = manifest;
            ScheduleDirectoryRemoval(onMessage);
            foreach (var shortcut in new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", ProductName + ".lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ProductName + ".lnk") })
                if (File.Exists(shortcut)) File.Delete(shortcut);
            Registry.CurrentUser.DeleteSubKeyTree(RegistryKey, false);
            onMessage?.Invoke("卸载已安排，将在程序退出后删除安装清单中的文件。用户添加的文件会保留。");
            return true;
        }
        catch (Exception exception)
        {
            onMessage?.Invoke($"卸载失败：{exception.Message}");
            return false;
        }
    }

    private static void ScheduleDirectoryRemoval(Action<string>? onMessage)
    {
        if (_manifest is null) throw new InvalidOperationException("缺少安装清单。");
        var files = _manifest.Files.Append(InstallationManifest.FileName)
            .Select(file => InstallationManifest.ResolveFile(InstallDirectory, file)).ToArray();
        // Keep the file list out of the Windows command line (limited to 32,767 characters).
        // The helper reads JSON data; only a bounded, encoded path enters its command line.
        var payloadPath = Path.Combine(Path.GetTempPath(), "gggt-uninstall-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(payloadPath, JsonSerializer.Serialize(new { root = InstallDirectory, files, process = Environment.ProcessId }));
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadPath));
        var script = """
            $ErrorActionPreference = 'Stop'
            $payloadPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('PAYLOAD'))
            try {
            $data = [IO.File]::ReadAllText($payloadPath) | ConvertFrom-Json
            Wait-Process -Id $data.process -ErrorAction SilentlyContinue
            $root = [IO.Path]::GetFullPath($data.root).TrimEnd('\')
            if ($root -eq [IO.Path]::GetPathRoot($root).TrimEnd('\')) { exit 1 }
            $dirs = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($file in $data.files) {
                $full = [IO.Path]::GetFullPath($file)
                if (-not $full.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { exit 1 }
                $current = $full
                while ($current) {
                    if (Test-Path -LiteralPath $current) {
                        if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { exit 1 }
                    }
                    $current = [IO.Path]::GetDirectoryName($current)
                }
            }
            foreach ($file in $data.files) {
                if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file -Force }
                $dir = [IO.Path]::GetDirectoryName($file)
                while ($dir -and ($dir -eq $root -or $dir.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase))) {
                    [void]$dirs.Add($dir)
                    $dir = [IO.Path]::GetDirectoryName($dir)
                }
            }
            foreach ($dir in ($dirs | Sort-Object Length -Descending)) {
                if ((Test-Path -LiteralPath $dir) -and -not [IO.Directory]::EnumerateFileSystemEntries($dir).GetEnumerator().MoveNext()) {
                    [IO.Directory]::Delete($dir, $false)
                }
            }
            } finally { Remove-Item -LiteralPath $payloadPath -Force -ErrorAction SilentlyContinue }
            """.Replace("PAYLOAD", payload);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        try
        {
            using var process = Process.Start(new ProcessStartInfo(powershell, "-NoProfile -NonInteractive -EncodedCommand " + encoded)
            {
                WorkingDirectory = Path.GetTempPath(), CreateNoWindow = true,
                UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden,
            }) ?? throw new InvalidOperationException("无法启动卸载清理进程。");
        }
        catch { File.Delete(payloadPath); throw; }
    }
}
