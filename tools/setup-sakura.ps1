<#
.SYNOPSIS
    Registers the Sakura-GalTransl model with the local Ollama server.

.DESCRIPTION
    Sakura-GalTransl is a 7B model fine-tuned specifically for visual-novel
    Japanese-to-Chinese translation. It is not a chat model: it expects one fixed
    system prompt, then the glossary and the previous translation, then a fixed
    instruction line. Those are baked into the Ollama model here, so the app only
    has to send the text.

    默认下载 IQ4_XS 量化文件，可指定模型目录和下载站点。

    Licence: CC-BY-NC-SA 4.0 — non-commercial use only.

.EXAMPLE
    .\setup-sakura.ps1                # download if needed, then register the model
    .\setup-sakura.ps1 -SkipDownload  # register from an existing file
#>
[CmdletBinding()]
param(
    [string] $ModelName = 'sakura-galtransl:7b',

    [string] $ModelDirectory = (Join-Path $env:USERPROFILE '.ollama\models\sakura'),

    [string] $OllamaHome = '',

    [string] $DownloadBaseUrl = 'https://huggingface.co',

    [switch] $SkipDownload
)

$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$fileName = 'Sakura-Galtransl-7B-v3.7-IQ4_XS.gguf'
$gguf = Join-Path $ModelDirectory $fileName
. (Join-Path $PSScriptRoot 'ollama-path.ps1')
$ollama = Resolve-OllamaExecutable $OllamaHome

New-Item -ItemType Directory -Force $ModelDirectory | Out-Null

if (-not (Test-Path $gguf)) {
    if ($SkipDownload) { throw "找不到 $gguf,而 -SkipDownload 要求它已经存在" }

    if (-not ([uri]$DownloadBaseUrl).IsAbsoluteUri -or ([uri]$DownloadBaseUrl).Scheme -ne 'https') { throw '下载站点必须使用 HTTPS。' }
    $url = $DownloadBaseUrl.TrimEnd('/') + "/SakuraLLM/Sakura-GalTransl-7B-v3.7/resolve/main/$fileName"
    Write-Host "下载 Sakura-GalTransl-7B-v3.7 (IQ4_XS,约 3.96 GB) ..." -ForegroundColor Cyan
    $download = $gguf + '.download'
    curl.exe -L --fail --show-error -o $download $url
    if ($LASTEXITCODE -ne 0) { throw "下载失败" }
    Move-Item -LiteralPath $download -Destination $gguf
}

Write-Host ("模型文件: {0} ({1:N2} GB)" -f $gguf, ((Get-Item $gguf).Length / 1GB)) -ForegroundColor Green

# Sakura 使用模型卡规定的系统提示和输入格式。
$systemPrompt = '你是一个视觉小说翻译模型，可以通顺地使用给定的术语表以指定的风格将日文翻译成简体中文，' +
                '并联系上下文正确使用人称代词，注意不要混淆使役态和被动态的主语和宾语，' +
                '不要擅自添加原文中没有的特殊符号，也不要擅自增加或减少换行。'

$modelfile = Join-Path $ModelDirectory 'Modelfile'
$content = @"
FROM $gguf

# No role markers: the model reads the system prompt and the user prompt as one
# block of text, exactly as the model card describes.
TEMPLATE `"`"`"{{ if .System }}{{ .System }}
{{ end }}{{ .Prompt }}`"`"`"

SYSTEM `"`"`"$systemPrompt`"`"`"

# 模型卡中的采样参数。
PARAMETER temperature 0.1
PARAMETER top_p 0.3
PARAMETER repeat_penalty 1.0
PARAMETER num_ctx 4096
"@

Set-Content -Path $modelfile -Value $content -Encoding UTF8
Write-Host "已写入 $modelfile" -ForegroundColor Cyan

Write-Host "注册到 Ollama:$ModelName ..." -ForegroundColor Cyan
& $ollama create $ModelName -f $modelfile
if ($LASTEXITCODE -ne 0) { throw "ollama create 失败" }

Write-Host ''
& $ollama list
Write-Host ''
Write-Host '完成。在 GuGuGaGaTranslator 里这样配:' -ForegroundColor Green
Write-Host "  翻译引擎 = openai-compatible"
Write-Host "  接口地址 = http://127.0.0.1:11434/v1"
Write-Host "  模型名   = $ModelName"
Write-Host '  指令格式 = sakura'
