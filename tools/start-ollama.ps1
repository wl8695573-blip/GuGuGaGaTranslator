<#
.SYNOPSIS
    Starts or stops the local Ollama server that GuGuGaGaTranslator uses as its offline
    translation engine.

.DESCRIPTION
    Models are kept on the X: drive because the system drive is small, so this
    script points OLLAMA_MODELS there before starting the server. Use -Persist
    once to write that into the user environment, which also covers the Ollama
    tray app starting on its own later.

.EXAMPLE
    .\start-ollama.ps1                 # start the server (models on X:)
    .\start-ollama.ps1 -Persist        # start it and remember the model path
    .\start-ollama.ps1 -Stop           # stop every ollama process
#>
[CmdletBinding()]
param(
    [switch] $Stop,

    [switch] $Persist,

    [string] $Models = 'X:\ollama\models',

    [string] $OllamaHome = 'X:\ollama',

    [int] $TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$ollama = Join-Path $OllamaHome 'ollama.exe'

if ($Stop) {
    $stopped = 0
    Get-Process ollama -ErrorAction SilentlyContinue | ForEach-Object {
        $_.Kill()
        $stopped++
    }
    Write-Host "已停止 $stopped 个 ollama 进程"
    return
}

if (-not (Test-Path $ollama)) {
    throw "找不到 $ollama —— 便携版需要先解压到 $OllamaHome"
}

New-Item -ItemType Directory -Force $Models | Out-Null
$env:OLLAMA_MODELS = $Models

if ($Persist) {
    [Environment]::SetEnvironmentVariable('OLLAMA_MODELS', $Models, 'User')
    Write-Host "已写入用户环境变量 OLLAMA_MODELS=$Models" -ForegroundColor Green
}

function Test-OllamaServer {
    try {
        # 127.0.0.1 rather than localhost: the server binds IPv4 only, while
        # localhost resolves to ::1 first on this machine, which made a healthy
        # server look dead.
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
