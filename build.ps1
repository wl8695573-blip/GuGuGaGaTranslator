[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [switch] $Publish,
    [switch] $SelfContained,
    [switch] $SingleFile,
    [switch] $Zip,
    [switch] $Installer
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Encoding UTF8
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version' }
dotnet build (Join-Path $root 'GuGuGaGaTranslator.slnx') -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
if (-not ($Publish -or $Zip -or $Installer)) { return }
if ($SingleFile -or $Installer) { $SelfContained = $true; $SingleFile = $true }
$staging = Join-Path $root ('.artifacts\release-' + $version + '-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $staging 'GuGuGaGaTranslator'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $app, $dist -Force | Out-Null
$arguments = @('publish', (Join-Path $root 'src\GuGuGaGaTranslator.App\GuGuGaGaTranslator.App.csproj'),
    '-c', $Configuration, '-r', $Runtime, '-o', $app, '--self-contained', $SelfContained.IsPresent.ToString().ToLowerInvariant(), '--nologo')
if ($SingleFile) { $arguments += @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true') }
dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'App publish failed' }
foreach ($name in @('PP-OCRv6_det_small.onnx','PP-OCRv6_rec_small.onnx','ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx','ppocrv6_small_dict.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root "models\v6\$name"))) { throw "Missing OCR model: $name" }
}
Copy-Item -LiteralPath (Join-Path $root 'models') -Destination (Join-Path $app 'models') -Recurse
foreach ($document in @('LICENSE','THIRD_PARTY_NOTICES.md','README.md','CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $root $document) -Destination $app
}
$licenses = Join-Path $root 'licenses'
if (Test-Path -LiteralPath $licenses) { Copy-Item -LiteralPath $licenses -Destination $app -Recurse }
# Carry the notices shipped with every restored package and runtime.
$assetsFile = Join-Path $root 'src\GuGuGaGaTranslator.App\obj\project.assets.json'
$assetData = Get-Content -LiteralPath $assetsFile -Raw -Encoding UTF8 | ConvertFrom-Json
$licenseOut = Join-Path $app 'licenses'
New-Item -ItemType Directory -Path $licenseOut -Force | Out-Null
foreach ($packageRoot in $assetData.packageFolders.PSObject.Properties.Name) {
    foreach ($library in $assetData.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') { continue }
        $packageDir = Join-Path $packageRoot $library.Value.path
        if (-not (Test-Path -LiteralPath $packageDir)) { continue }
        $destination = Join-Path $licenseOut ($library.Name.Replace('/', '-'))
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Get-ChildItem -LiteralPath $packageDir -File |
            Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE|THIRD-PARTY)' -or $_.Extension -eq '.nuspec' } |
            Copy-Item -Destination $destination
    }
    foreach ($runtimeName in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
        $runtimeRoot = Join-Path $packageRoot $runtimeName
        if (-not (Test-Path -LiteralPath $runtimeRoot)) { continue }
        foreach ($runtimeDir in Get-ChildItem -LiteralPath $runtimeRoot -Directory) {
            $destination = Join-Path $licenseOut ($runtimeName + '-' + $runtimeDir.Name)
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            Get-ChildItem -LiteralPath $runtimeDir.FullName -File |
                Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE|THIRD-PARTY)' } |
                Copy-Item -Destination $destination
        }
    }
}
Write-Host "Published $app"
if (-not ($Zip -or $Installer)) { return }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = Join-Path $dist "GuGuGaGaTranslator-$Runtime-$version.zip"
if (Test-Path -LiteralPath $archive) { throw "Release archive already exists: $archive. Move this exact artifact aside before rebuilding." }
$zipFile = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $app -Recurse -File) {
        if ($file.Extension -in '.pdb','.lib') { continue }
        $relative = $file.FullName.Substring($app.Length).TrimStart('\','/').Replace('\','/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zipFile, $file.FullName,
            "GuGuGaGaTranslator/$relative", [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $zipFile.Dispose() }
$assets = @($archive)
if ($Installer) {
    $payload = Join-Path $root 'tools\Installer\payload'
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    Copy-Item -LiteralPath $archive -Destination (Join-Path $payload 'app.zip') -Force
    $setupOutput = Join-Path $staging 'installer'
    dotnet publish (Join-Path $root 'tools\Installer\Installer.csproj') -c $Configuration -r $Runtime -o $setupOutput --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed' }
    $setup = Join-Path $dist "GuGuGaGaTranslator-Setup-$version.exe"
    if (Test-Path -LiteralPath $setup) { throw "Installer already exists: $setup" }
    Copy-Item -LiteralPath (Join-Path $setupOutput 'GuGuGaGaTranslator-Setup.exe') -Destination $setup
    $assets += $setup
}
$checksums = foreach ($file in $assets) { "{0}  {1}" -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($file) }
[IO.File]::WriteAllLines((Join-Path $dist "SHA256SUMS-$version.txt"), [string[]]$checksums, [Text.Encoding]::ASCII)
Write-Host "Release $version ready in $dist"
