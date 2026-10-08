[CmdletBinding()]
param(
    [string] $Repo = 'wl8695573-blip/GuGuGaGaTranslator',
    [string] $NotesFile = '',
    [switch] $SkipPush
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Encoding UTF8
$version = [string]$props.Project.PropertyGroup.Version
$tag = "v$version"
$slug = $Repo -replace '^https://github.com/', '' -replace '\.git$', ''
if ($slug -notmatch '^[\w.-]+/[\w.-]+$') { throw 'Invalid repository' }
if (-not $NotesFile) { $NotesFile = Join-Path $PSScriptRoot 'release-notes-lcta.md' }
$notes = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $NotesFile), [Text.Encoding]::UTF8)
$dist = Join-Path $root 'dist'
$names = @("LCTA-Setup-$version.exe", "LCTA-win-x64-$version.zip", "SHA256SUMS-$version.txt")
foreach ($name in $names) { if (-not (Test-Path -LiteralPath (Join-Path $dist $name))) { throw "Missing artifact: $name" } }
foreach ($line in [IO.File]::ReadAllLines((Join-Path $dist $names[2]))) {
    $parts = $line -split '  ',2
    if ($parts.Length -ne 2 -or $parts[1] -notin $names[0..1]) { throw 'Invalid checksum manifest' }
    if ((Get-FileHash -LiteralPath (Join-Path $dist $parts[1]) -Algorithm SHA256).Hash -ne $parts[0]) { throw "Checksum mismatch: $($parts[1])" }
}
Push-Location $root
try {
    if (git status --porcelain) { throw 'Commit reviewed changes before publishing' }
    $commit = (git rev-parse HEAD).Trim()
    if (-not $SkipPush) { git push origin HEAD:main; if ($LASTEXITCODE -ne 0) { throw 'Push failed' } }
    $remote = (git ls-remote origin refs/heads/main) -split '\s+'
    if ($remote[0] -ne $commit) { throw 'Remote main does not match this build' }
} finally { Pop-Location }
$token = $env:GITHUB_TOKEN
if (-not $token) {
    $env:GIT_TERMINAL_PROMPT = '0'
    $credentialLines = "protocol=https`nhost=github.com`n`n" | git credential fill 2>$null
    foreach ($line in $credentialLines) { if ($line.StartsWith('password=')) { $token = $line.Substring(9).Trim() } }
}
if (-not $token) { throw 'No stored GitHub credential' }
$headers = @{ Authorization = "Bearer $token"; Accept = 'application/vnd.github+json'; 'User-Agent' = 'LCTA-release'; 'X-GitHub-Api-Version' = '2022-11-28' }
function Invoke-Api([string]$Method, [string]$Url, $Body = $null) {
    $args = @{ Method=$Method; Uri=$Url; Headers=$headers }
    if ($null -ne $Body) {
        $args.ContentType = 'application/json; charset=utf-8'
        $args.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 10 -Compress))
    }
    Invoke-RestMethod @args
}
$api = "https://api.github.com/repos/$slug"
$existing = @(Invoke-Api GET "$api/releases?per_page=100") | Where-Object tag_name -eq $tag
if ($existing -and -not $existing.draft) { throw "Published release $tag already exists; refusing to replace it." }
$release = if ($existing) { $existing } else {
    Invoke-Api POST "$api/releases" @{ tag_name=$tag; target_commitish=$commit; name="LCTA $version"; body=$notes; draft=$true; prerelease=$false }
}
foreach ($name in $names) {
    $file = Get-Item -LiteralPath (Join-Path $dist $name)
    $old = @($release.assets) | Where-Object name -eq $name
    if ($old) { Invoke-Api DELETE "$api/releases/assets/$($old.id)" | Out-Null }
    Write-Host "Uploading $name"
    Invoke-RestMethod -Method POST -Uri ("https://uploads.github.com/repos/$slug/releases/$($release.id)/assets?name=" + [uri]::EscapeDataString($name)) -Headers $headers -InFile $file.FullName -ContentType 'application/octet-stream' | Out-Null
}
$verified = Invoke-Api GET "$api/releases/$($release.id)"
foreach ($name in $names) {
    $asset = @($verified.assets) | Where-Object name -eq $name
    if (-not $asset -or $asset.state -ne 'uploaded' -or $asset.size -ne (Get-Item -LiteralPath (Join-Path $dist $name)).Length) { throw "Upload verification failed: $name" }
    if ($asset.digest -and $asset.digest -ne ('sha256:' + (Get-FileHash -LiteralPath (Join-Path $dist $name) -Algorithm SHA256).Hash.ToLowerInvariant())) { throw "Server digest mismatch: $name" }
}
$published = Invoke-Api PATCH "$api/releases/$($release.id)" @{ body=$notes; draft=$false; make_latest='true' }
Write-Host $published.html_url
