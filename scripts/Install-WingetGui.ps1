<#
.SYNOPSIS
    Installs (or removes) Winget GUI for the current user.

.DESCRIPTION
    Installs to %LOCALAPPDATA%\Programs\Winget GUI and adds a Start menu shortcut. No administrator
    rights are needed. Where the files come from, in order:
      1. -Zip <file>: a release zip (WingetGui-<version>-win-x64.zip).
      2. A release zip next to this script (download both from the GitHub release).
      3. Otherwise, a fresh build of the source with publish.ps1 (needs the .NET 10 SDK).
    A zip is checked against SHA256SUMS.txt when that file is next to it.

    The Start menu shortcut is also how Windows shows the app's notifications; the app adds its
    notification properties to it the first time it runs. Later versions arrive through the app's own
    self-update (Settings, About Winget GUI), or by running this script again.

    -Uninstall first runs WingetGui.exe -Unregister (removes notification registration and Start at
    sign-in), then deletes the shortcut and the program folder. Settings and logs in
    %LOCALAPPDATA%\WingetGui stay unless -RemoveSettings is also given.

.PARAMETER Zip
    Release zip to install.

.PARAMETER InstallDir
    Where to install. Defaults to %LOCALAPPDATA%\Programs\Winget GUI.

.PARAMETER Uninstall
    Remove Winget GUI.

.PARAMETER RemoveSettings
    With -Uninstall, also delete %LOCALAPPDATA%\WingetGui (settings and logs).

.EXAMPLE
    .\Install-WingetGui.ps1
    Installs the release zip downloaded next to this script.

.EXAMPLE
    pwsh scripts\Install-WingetGui.ps1 -Uninstall -RemoveSettings

.NOTES
    Version 0.1.0 (adapted from MarkItDown-GUI's Install-MarkItDownGUI.ps1)
#>
[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(ParameterSetName = 'Install')][string]$Zip,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Winget GUI'),
    [Parameter(ParameterSetName = 'Uninstall', Mandatory)][switch]$Uninstall,
    [Parameter(ParameterSetName = 'Uninstall')][switch]$RemoveSettings
)

$ErrorActionPreference = 'Stop'
$appName = 'Winget GUI'
$exeName = 'WingetGui.exe'
$startMenuLink = Join-Path ([Environment]::GetFolderPath('Programs')) "$appName.lnk"
$exe = Join-Path $InstallDir $exeName

function Assert-NotRunning {
    $running = Get-Process -Name 'WingetGui' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) }
    if ($running) {
        throw "$appName is running. Exit it (right-click its icon in the notification area, then Exit) and run this again."
    }
}

# Refuse to delete a folder that isn't a Winget GUI install (e.g. a mistyped -InstallDir).
function Assert-OurInstallDir {
    if ((Test-Path -LiteralPath $InstallDir) -and
        -not (Test-Path -LiteralPath $exe) -and
        (Get-ChildItem -LiteralPath $InstallDir -Force | Select-Object -First 1)) {
        throw "$InstallDir is not empty and has no $exeName in it, so it is not being touched. Choose another -InstallDir."
    }
}

Assert-NotRunning
Assert-OurInstallDir

if ($Uninstall) {
    if (Test-Path -LiteralPath $exe) {
        & $exe -Unregister | Out-Host  # removes notification registration and Start at sign-in
    }
    if (Test-Path -LiteralPath $startMenuLink) { Remove-Item -LiteralPath $startMenuLink; Write-Host "Removed $startMenuLink" }
    foreach ($dir in $InstallDir, "$InstallDir.previous") {
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force; Write-Host "Removed $dir" }
    }
    $data = Join-Path $env:LOCALAPPDATA 'WingetGui'
    if ($RemoveSettings -and (Test-Path -LiteralPath $data)) {
        Remove-Item -LiteralPath $data -Recurse -Force
        Write-Host "Removed $data"
    }
    return
}

