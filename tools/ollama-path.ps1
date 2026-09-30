function Resolve-OllamaExecutable([string]$OllamaHome) {
    if ($OllamaHome) {
        $path = Join-Path $OllamaHome 'ollama.exe'
    } else {
        $command = Get-Command ollama.exe -ErrorAction SilentlyContinue
        $path = if ($command) { $command.Source } else { Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe' }
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw '找不到 Ollama。请先安装，或使用 -OllamaHome 指定 ollama.exe 所在目录。'
    }
    [IO.Path]::GetFullPath($path)
}
