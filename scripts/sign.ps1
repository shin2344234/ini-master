<#
.SYNOPSIS
    Sign INIMaster.exe with Azure Trusted Signing, then verify it.

.DESCRIPTION
    Runs signtool with the Azure Trusted Signing dlib against the account in
    private\signing.json (endpoint, account name, certificate profile; no
    secrets). The credential is the Azure CLI login, so `az login` once on
    this machine is the whole setup. Same account and certificate as Master
    Looter and Stamina Master.

    private\tools is a copy of vpk's vendor\signing folder. Use the signtool
    in it: the Windows SDK's own signtool ignores the dlib and fails with "No
    certificates were found that met all the given criteria".

    publish.ps1 calls this before it builds the zip, so the zip and the
    checksum in the release notes are of the signed file.

.EXAMPLE
    .\scripts\sign.ps1                       signs dist\INIMaster.exe
    .\scripts\sign.ps1 -Path some\other.exe  signs that file instead
    .\scripts\sign.ps1 -VerifyOnly           reports the signature already on the file
#>
[CmdletBinding()]
param(
    [string] $Path,
    [switch] $VerifyOnly
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
if (-not $Path) { $Path = Join-Path $repo 'dist\INIMaster.exe' }
$Path = (Resolve-Path $Path).Path

$tools    = Join-Path $repo 'private\tools'
$metadata = Join-Path $repo 'private\signing.json'
$dlib     = Join-Path $tools 'Azure.CodeSigning.Dlib.dll'
$signtool = Join-Path $tools 'signtool.exe'
foreach ($f in @($metadata, $dlib, $signtool)) {
    if (-not (Test-Path -LiteralPath $f)) {
        throw "Missing $f. private\tools is a copy of vpk's vendor\signing folder, as used by Master Looter."
    }
}
$bom = [System.IO.File]::ReadAllBytes($metadata) | Select-Object -First 3
if ($bom.Length -ge 3 -and $bom[0] -eq 0xEF -and $bom[1] -eq 0xBB -and $bom[2] -eq 0xBF) {
    throw "$metadata has a UTF-8 BOM and the dlib will refuse it. Rewrite it without one."
}

if (-not $VerifyOnly) {
    Write-Host "Signing $Path"
    & $signtool sign /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 `
        /dlib $dlib /dmdf $metadata $Path
    if ($LASTEXITCODE -ne 0) {
        throw "signtool sign failed ($LASTEXITCODE). If it mentions credentials, run 'az login' and try again."
    }
}

Write-Host "Verifying $Path"
$out = & $signtool verify /pa /v $Path 2>&1
if ($LASTEXITCODE -ne 0) { $out | Write-Host; throw "signtool verify failed ($LASTEXITCODE)." }
$out | Where-Object { $_ -match 'Issued to: Seth|Successfully verified|Timestamp' } | ForEach-Object { Write-Host "  $($_.ToString().Trim())" }
