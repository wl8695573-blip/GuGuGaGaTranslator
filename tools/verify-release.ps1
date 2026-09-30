param([string]$Version = '1.2.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sandbox = Join-Path $root ('.artifacts\verify-' + [guid]::NewGuid().ToString('N').Substring(0,8))
$installDir = Join-Path $sandbox 'installed'
$configDir = Join-Path $sandbox 'config'
$setup = Join-Path $root "dist\GuGuGaGaTranslator-Setup-$Version.exe"
$registryPath = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\GuGuGaGaTranslator'
$savedKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath)
$savedValues = @{}
if ($savedKey) {
    foreach ($name in $savedKey.GetValueNames()) { $savedValues[$name] = @($savedKey.GetValue($name), $savedKey.GetValueKind($name)) }
    $savedKey.Dispose()
}
$shortcutPaths = @(
    (Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\GuGuGaGaTranslator.lnk'),
    (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'GuGuGaGaTranslator.lnk')
)
$savedShortcuts = @{}
foreach ($path in $shortcutPaths) { if (Test-Path -LiteralPath $path) { $savedShortcuts[$path] = [IO.File]::ReadAllBytes($path) } }
New-Item -ItemType Directory -Path $installDir,$configDir -Force | Out-Null
function Run-Checked([string] $Exe, [string] $Arguments, [int] $Expected = 0) {
    $process = Start-Process -FilePath $Exe -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(30000)) { throw "Process did not finish: $Exe" }
    if ($process.ExitCode -ne $Expected) { throw "Unexpected exit code $($process.ExitCode): $Exe" }
}
try {
    $blocked = Join-Path $sandbox 'occupied'
    New-Item -ItemType Directory -Path $blocked | Out-Null
    [IO.File]::WriteAllText((Join-Path $blocked 'keep.txt'), 'user data')
    Run-Checked $setup "--silent --dir `"$blocked`"" 1
    if ((Get-ChildItem -LiteralPath $blocked).Count -ne 1) { throw 'Installer modified occupied directory' }
    Write-Host 'PASS rejects occupied directory'
    Run-Checked $setup "--silent --dir `"$installDir`""
    $marker = Join-Path $installDir '.gugugaga-install.json'
    if (-not (Test-Path -LiteralPath $marker)) { throw 'Missing install manifest' }
    $manifest = Get-Content -LiteralPath $marker -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($file in $manifest.Files) { if (-not (Test-Path -LiteralPath (Join-Path $installDir $file))) { throw "Missing installed file: $file" } }
    Write-Host 'PASS install manifest matches files'
    $exe = Join-Path $installDir 'GuGuGaGaTranslator.exe'
    if ((Get-Item -LiteralPath $exe).VersionInfo.FileVersion -ne "$Version.0") { throw 'App version mismatch' }
    [IO.File]::WriteAllText((Join-Path $configDir 'config.json'), '{"setupCompleted":true,"translation":{"translator":{"provider":"mock"}}}')
    Run-Checked $exe "--config-dir `"$configDir`" --exit-after 2"
    Write-Host 'PASS installed app startup'
    $extra = Join-Path $installDir 'user-notes.txt'
    [IO.File]::WriteAllText($extra, 'must survive uninstall')
    Run-Checked $setup "--silent --dir `"$installDir`""
    Write-Host 'PASS upgrade registered directory'
    Run-Checked $exe '--uninstall --quiet'
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while ((Test-Path -LiteralPath $exe) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
    if (Test-Path -LiteralPath $exe) { throw 'Uninstaller left the executable' }
    if ([IO.File]::ReadAllText($extra) -ne 'must survive uninstall') { throw 'Uninstaller removed user file' }
    Write-Host 'PASS uninstall preserves user file'
    $portable = Join-Path $sandbox 'portable'
    Expand-Archive -LiteralPath (Join-Path $root "dist\GuGuGaGaTranslator-win-x64-$Version.zip") -DestinationPath $portable
    $portableExe = Join-Path $portable 'GuGuGaGaTranslator\GuGuGaGaTranslator.exe'
    Run-Checked $portableExe '--uninstall --quiet'
    if (-not (Test-Path -LiteralPath $portableExe)) { throw 'Portable uninstall removed files' }
    Write-Host 'PASS portable copy refuses uninstall'
    Write-Host "Acceptance artifacts: $sandbox"
}
finally {
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($registryPath, $false)
    if ($savedValues.Count) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath)
        foreach ($name in $savedValues.Keys) { $key.SetValue($name, $savedValues[$name][0], $savedValues[$name][1]) }
        $key.Dispose()
    }
    foreach ($path in $shortcutPaths) {
        if ($savedShortcuts.ContainsKey($path)) { [IO.File]::WriteAllBytes($path, $savedShortcuts[$path]) }
        elseif (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
}
