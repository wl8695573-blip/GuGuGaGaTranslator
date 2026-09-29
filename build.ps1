<#
.SYNOPSIS
    Builds GuGuGaGaTranslator, and optionally publishes a runnable folder.

.DESCRIPTION
    With no switches this restores and builds the whole solution in Debug.
    -Publish copies a release build into dist\.
    -SelfContained additionally bundles the .NET runtime, so the result runs on
    a machine with no .NET installed (about 150 MB larger).
    -SingleFile packs everything into one GuGuGaGaTranslator.exe (implies -SelfContained,
    because a single file that still needs an installed runtime is not portable).
    -Zip produces the thing you actually send someone: dist\GuGuGaGaTranslator-win-x64-<date>.zip
    holding GuGuGaGaTranslator.exe, models\, and a short read-me. It leaves the debug symbols
    and import libraries out, because those are a megabyte of nothing to a player.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Configuration Release -Publish
    .\build.ps1 -Publish -SingleFile
    .\build.ps1 -Publish -SingleFile -Zip
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [string] $Runtime = 'win-x64',

    [switch] $Publish,

    [switch] $SelfContained,

    [switch] $SingleFile,

    [switch] $Zip
)

$ErrorActionPreference = 'Stop'

# PowerShell 7.4+ turns any native-command stderr line into a terminating error
# while ErrorActionPreference is Stop, which would abort this script on an
# ordinary dotnet restore message. Exit codes are checked explicitly instead.
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$root = $PSScriptRoot
$solution = Join-Path $root 'GuGuGaGaTranslator.slnx'

<#
.SYNOPSIS
    Pack dist\ into the archive a person actually downloads.
.DESCRIPTION
    Everything lands under one top-level folder, so extracting into Downloads
    makes a folder rather than a mess. Debug symbols and import libraries are
    skipped, and the read-me answers the three questions a recipient always has:
    does it need an installer, why did Windows warn, and where does the models
    folder have to stay.
