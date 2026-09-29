<#
.SYNOPSIS
    Publishes this repository: pushes the code and uploads the release downloads.

.DESCRIPTION
    Two steps, because they authenticate differently. The push uses whatever git
    credential helper is configured (Git Credential Manager opens a browser the
    first time). Uploading release assets has no git equivalent, so it needs a
    token with `repo` scope, taken from -Token, then $env:GITHUB_TOKEN, then the
    credential helper.

.EXAMPLE
    .\tools\publish.ps1 -Repo https://github.com/you/GuGuGaGaTranslator

.EXAMPLE
    # 代码已经推过了,只重发 Release
    .\tools\publish.ps1 -Repo https://github.com/you/GuGuGaGaTranslator -SkipPush
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Repo,

    [string] $Tag = 'v1.0.0',

    [string] $Title = 'GuGuGaGaTranslator 1.0.0',

    [string] $NotesFile = '',

    [string] $Token = '',

    [string] $Description = '屏幕实时翻译:选中游戏窗口、框住对话框,译文实时贴在置顶悬浮层上。离线识别 + AI 翻译,支持游戏专属术语表。',

    [string[]] $Topics = @('galgame', 'translation', 'ocr', 'screen-translation', 'wpf', 'dotnet', 'deepseek', 'visual-novel'),

    [switch] $SkipPush,

    [switch] $SkipRelease
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function ConvertTo-OwnerRepo {
    param([string] $Url)
    $trimmed = $Url.Trim().TrimEnd('/')
    if ($trimmed -match '^https?://[^/]+/([^/]+)/([^/]+)$') { return @{ Owner = $Matches[1]; Name = $Matches[2] -replace '\.git$', '' } }
    if ($trimmed -match '^([^/]+)/([^/]+)$') { return @{ Owner = $Matches[1]; Name = $Matches[2] } }
    throw "看不懂这个仓库地址:$Url(例:https://github.com/you/GuGuGaGaTranslator)"
}

function Get-GitHubToken {
    # 末尾的空白(文件或环境变量读进来的换行)会让 GitHub 回「Bad credentials」,
    # 令牌本身却是好的 —— 所以这里一律 Trim。
    if ($Token) { return $Token.Trim() }
    if ($env:GITHUB_TOKEN) { return $env:GITHUB_TOKEN.Trim() }

    $filled = "protocol=https`nhost=github.com`n`n" | git credential fill 2>$null
    $line = $filled | Where-Object { $_ -match '^password=' } | Select-Object -First 1
    if ($line) { return ($line -replace '^password=', '').Trim() }

    throw "没有可用的 GitHub 令牌。请先推送一次(浏览器登录后凭据会被记住),或者用 -Token 传入一个有 repo 权限的令牌。"
}

function Invoke-GitHub {
    param([string] $Method, [string] $Uri, $Body, [string] $ContentType = 'application/json', [string] $TokenValue)
    $headers = @{
        Authorization          = "Bearer $TokenValue"
        Accept                 = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
        'User-Agent'           = 'GuGuGaGaTranslator-publish'
    }
    if ($Body -is [byte[]]) {
        return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -Body $Body -ContentType $ContentType
    }

    # JSON 正文必须以 UTF-8 字节发送:ContentType 不带 charset 时,PowerShell 会按
    # ASCII 编码字符串,仓库描述里的中文会变成一串「?」。
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 6 }
    $bytes = if ($null -eq $json) { $null } else { [System.Text.Encoding]::UTF8.GetBytes($json) }
    return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -Body $bytes -ContentType "$ContentType; charset=utf-8"
}

$target = ConvertTo-OwnerRepo $Repo
$slug = "$($target.Owner)/$($target.Name)"
Write-Host "==> 目标仓库 $slug" -ForegroundColor Cyan

# ── 1. push ─────────────────────────────────────────────────────────────────
if (-not $SkipPush) {
    Push-Location $root
    try {
        $existing = git remote 2>$null
        if ($existing -contains 'origin') { git remote set-url origin $Repo } else { git remote add origin $Repo }
        Write-Host '==> git push main(如果弹出浏览器登录,请在上面点授权)' -ForegroundColor Cyan
        git push -u origin main
        if ($LASTEXITCODE -ne 0) {
            # 仓库若被建成了「带 README 的仓库」,远端就已存在提交,先合再推。
            Write-Host '==> 直推失败,尝试合并远端已有提交后重推' -ForegroundColor Yellow
            git pull --rebase --allow-unrelated-histories origin main
            if ($LASTEXITCODE -ne 0) { throw "合并远端失败(退出码 $LASTEXITCODE),请手动处理" }
            git push -u origin main
            if ($LASTEXITCODE -ne 0) { throw "推送失败(退出码 $LASTEXITCODE)" }
        }
    }
    finally { Pop-Location }
}

