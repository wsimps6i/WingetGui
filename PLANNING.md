# Winget GUI — Plan

Small, light WinUI 3 front end for winget, in the spirit of [hvbraak/WinGet-Manager](https://github.com/hvbraak/WinGet-Manager) (PowerShell + WPF, parses winget.exe output). Nothing is built yet; this file records the decisions and why, the same way BrowserDeploy's and MarkItDown-GUI's PLANNING/BACKLOG files do.

## Decisions

| Area | Decision | Why |
|---|---|---|
| Targets | Windows 11 and Windows 10 (1809+). No Windows Server. | WinUI 3 is fine on desktops; BrowserDeploy found it crashes on Server 2019. |
| UI | WinUI 3, Windows App SDK, .NET 10, MVVM (CommunityToolkit.Mvvm). Mica, `NavigationView`. | Modern native look; same stack as MarkItDown-GUI. |
| winget backend | **WinGet COM API** (`Microsoft.Management.Deployment`), the API the official `Microsoft.WinGet.Client` PowerShell module uses. | Typed results, real progress events, cancel. No parsing of table output that breaks on long names, locales and new winget versions. |
| Gaps in the COM API | `winget.exe` **only** for what the API lacks (confirmed in Phase 0): `winget pin list/add/remove` (parse one small table — no JSON output), `winget export -o` (writes winget's own schema), elevated `winget source add/remove/reset`. Import reads that JSON and queues installs through the COM API, so it gets per-package progress. | Keeps text parsing to one table. |
| Elevation | App runs as the signed-in user, never elevated. Machine-scope installers raise their own UAC prompt. Source add/remove/reset runs `winget source …` in a `runas` child process. | Phase 0: `AddPackageCatalogAsync` from a non-elevated process returns `AccessDenied` (0x8A150019); the out-of-proc COM server isn't meant for elevated callers. |
| Packaging | Unpackaged, self-contained, per-user install to `%LOCALAPPDATA%\Programs\WingetGui` + Start menu shortcut, via `scripts/Install-WingetGui.ps1`. | Same as MarkItDown-GUI; no signing cert needed. |
| Single instance | Custom `Program.cs` Main (MarkItDown-GUI pattern): handles CLI switches first, then hands over to the open window if one is running. | Needed for tray mode and silent switches. |

## Features (v1)

Core
- **Installed**: list with installed vs available version, source, update pill; filter box; multi-select.
- **Updates**: everything with an update first; Update selected / Update all; skips pinned packages.
- **Search**: search as you type (debounced), install from results, choose version and scope (user/machine).
- **Uninstall**: single or multi-select, with confirmation.
- **Package details** pane: publisher, description, homepage, licence, release notes, available versions.
- **Operation queue**: one operation at a time, live progress ("3/12: Firefox — downloading 45%"), cancel button, per-package result (success / failed / needs reboot) with the winget error text.

Extras (all requested)
- **Light / dark / system theme.**
- **Daily log files** in `%LOCALAPPDATA%\WingetGui\logs\yyyy-MM-dd.log`, oldest deleted after N days (setting, default 14).
- **Sources**: list, add, remove, reset to defaults.
- **Pins**: pin / unpin a package (pinned packages are skipped by Update all).
- **Export / import**: JSON in winget's own `winget export` schema, so files work with `winget import` either way.
- **Tray + toast**: close-to-tray, periodic update check (interval setting, default 6 h), Windows toast "5 updates available" that opens the Updates page; optional start at sign-in (HKCU `Run` key, starts minimised to tray).
- **Silent CLI switches**: `-UpdateAll`, `-ListUpdates`, `-Install <id>`, `-Export <path>`, `-Import <path>`; exit code 0/1 for scheduled tasks; logs to the same daily file.
- **Secure self-update** from GitHub Releases (see below).

Settings page: theme, log retention, check interval, start at sign-in, close-to-tray, include unknown versions, default install scope.

Deliberately **not** in v1 (name them so we don't drift): auto-closing apps that block an update, multiple languages, scheduled updates via Task Scheduler (the silent switch + a task you create covers it), WinGet Configuration/DSC, Intune packaging, editing winget's `settings.json`.

## Self-update design

1. On start (and from Settings → Check now) call the GitHub Releases API for the fixed repo `owner/WingetGui`, HTTPS only, compare `tag_name` with the running version.
2. Download the release zip to a temp folder and verify its **SHA-256 against the asset `digest`** GitHub publishes in the API, plus a `SHA256SUMS` file in the release.
3. Verify the exe's **Authenticode signature** if we ever sign; until then the hash is the check. Note: a hash from the same release proves the download is intact, not that the repo wasn't compromised — signing is the real fix.
4. A small updater step (the new exe started with `-ApplyUpdate <old folder>`) waits for the old process to exit, swaps the folder, relaunches. Old folder kept as `.previous` for rollback.

Needs: a GitHub repo with Releases, and a release workflow (BingWallpaper's tag-triggered workflow is the template).

## Project layout

```
WingetGui.slnx
src/WingetGui.Core/      winget access (COM + CLI fallback), operation queue, export/import, logging, settings, self-update — no UI
src/WingetGui/           WinUI 3 app: Program.cs, pages (Installed, Updates, Search, Sources, Settings), tray, toasts
tests/WingetGui.Core.Tests/  xUnit: export/import JSON, CLI fallback parsing, version compare, log rotation, hash check
scripts/publish.ps1, scripts/Install-WingetGui.ps1
.github/workflows/release.yml
```

Likely NuGet packages: `Microsoft.WindowsAppSDK`, `CommunityToolkit.Mvvm`, `H.NotifyIcon.WinUI` (WinUI 3 has no tray icon API), and `Microsoft.WindowsPackageManager.ComInterop` (confirmed in Phase 0). Toasts use the Windows App SDK's own `AppNotificationManager`, which works unpackaged.

## Phases

0. ~~**Spike (go/no-go on the COM API)**~~ **Done — go.** See *Phase 0 findings*. Tiny console app: create the COM objects from an unpackaged .NET 10 process; list installed, find upgrades, search, install with progress and cancel, uninstall. Record which of pins / source add/remove/reset exist in the API on winget 1.29, and what happens when elevated. Outcome decides the CLI fallback list above.
1. ~~**App shell + core**~~ **Done (v0.1.0).** Navigation, theme, logging, Installed / Updates / Search / details, install-upgrade-uninstall through the operation queue, Activity page with cancel. See *Phase 1 notes*.
2. **Management**: sources, pins, export/import, Settings page.
3. **Background**: single instance, tray, update-check timer, toasts, start at sign-in, silent CLI switches.
4. **Ship**: publish + install scripts, self-update, GitHub release workflow, README/BACKLOG/CHANGELOG.

Each phase ends runnable; Core tests run with `dotnet test`.

## Phase 0 findings (2026-11-11, winget 1.29.380, Windows 11, not elevated)

Spike is in `spike/` (`dotnet build`, then `WingetSpike.exe info|catalogs|list|updates|search <q>|show <id>|install|upgrade|uninstall <id>|addsource|probe`).

- **Package**: `Microsoft.WindowsPackageManager.ComInterop` 1.29.380 ships a ready CsWinRT projection (`lib/net8.0-windows10.0.26100.0`). Needs a Windows TFM (`net10.0-windows10.0.26100.0`) **and** a `RuntimeIdentifier`, because it copies the native `Microsoft.Management.Deployment.dll` that activates winget's out-of-proc COM server. Works from an unpackaged process; `new PackageManager()` ≈ 1 s cold, ~130 ms warm.
- **Gotcha — no `foreach` on API lists**: the cross-process proxy doesn't answer QueryInterface for `IIterable`, so `foreach`/LINQ on `Matches`, `GetPackageCatalogs()` etc. throws `InvalidCastException: No such interface supported`. Index with `Count` + `[i]` (a 1-line `Items()` helper).
- **Gotcha — composite catalog**: for `CompositeSearchBehavior.LocalCatalogs` pass only the remote catalogs; adding `GetLocalPackageCatalog(InstalledPackages)` yourself gives `InvalidOptions`.
- **Works**: list installed (206 pkgs, ~3 s), updates (~7.5 s), search (~29 s first call incl. msstore + source refresh — search should run per source and stream results), details metadata (publisher, licence, homepage), install/upgrade/uninstall with progress states `Queued → Downloading (with %) → Installing → PostInstall`, **cancel** via `AsTask(ct, progress)` (stopped mid-download, nothing left installed), `RepairPackageAsync` exists too.
- **Updates differ from the CLI, on purpose**: CLI showed 7 upgrades, COM 6. Missing one: PowerShell 7.6 (MSI) → 7.7 (MSIX only). `IsUpdateAvailable` is false because no installer of the same technology applies; the CLI lists it but can't do an in-place upgrade. UI: show such rows greyed as "newer version, not applicable".
- **In the API**: `AddPackageCatalogAsync`, `RemovePackageCatalogAsync`, `EditPackageCatalog`, `DownloadPackageAsync`, `RepairPackageAsync`. **Not in the API**: pins, export.
- **Not tested**: running elevated (would need a UAC click; the app is designed not to need it), running from a scheduled task for `-UpdateAll`, whether `IsUpdateAvailable` honours pins (the two pinned packages here, Contoso Agent and Fabrikam Office, showed in neither list, so it's unknown whether they have updates), Windows 10.

## Phase 1 notes (2026-11-11)

Build/run: `dotnet build WingetGui.slnx`, `dotnet test tests/WingetGui.Core.Tests`, run `src\WingetGui\bin\Debug\net10.0-windows10.0.26100.0\win-x64\WingetGui.exe`.

- **Layout as built**: `WingetGui.Core` = `WingetService` (all COM API use), `OperationQueue` (one at a time, cancel, `Drained` → refresh), `InstalledStore` (shared installed list), `Log`, `AppSettings`. App = `MainWindow` (NavigationView) hosting `Views/PackagesView` (one view, three modes: Installed / Updates / Search), `ActivityView`, `SettingsView`. Views are UserControls kept alive by MainWindow rather than Frame navigation, so a search or selection survives switching pages. Code-behind, not view models: the views are thin, and `QueuedOperation` is the only thing that needed change notification (CommunityToolkit.Mvvm).
- **Gotcha — reference ComInterop from the app too**: the package copies its native `Microsoft.Management.Deployment.dll` from `build/` targets, which only run in projects that reference it directly. With only Core referencing it the app gets `REGDB_E_CLASSNOTREG` (0x80040154) at `new PackageManager()`.
- **Gotcha — ListView accessible names**: rows are read out as the item's `ToString()`, so `PackageItem` overrides it (otherwise screen readers say "WingetGui.Core.PackageItem").
- **Gotcha — one ContentDialog at a time**: a second `ShowAsync` throws, and from an `async void` click handler that ends the app; the uninstall confirmation has a re-entry guard.
- **`Log` writes nothing until `Log.Init`**, so tests never touch the user's real log.
- **Checked in the real app**: 206 installed / 6 updates (PowerShell greyed as not applicable, with tooltip), search across sources with details (publisher, description, homepage, licence, release notes), install and uninstall of `jqlang.jq` from the UI via the queue (list refreshed itself both times), uninstall confirmation, light theme switch. 11 Core tests pass.
- **Not checked**: Update / Update all on a real package (would change real apps), cancel from the Activity page in the app (covered by queue tests and the Phase 0 spike), version/scope pickers on install, Windows 10, high-contrast themes.
- **Known limits (Phase 1)**: search results don't refresh after installing from them (the Installed page does); closing the window while an operation runs just closes (Phase 3 adds close-to-tray); no app icon yet.

## Open questions

- App name (placeholder `WingetGui`).
- GitHub repo for releases/self-update (public or private? a private repo needs a token, which we don't want in the app).
- Code signing later (SmartScreen will warn on an unsigned exe).
