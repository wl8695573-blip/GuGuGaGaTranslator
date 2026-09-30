[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Path,
    [string] $CertificateThumbprint = $env:GGGT_SIGN_CERTIFICATE_THUMBPRINT,
    [string] $TimestampServer = 'https://timestamp.digicert.com',
    [switch] $RequireSigning
)
$ErrorActionPreference = 'Stop'
if (-not $CertificateThumbprint) {
    if ($RequireSigning) { throw 'Signing required: set GGGT_SIGN_CERTIFICATE_THUMBPRINT to a valid CurrentUser code-signing certificate.' }
    Write-Warning "Unsigned artifact: $Path. No code-signing certificate configured."
    return
}
$CertificateThumbprint = $CertificateThumbprint.Replace(' ', '')
if ($CertificateThumbprint -notmatch '^[0-9a-fA-F]{40}$') { throw 'Invalid certificate thumbprint.' }
$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
if (-not $certificate.HasPrivateKey -or $certificate.NotAfter -lt [DateTime]::Now) { throw 'Signing certificate has no private key or has expired.' }
if (-not ($certificate.EnhancedKeyUsageList.ObjectId.Value -contains '1.3.6.1.5.5.7.3.3')) { throw 'Certificate is not valid for code signing.' }
$timestampUri = [uri]$TimestampServer
if (-not $timestampUri.IsAbsoluteUri -or $timestampUri.Scheme -ne 'https') { throw 'Timestamp server must use HTTPS.' }
$signTool = Get-Command signtool.exe -ErrorAction SilentlyContinue
if ($signTool) { $signToolPath = $signTool.Source }
else {
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $signToolPath = Get-ChildItem -LiteralPath $sdkRoot -Directory |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $signToolPath) { throw 'Install the Windows SDK signing tools.' }
& $signToolPath sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampServer /td SHA256 $Path
if ($LASTEXITCODE -ne 0) { throw 'Authenticode signing failed.' }
& $signToolPath verify /pa /all /v $Path
if ($LASTEXITCODE -ne 0) { throw 'Authenticode verification failed.' }