# Where the files come from.
if (-not $Zip) {
    $Zip = Get-ChildItem -Path $PSScriptRoot -Filter 'WingetGui-*-win-x64.zip' -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.Name -replace '^WingetGui-|-win-x64\.zip$') } | Select-Object -Last 1 -ExpandProperty FullName
}
$staging = Join-Path ([IO.Path]::GetTempPath()) "WingetGui-install-$PID"
if ($Zip) {
    $Zip = (Resolve-Path -LiteralPath $Zip).Path
    $sums = Join-Path (Split-Path -Parent $Zip) 'SHA256SUMS.txt'
    if (Test-Path -LiteralPath $sums) {
        $expected = (Get-Content -LiteralPath $sums | Where-Object { $_ -match "\s\*?$([regex]::Escape((Split-Path -Leaf $Zip)))$" } | Select-Object -First 1) -replace '\s.*$'
        $actual = (Get-FileHash -LiteralPath $Zip -Algorithm SHA256).Hash
        if (-not $expected -or $actual -ne $expected) { throw "$Zip doesn't match SHA256SUMS.txt (expected '$expected', got '$actual'). Download it again." }
        Write-Host "Checksum OK ($Zip)"
    }
    else {
        Write-Warning "No SHA256SUMS.txt next to the zip, so it can't be checked. Download it from the same release to verify."
    }
    Expand-Archive -LiteralPath $Zip -DestinationPath $staging -Force
    $source = $staging
}
else {
    $repo = Split-Path -Parent $PSScriptRoot
    if (-not (Test-Path (Join-Path $repo 'src\WingetGui\WingetGui.csproj'))) {
        throw "No release zip next to this script and no source to build. Download WingetGui-<version>-win-x64.zip (and SHA256SUMS.txt) from the release into $PSScriptRoot."
    }
    & (Join-Path $PSScriptRoot 'publish.ps1')
    $source = Join-Path $repo 'artifacts\publish'
}
if (-not (Test-Path -LiteralPath (Join-Path $source $exeName))) { throw "No $exeName in $source." }

# Checked again: building takes a while, long enough to have started the app meanwhile.
Assert-NotRunning

# Move the old copy aside first. Renaming fails as a whole if anything in it is in use, so a
# locked file leaves the existing install untouched instead of half-deleted.
$previous = "$InstallDir.previous"
if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Recurse -Force }
if (Test-Path -LiteralPath $InstallDir) {
    try {
        Rename-Item -LiteralPath $InstallDir -NewName (Split-Path -Leaf $previous)
    }
    catch {
        throw "Couldn't replace $InstallDir because something is using it ($($_.Exception.Message)). Close $appName and any Explorer window showing that folder, then run this again."
    }
}
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $InstallDir -Recurse
if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Recurse -Force }
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
$installed = ([version](Get-Item -LiteralPath $exe).VersionInfo.FileVersion).ToString(3)
Write-Host "Installed $appName $installed to $InstallDir" -ForegroundColor Green

if (-not ('WingetGuiSetup.ShellLink' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace WingetGuiSetup
{
    public static class ShellLink
    {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, int flags);
            void GetIDList(out IntPtr pidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCmd);
            void SetShowCmd(int showCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int cch, out int iconIndex);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pathRel, int reserved);
            void Resolve(IntPtr hwnd, int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        public static void Write(string linkPath, string target, string workingDir, string description)
        {
            var link = (IShellLinkW)new CShellLink();
            link.SetPath(target);
            link.SetWorkingDirectory(workingDir);
            link.SetDescription(description);
            link.SetIconLocation(target, 0);
            ((IPersistFile)link).Save(linkPath, true);
        }
    }
}
'@
}

# A plain shortcut: on first run the app adds its AppUserModelID and notification activator to it (StartMenuShortcut.cs).
[WingetGuiSetup.ShellLink]::Write($startMenuLink, $exe, $InstallDir, 'Install, update and remove apps with winget')
Write-Host "Shortcut: $startMenuLink"
