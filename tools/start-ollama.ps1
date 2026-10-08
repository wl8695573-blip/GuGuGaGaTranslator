<#
.SYNOPSIS
    Starts or stops the local Ollama server that LCTA uses as its offline
    translation engine.

.DESCRIPTION
    使用已安装的 Ollama。-Models 指定模型目录；-Persist 将该目录保存到
    当前用户环境变量。-OllamaHome 可指定便携版程序目录。

.EXAMPLE
    .\start-ollama.ps1                 # start the installed server
    .\start-ollama.ps1 -Persist        # start it and remember the model path
    .\start-ollama.ps1 -Stop           # stop every ollama process
#>
[CmdletBinding()]
param(
    [switch] $Stop,

    [switch] $Persist,

    [string] $Models = (Join-Path $env:USERPROFILE '.ollama\models'),

    [string] $OllamaHome = '',

    [int] $TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ollama-path.ps1')
$ollama = Resolve-OllamaExecutable $OllamaHome

if ($Stop) {
    $stopped = 0
    Get-Process ollama -ErrorAction SilentlyContinue | ForEach-Object {
        $_.Kill()
        $stopped++
    }
    Write-Host "已停止 $stopped 个 ollama 进程"
    return
}

New-Item -ItemType Directory -Force $Models | Out-Null
$env:OLLAMA_MODELS = $Models

if ($Persist) {
    [Environment]::SetEnvironmentVariable('OLLAMA_MODELS', $Models, 'User')
    Write-Host "已写入用户环境变量 OLLAMA_MODELS=$Models" -ForegroundColor Green
}

function Test-OllamaServer {
    try {
        # 使用 IPv4 回环地址，避免 localhost 的 IPv6 解析差异。
        return (Invoke-WebRequest 'http://127.0.0.1:11434/api/tags' -TimeoutSec 2 -UseBasicParsing).StatusCode -eq 200
    } catch {
        return $false
    }
}

if (Test-OllamaServer) {
    Write-Host 'Ollama 服务已在运行' -ForegroundColor Green
} else {
    Start-Process -FilePath $ollama -ArgumentList 'serve' -WindowStyle Hidden
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (Test-OllamaServer)) {
        if ((Get-Date) -gt $deadline) { throw "Ollama 在 $TimeoutSeconds 秒内没有起来" }
        Start-Sleep -Milliseconds 500
    }
    Write-Host "Ollama 服务已启动(模型目录:$Models)" -ForegroundColor Green
}

Write-Host '已安装的模型:'
& $ollama list
