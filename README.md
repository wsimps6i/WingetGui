# Winget GUI

A small, light Windows app for [winget](https://learn.microsoft.com/windows/package-manager/winget/) (Windows Package Manager): see what's installed, what has updates, search, install, update and remove apps, without the command line.

Built with WinUI 3 (.NET 10). It talks to winget through its official COM API, the same one Microsoft's `Microsoft.WinGet.Client` PowerShell module uses, rather than parsing `winget.exe` output, so progress, cancel and results come straight from winget.

## Features

- **Installed**: every installed app with its version and any available update; filter, multi-select, update or uninstall.
- **Updates**: everything that can be updated, with **Update all**. Updates winget can't apply in place (for example MSI installed, MSIX only now) are shown greyed out rather than hidden.
- **Search** across winget and the Microsoft Store as you type; install a chosen version and scope.
- **Details** for any package: publisher, description, homepage, licence, release notes.
- **Activity**: installs run one at a time with live progress and cancel.
- **Pins**: pin a package so Update all leaves it alone.
- **Sources**: add, remove or reset winget sources (asks for administrator permission).
- **Export / import** package lists in winget's own format.
- **Notification area**: keeps running when closed, checks for updates in the background and shows a notification when new ones appear. Optional start at sign-in.
- **Command line** for scripts and scheduled tasks (see below).
- **Light / dark / system theme**, daily log files with automatic clean-up.
- **Updates itself** from this repository's releases, after checking each download's SHA-256.

## Requirements

Windows 10 1809+ or Windows 11, x64, with winget installed (App Installer from the Microsoft Store). No administrator rights and no .NET or Windows App SDK runtime needed.

## Install

From the [latest release](https://github.com/wsimps6i/WingetGui/releases/latest), download `WingetGui-<version>-win-x64.zip`, `SHA256SUMS.txt` and `Install-WingetGui.ps1` into one folder, then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-WingetGui.ps1
```

It checks the zip against `SHA256SUMS.txt`, installs to `%LOCALAPPDATA%\Programs\Winget GUI` and adds a Start menu shortcut. The app isn't code-signed yet, so SmartScreen may warn the first time.

**Updating:** Settings → About Winget GUI → Check for updates (it also checks on its own). **Uninstalling:**

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-WingetGui.ps1 -Uninstall -RemoveSettings
```

## Command line

```text
WingetGui.exe -ListUpdates          List available updates
WingetGui.exe -UpdateAll            Install every update (pinned packages skipped)
WingetGui.exe -Install <id>         Install a package by its exact id
WingetGui.exe -Export <file>        Save installed packages as a winget export file
WingetGui.exe -Import <file>        Install what's in an export file and not installed yet
WingetGui.exe -Help
```

Exit codes: 0 OK, 1 something failed, 2 bad command line. Output goes to the console and the daily log in `%LOCALAPPDATA%\WingetGui\logs`. It's a windowed app, so wait for it with `start /wait` or `Start-Process -Wait`.

## Build from source

```powershell
dotnet build WingetGui.slnx
dotnet test tests/WingetGui.Core.Tests
dotnet run --project src/WingetGui            # or run src\WingetGui\bin\Debug\...\WingetGui.exe
pwsh scripts/publish.ps1 -Zip                 # self-contained build + release zip in artifacts\
pwsh scripts/Install-WingetGui.ps1            # build and install for the current user
```

Releases: push a tag `vX.Y.Z`; `.github/workflows/release.yml` tests, builds and publishes the release.

## Layout

| Path | What |
|---|---|
| `src/WingetGui.Core` | All winget logic, no UI: COM API (`WingetService`), `winget.exe` for pins/export/sources (`WingetCli`), operation queue, installed list, logging, settings, command line, self-update |
| `src/WingetGui` | WinUI 3 app: `Program.cs` (entry point, single instance, command line), views, tray icon, notifications |
| `tests/WingetGui.Core.Tests` | xUnit tests for Core |
| `scripts` | `publish.ps1`, `Install-WingetGui.ps1` |
| `spike` | The console app that proved the COM API works (kept for reference) |

Why things are the way they are, and the gotchas found along the way, are in [PLANNING.md](PLANNING.md). Known limits and ideas are in [BACKLOG.md](BACKLOG.md).
