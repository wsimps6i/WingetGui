<#
.SYNOPSIS
    Builds a self-contained Release copy of Winget GUI into artifacts\publish, and optionally the release zip.

.DESCRIPTION
    The output runs on Windows 10 1809+ or Windows 11 (x64) with no .NET or Windows App SDK runtime
    installed. winget itself (App Installer) must be present. Deliberately not single-file: the
    Windows App SDK and the WinGet COM interop need their native DLLs beside the exe.

.PARAMETER Version
    Version to stamp on the build (e.g. 0.2.0). Defaults to the <Version> in WingetGui.csproj.

.PARAMETER Zip
    Also write artifacts\WingetGui-<version>-win-x64.zip and artifacts\SHA256SUMS.txt, the release assets
    self-update expects.

.EXAMPLE
    pwsh scripts\publish.ps1

.EXAMPLE
    pwsh scripts\publish.ps1 -Version 0.2.0 -Zip

.NOTES
    Version 0.1.0 (adapted from MarkItDown-GUI's publish.ps1)
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\WingetGui\WingetGui.csproj'
$artifacts = Join-Path $repo 'artifacts'
$output = Join-Path $artifacts 'publish'

if (Test-Path $output) { Remove-Item $output -Recurse -Force }
# @( ) around the whole if: a one-element array returned from an if unwraps to a string, and splatting a string passes it one character at a time.
$versionArgs = @(if ($Version) { "-p:Version=$Version" })
dotnet publish $project -c Release -r win-x64 -o $output @versionArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)." }

$exe = Join-Path $output 'WingetGui.exe'
$built = ([version](Get-Item $exe).VersionInfo.FileVersion).ToString(3)
Write-Host "Published Winget GUI $built to $output"

if ($Zip) {
    $zipPath = Join-Path $artifacts "WingetGui-$built-win-x64.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath }
    # The zip holds the folder's contents (no top-level folder): self-update unpacks it as the new install.
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zipPath -CompressionLevel Optimal
    $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $zipPath)" | Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii
    Write-Host "Zip: $zipPath ($([math]::Round((Get-Item $zipPath).Length / 1MB, 1)) MB, sha256 $hash)"
}
