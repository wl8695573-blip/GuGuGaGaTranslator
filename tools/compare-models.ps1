<#
.SYNOPSIS
    Runs the same Japanese lines through several translation models and prints a
    side-by-side table.

.DESCRIPTION
    向已安装的本地模型发送相同文本，使用各模型对应的提示格式，
    输出译文和耗时。不执行抓屏或 OCR。

.EXAMPLE
    .\compare-models.ps1
    .\compare-models.ps1 -Lines '「ちょっと、コンビニまで…」'
#>
[CmdletBinding()]
param(
    [string] $Probe = (Join-Path $PSScriptRoot 'Probe\bin\Release\net10.0-windows10.0.19041.0\gugugaga-probe.exe'),

    [string] $BaseUrl = 'http://127.0.0.1:11434/v1',

    [string] $From = 'ja',

    [string] $To = 'zh-Hans',

    [string[]] $Lines = @(
        '「ねえ、こんな時間にどこに行くつもり？」',
        '「ちょっと、コンビニまで…」',
        '「そんな格好で行くつもり？」',
        '「文句あるなら一緒に来れば？」',
        '「べ、別にあんたのためじゃないんだからね！」'
    )
)

$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

# Each candidate pairs a model with the instruction format it expects: the
# general model gets the general instruction, Sakura gets its own wording.
$candidates = @(
    @{ Label = 'qwen2.5:7b (通用)'; Model = 'qwen2.5:7b-instruct'; Style = 'galgame' },
    @{ Label = 'Sakura-GalTransl (专用)'; Model = 'sakura-galtransl:7b'; Style = 'sakura' }
)

if (-not (Test-Path $Probe)) { throw "找不到探针:$Probe(先跑 build.ps1)" }

Write-Host "对比 $($candidates.Count) 个模型,共 $($Lines.Count) 句。" -ForegroundColor Cyan
Write-Host "按模型分组调用，减少模型切换和重复加载对耗时的影响。" -ForegroundColor DarkGray

$results = [ordered]@{}
foreach ($candidate in $candidates) {
    Write-Host "--- $($candidate.Label) ---" -ForegroundColor Cyan
    $lines = @()
    foreach ($line in $Lines) {
        $arguments = @(
            'translate', '--text', $line,
            '--from', $From, '--to', $To,
            '--translator', 'openai-compatible',
            '--base-url', $BaseUrl,
            '--model', $candidate.Model,
            '--prompt-style', $candidate.Style,
            '--timeout', '180'
        )
        try {
            $parsed = (& $Probe @arguments 2>$null | Out-String) | ConvertFrom-Json
            $lines += $parsed.translation
            Write-Host ("  {0,7:N0} ms  {1}" -f $parsed.elapsedMs, $parsed.translation) -ForegroundColor DarkGray
        } catch {
            $lines += '(失败)'
            Write-Host "  失败: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
    $results[$candidate.Label] = $lines
}

Write-Host "`n================ 对照表 ================" -ForegroundColor Green
for ($i = 0; $i -lt $Lines.Count; $i++) {
    Write-Host "`n原文:$($Lines[$i])" -ForegroundColor White
    foreach ($candidate in $candidates) {
        Write-Host ("  {0,-26} {1}" -f $candidate.Label, $results[$candidate.Label][$i])
    }
}