#>
function New-Distribution {
    param([Parameter(Mandatory)][string] $PublishDirectory)

    $staging = Join-Path ([System.IO.Path]::GetTempPath()) ("gggt-zip-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
    $inner = Join-Path $staging 'GuGuGaGaTranslator'
    New-Item -ItemType Directory -Path $inner -Force | Out-Null

    Get-ChildItem $PublishDirectory -File |
        Where-Object { $_.Extension -notin '.pdb', '.lib' } |
        Copy-Item -Destination $inner -Force

    $models = Join-Path $PublishDirectory 'models'
    if (Test-Path $models) { Copy-Item $models (Join-Path $inner 'models') -Recurse -Force }

    $readme = @'
GuGuGaGaTranslator · 屏幕实时翻译(免安装版)
==============================

怎么用
------
1. 把整个 GuGuGaGaTranslator 文件夹解压到任意位置(桌面、D 盘都行,别只把 exe 拖出来)。

2. 双击 GuGuGaGaTranslator.exe。第一次启动会弹出设置卡片:
   · 识别(认字)已经能用了 —— 离线识别模型就在 models 文件夹里,不用下载任何东西。
   · 翻译需要联网 + 一个 API Key。卡片上有领取链接,步骤是:
     注册登录 → 创建 API Key → 复制粘贴到卡片上 → 点「测试连接」。
   · 没有 Key 也可以先点「先跳过」:抓屏和识别照常工作,只是译文会显示成 [mock …] 原文。

   领取地址(界面里都能直接点开):
     DeepSeek    https://platform.deepseek.com/     按量付费,一句台词约 $0.00003,新账号通常有赠送额度
     智谱 GLM    https://open.bigmodel.cn/          glm-4-flash 目前免费,想零成本先选它
     硅基流动    https://cloud.siliconflow.cn/      部分模型有免费额度
     本地模型    https://ollama.com/download        完全离线免费,但要先下几 GB 模型文件

3. 主界面左上角选游戏窗口 → 「底部对话框(一键)」或「框选区域」→ 开始翻译。

常见问题
--------
* 不用装任何运行库:.NET 运行时已经打进 GuGuGaGaTranslator.exe 了。
* 「models」文件夹必须和 GuGuGaGaTranslator.exe 待在一起,那是离线识别模型,删了就认不出字。
* 默认用自带的离线识别(RapidOCR),不需要做任何系统设置。
  想换成系统自带的「Windows OCR」(略快一点):先给对应语言装「光学字符识别」功能
  (「识别与翻译」页有个「打开 Windows 语言设置」按钮可以直接跳过去),再把识别引擎换过去。
* Windows 提示「已保护你的电脑」是未签名程序的正常提示:点「更多信息」→「仍要运行」。
  如果是从压缩包解出来的,先右键 zip 文件 → 属性 → 勾选「解除锁定」再解压,能省掉一堆麻烦。
* 翻译要联网(除非你本地跑模型)。API Key 存在 %APPDATA%\GuGuGaGaTranslator\config.json。
* 游戏必须用「无边框窗口 / 窗口化全屏」,独占全屏抓不到画面。

快捷键
------
Ctrl+Alt+T 开始/停止    Ctrl+Alt+P 暂停      Ctrl+Alt+R 重新框选
Ctrl+Alt+U 解锁翻译框(拖动/缩放)  Ctrl+Alt+O 显示原文  Ctrl+Alt+H 显示/隐藏翻译框
'@
    # A BOM, because Notepad on a Chinese Windows reads UTF-8 without one as GBK.
    [System.IO.File]::WriteAllText((Join-Path $inner '使用说明.txt'), $readme, (New-Object System.Text.UTF8Encoding $true))

    $stamp = Get-Date -Format 'yyyyMMdd'
    $archive = Join-Path $PublishDirectory "GuGuGaGaTranslator-win-x64-$stamp.zip"
    if (Test-Path $archive) { Remove-Item $archive -Force }

    # Entries are written by hand rather than with Compress-Archive: both that
    # cmdlet and the .NET Framework build of ZipFile.CreateFromDirectory on
    # Windows PowerShell write backslash separators, which some unzip tools turn
    # into filenames containing a backslash.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $root = Split-Path $inner -Leaf
    $zip = [System.IO.Compression.ZipFile]::Open($archive, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem $inner -Recurse -File) {
            $relative = $file.FullName.Substring($inner.Length).TrimStart('\', '/').Replace('\', '/')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $file.FullName, "$root/$relative", [System.IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally { $zip.Dispose() }

    Remove-Item $staging -Recurse -Force
    return Get-Item $archive
}

Write-Host "==> building $solution ($Configuration)" -ForegroundColor Cyan
dotnet build $solution -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "build failed with exit code $LASTEXITCODE" }

if ($Publish) {
    # A single file has to carry the runtime with it to be portable.
    if ($SingleFile) { $SelfContained = $true }

    $output = Join-Path $root 'dist'
    Write-Host "==> publishing to $output (self-contained=$($SelfContained.IsPresent), single-file=$($SingleFile.IsPresent))" -ForegroundColor Cyan
    $arguments = @(
        'publish', (Join-Path $root 'src\GuGuGaGaTranslator.App\GuGuGaGaTranslator.App.csproj')
        '-c', $Configuration
        '-r', $Runtime
        '-o', $output
        '--nologo'
    )
    $arguments += if ($SelfContained) { '--self-contained', 'true' } else { '--self-contained', 'false' }
    if ($SingleFile) {
        $arguments += '-p:PublishSingleFile=true'
        $arguments += '-p:IncludeNativeLibrariesForSelfExtract=true'
        $arguments += '-p:EnableCompressionInSingleFile=true'
    }

    dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "publish failed with exit code $LASTEXITCODE" }

    # The offline recognizer loads its models from disk rather than from the
    # assembly, so they travel beside the executable: a copied dist folder then
    # works without editing any path in the configuration.
    #
    # Cleared first, because Copy-Item into an existing directory nests the
    # source inside it: publishing twice left dist\models\models\v6 behind and
    # added thirty megabytes of duplication to every later archive.
    $models = Join-Path $root 'models'
    if (Test-Path $models) {
        $target = Join-Path $output 'models'
        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        Copy-Item $models $target -Recurse -Force
        Write-Host "==> models copied to $target" -ForegroundColor Green
    }

    $produced = Get-ChildItem $output -Filter 'GuGuGaGaTranslator.exe' -ErrorAction SilentlyContinue
    if ($produced) {
        Write-Host ("==> run it: {0} ({1:N1} MB)" -f $produced.FullName, ($produced.Length / 1MB)) -ForegroundColor Green
    }

    if ($Zip) {
        if (-not $produced) { throw "nothing to zip: $output has no GuGuGaGaTranslator.exe" }
        $archive = New-Distribution $output
        Write-Host ("==> send this: {0} ({1:N1} MB)" -f $archive.FullName, ($archive.Length / 1MB)) -ForegroundColor Green
    }
}

$appExe = Join-Path $root "src\GuGuGaGaTranslator.App\bin\$Configuration\net10.0-windows10.0.19041.0\GuGuGaGaTranslator.exe"
Write-Host "==> app:    $appExe" -ForegroundColor Green
Write-Host "==> probe:  $(Join-Path $root "tools\Probe\bin\$Configuration\net10.0-windows10.0.19041.0\gugugaga-probe.exe")" -ForegroundColor Green