if ($SkipRelease) { Write-Host '==> 按要求跳过 Release' -ForegroundColor Green; exit 0 }

$tokenValue = Get-GitHubToken

# ── 2. 仓库描述与标签 ────────────────────────────────────────────────────────
try {
    Invoke-GitHub -Method PATCH -Uri "https://api.github.com/repos/$slug" -TokenValue $tokenValue `
        -Body @{ description = $Description; homepage = '' } | Out-Null
    Invoke-GitHub -Method PUT -Uri "https://api.github.com/repos/$slug/topics" -TokenValue $tokenValue `
        -Body @{ names = $Topics } | Out-Null
    Write-Host '==> 已设置仓库描述与话题标签' -ForegroundColor Green
}
catch { Write-Warning "设置描述/标签失败(不影响发布):$($_.Exception.Message)" }

# ── 3. Release + 资产 ───────────────────────────────────────────────────────
$assets = Get-ChildItem (Join-Path $root 'dist') -File |
    Where-Object { $_.Name -like 'GuGuGaGaTranslator-Setup-*.exe' -or $_.Name -like 'GuGuGaGaTranslator-win-x64-*.zip' }
if (-not $assets) { throw "dist 里没有找到安装包或压缩包,先跑 .\build.ps1 -Publish -SingleFile -Zip" }

# [System.IO.File] 而不是 Get-Content -Raw:后者返回的对象带 PSPath 等扩展属性,ConvertTo-Json 会把它序列化成对象,API 会拒绝。
if ($NotesFile -and (Test-Path $NotesFile)) { $body = [System.IO.File]::ReadAllText($NotesFile, [System.Text.Encoding]::UTF8) }
else {
    $body = @"
## 下载哪个?

| 文件 | 说明 |
|---|---|
| **GuGuGaGaTranslator-Setup-$($Tag.TrimStart('v')).exe** | **推荐**:双击安装,自动建开始菜单快捷方式,可在「设置 → 应用」里卸载 |
| GuGuGaGaTranslator-win-x64-$($Tag.TrimStart('v')).zip | 免安装版:解压后双击 GuGuGaGaTranslator.exe(**别把 exe 单独拖出来**,旁边的 models 是识别模型) |

## 装完就能用

- **识别**:离线模型随包附带,**不需要任何额外下载**,解压即用。
- **翻译**:需要一个 API Key(不用装软件)。第一次启动会弹出设置卡片,里面每个服务商都有可点开的领取链接和步骤;DeepSeek 新账号通常有赠送额度。
- 没有 Key 也能先跳过:抓屏和识别照常工作,用来确认框选对不对。

详细说明见 [README](https://github.com/$slug#readme)。
"@
}

Write-Host '==> 创建 Release' -ForegroundColor Cyan
$release = $null
try {
    $release = Invoke-GitHub -Method GET -Uri "https://api.github.com/repos/$slug/releases/tags/$Tag" -TokenValue $tokenValue
    Write-Host "    已存在同名 Release,将复用并重传资产"
}
catch {
    $release = Invoke-GitHub -Method POST -Uri "https://api.github.com/repos/$slug/releases" -TokenValue $tokenValue -Body @{
        tag_name   = $Tag
        name       = $Title
        body       = $body
        draft      = $false
        prerelease = $false
    }
}

foreach ($asset in $assets) {
    $bytes = [System.IO.File]::ReadAllBytes($asset.FullName)
    Write-Host ("    上传 {0}({1:N1} MB)…" -f $asset.Name, ($asset.Length / 1MB)) -ForegroundColor Cyan
    $upload = "https://uploads.github.com/repos/$slug/releases/$($release.id)/assets?name=$([uri]::EscapeDataString($asset.Name))"
    Invoke-GitHub -Method POST -Uri $upload -Body $bytes -ContentType 'application/octet-stream' -TokenValue $tokenValue | Out-Null
}

Write-Host "==> 完成:https://github.com/$slug/releases/tag/$Tag" -ForegroundColor Green
