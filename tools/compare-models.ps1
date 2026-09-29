<#
.SYNOPSIS
    Runs the same Japanese lines through several translation models and prints a
    side-by-side table.

.DESCRIPTION
    Quality differences between models are easy to argue about and hard to see.
    This script removes the argument: identical input, identical parameters, only
    the model (and the instruction format it needs) changes.

    It calls the probe's `translate` command, so no window and no screenshot is
    involved — the OCR result is fixed text, which is what makes the comparison
    about translation rather than about recognition.

.EXAMPLE
    .\compare-models.ps1
    .\compare-models.ps1 -Lines '「ちょっと、コンビニまで…」'
#>
[CmdletBinding()]
param(
    [string] $Probe = (Join-Path $PSScriptRoot 'GuGuGaGaTranslator.Probe\bin\Debug\net10.0-windows10.0.19041.0\gugugaga-probe.exe'),

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
Write-Host "注意:按模型分组跑,而不是逐句在两个模型之间切换 —— 6GB 显存装不下两个模型," -ForegroundColor DarkGray
Write-Host "逐句切换会让 Ollama 每次都重新加载整个模型,测出来的时间会虚高十几倍。`n" -ForegroundColor DarkGray

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
