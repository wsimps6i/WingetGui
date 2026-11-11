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
2. ~~**Management**~~ **Done.** Sources page, pins, export/import, default install scope. See *Phase 2 notes*.
3. ~~**Background**~~ **Done.** Single instance, tray, update-check timer, notifications, start at sign-in, silent CLI switches, app icon. See *Phase 3 notes*.
4. **Ship**: publish + install scripts, self-update, GitHub release workflow, README/BACKLOG/CHANGELOG. See *Phase 4 notes*.

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

## Phase 2 notes (2026-11-11)

- **Where things live**: `WingetCli` (Core) is the only winget.exe caller: `pin list/add/remove`, `export`, and elevated `source add/remove/reset`. `ImportFile` parses export files; `WingetService.ResolveAsync` looks each id up (exact match, in the file's source if configured here, else all sources) and `GetSourcesAsync` lists sources through the COM API. UI: new **Sources** page; **Pin/Unpin** in the details pane; **⋯ → Export / Import package list** on the Installed page; **Default install scope** in Settings (used by Install selected, imports, and the details pane's starting choice).
- **Pins**: loaded alongside the installed list (a pin-list failure only loses the decoration, not the list). Any pin type, Gating included, keeps a package out of Update selected / Update all and the Updates badge count; its Available column says "(pinned)" and its Update button "Pinned". Pinning needs a source package, so Add/Remove-Programs-only entries get no Pin button.
- **Pin table parsing**: columns come from the header line above the dashes, in winget's fixed order. shortcut: a localised header with a space in an earlier column name would shift columns; reading winget's `pinning.db` is the upgrade if that ever happens. Ids winget shortens with "…" are matched on the shown prefix.
- **Elevated source commands**: run via `runas` with one UAC prompt; output can't be captured across elevation, so only the exit code is shown (details in `winget --logs`). Name must match `^[A-Za-z0-9._-]{1,64}$`, URL must be absolute https without quotes, type must be one of the two known types: validated before anything reaches the elevated command line (tested). Declined UAC shows "Cancelled at the administrator prompt."
- **Export** uses `winget export` (exact schema; it warns about packages no source has but still writes the file). **Import** never runs winget import: it resolves, shows "N to install, M already installed, K not found", and after one confirmation queues the installs through the COM API, so each gets progress and cancel. A version in the file that's no longer available fails that one install with a clear message.
- **Checked in the real app**: Fabrikam/Contoso show as pinned with Update disabled; pin then unpin 7-Zip (verified with `winget pin list` after each); Sources page lists msstore/winget/winget-font with explicit flag; export through the real Save dialog (53 packages); re-import of that file → "0 to install, 53 already installed"; a mixed file → dialog with 1 to install (jq), 1 installed, 1 not found, then cancelled (nothing installed); Settings scope combo. 24 Core tests pass.
- **Not checked**: source add/remove/reset end to end (needs a UAC click and changes machine-wide sources), confirming an import install, a non-default install scope on a real install, a separate-admin-account machine (the elevated process may not have the winget alias; the error says so).

## Phase 3 notes (2026-11-11)

- **Program.cs** replaces the XAML Main (`DISABLE_XAML_GENERATED_MAIN`). Order: parse the command line → a switch (or a bad command line) runs `SilentRunner` with no window and returns its exit code → otherwise single instance (`AppInstance` key = SHA-256 of the exe path; **not** `string.GetHashCode()`, which .NET randomises per process, so two launches would never match) → register notifications → start WinUI. A second launch redirects to the running copy, which shows itself even from the tray, then the new process brings it to the front (Windows only lets the launching process take the foreground).
- **Silent switches** (`CommandLine` + `SilentRunner` in Core): `-ListUpdates`, `-UpdateAll` (skips pinned), `-Install <id>`, `-Export <file>`, `-Import <file>` (no confirmation), `-Help`; also `--x` and `/x`. Exit 0 OK / 1 failed / 2 bad command line. Unknown switches and typos are errors (exit 2 with usage), never a window: a scheduled task must not sit on an invisible window. `----…` arguments are Windows App SDK activation and ignored. Output: `AttachConsole` to the parent console if any, plus the daily log. A WinExe doesn't make cmd/PowerShell wait, so scripts use `start /wait` or `Start-Process -Wait` (in the usage text).
- **Tray** (`TrayIcon.cs`, H.NotifyIcon.WinUI 2.4.1): click opens; right-click menu Open / Check for updates now / Exit, as a native popup menu (works while the window is hidden; items need Commands, not Click handlers). Tooltip shows the update count. Disposed on exit, otherwise a dead icon lingers until hovered (it still does if the process is killed).
- **Close** hides to the tray (setting, default on). With it off, closing exits, asking first if operations are queued or running.
- **Background checks**: `DispatcherQueueTimer`, interval setting (Off / 1 / 3 / 6 / 12 / 24 h, default 6). After every refresh, updates not announced before (`id version` pairs) raise one notification **only when the window isn't on screen**; otherwise the badge already says it. Clicking it (running or not) opens the Updates page.
- **Notifications** (`AppNotificationManager`, unpackaged) — **gotcha, found the hard way**: on this Windows 11 build (26200) the shell **silently drops** notifications from an AppUserModelID that exists only in the registry, which is all the Windows App SDK registers for an unpackaged app. `Show` still returns an id, `Setting` says `Enabled`, history even stores them, but nothing pops up or reaches the notification centre. Proven by posting test toasts: registry-only IDs (name only, `.ico`, `.png`) never showed; the same toast under an ID backed by a Start menu shortcut showed at once. The fix (`StartMenuShortcut.cs`):
  1. `SetCurrentProcessExplicitAppUserModelID("WingetGui")` first thing; the Windows App SDK then registers under that fixed ID (instead of a GUID generated per exe path). Taskbar button and pins use it too.
  2. After `Register`, create/refresh `Start Menu\Programs\Winget GUI.lnk` with `System.AppUserModel.ID = WingetGui` (to be shown) and `System.AppUserModel.ToastActivatorCLSID` = the SDK's `CustomActivator` CLSID (for a click to reach the app). shortcut: that CLSID is read from the SDK's registry entry, which isn't public API. A shortcut pointing at another copy of the exe (the installed one) is left alone; same ID, so notifications still show.
  3. Saving a shortcut through the object that loaded it fails with `STG_E_ACCESSDENIED`; read with one object, write with a fresh one.
  - `Log.Init` moved to `Program.Main` so this setup can log.
  - **Phase 4's installer** should create this same shortcut (ID + activator), and its **uninstall must call `AppNotificationManager.Default.UnregisterAll()`** (e.g. a hidden `-Unregister` switch) and delete the shortcut, or `HKCU\Software\Classes\AppUserModelId\WingetGui` and its CLSID stay behind. If `Setting` isn't `Enabled` (user, group policy…), the log says why and only the tray tooltip shows updates.
- **Start at sign-in**: HKCU `Run` value `WingetGui` = `"<exe>" -Tray`; the registry is the source of truth (toggle reads it), so a moved exe shows as off.
- **App icon**: `Assets/build-icon.ps1` (adapted from MarkItDown-GUI's) → `AppIcon.ico`, used for the exe, window, tray and notifications.
- **Checked**: `-Nope` → exit 2 + usage; `-ListUpdates` → same 6 + PowerShell "not applicable", exit 0; `-Install jqlang.jq` twice (installed, then "already installed", both 0; removed afterwards); unknown id → 1; `-Export` → file written. Close → hidden, process alive; second launch → same process, window back; `-Tray` → hidden start, refresh, `Notified: 6 new updates` with a valid notification id and `Setting == Enabled`; Start at sign-in toggle writes and removes the Run value. 42 Core tests pass.
- **Notification checked after the fix**: "6 updates available — AWS Command Line Interface, GitHub Desktop, Microsoft Visual C++ … and 3 more" pops up with the app icon and is listed under "Winget GUI" in the notification centre (confirmed by the user); the shortcut carries the ID and the matching activator CLSID.
- **Clicking a notification** (confirmed by the user): brings the window to the front on the Updates page. Gotcha: the click arrived and the window opened, but **behind** other windows; `Activate()` doesn't take the foreground here, so `ShowAndActivate` also calls `SetForegroundWindow` (allowed right after a notification or tray click).
- **Not checked**: the tray icon's clicks and menu (hidden in the overflow, not reachable by UI Automation), `-UpdateAll` and `-Import` end to end (would change real apps), close-with-tray-off confirmation, a 6-hour timer tick (only the startup refresh).

## Phase 4 notes (2026-11-11)

- **Repository**: public `wsimps6i/WingetGui` (self-update needs no token). Before the first push the local history was rewritten to swap this machine's real pinned packages in test data and notes for made-up names.
- **Self-update** (`SelfUpdate` / `UpdateInstaller` in Core): GitHub "latest release" API → newer tag → the exact asset `WingetGui-<version>-win-x64.zip`, its download URL under this repo's `/releases/download/`, and the **`digest` GitHub records for the asset (`sha256:…`): no digest, no update**. Download hashes while writing; mismatch deletes it. Unzip to `%LOCALAPPDATA%\WingetGui\update\<version>` (.NET refuses entries escaping the folder) and require the exe inside to report that version. Then the app starts the *downloaded* exe with `-ApplyUpdate <install folder>` and exits; that copy retries renaming the install folder to `.previous` until the old process has let go (the rename fails while any file is in use), copies itself in, starts the new version, and on failure puts the old folder back and starts it. `.previous` is kept until the next update; the staging folder is cleared at the next start. Debug builds can check but never apply (they'd replace `bin\`).
- **Checks**: at startup and with each background check, when "Check for new versions of Winget GUI automatically" is on; quiet on failure (logged). A found update puts a dot on Settings; Settings → About Winget GUI has Check / Download and install / What's new.
- **Installer** (`scripts/Install-WingetGui.ps1`, from MarkItDown-GUI's): release zip given, or next to the script, or built from source. Checks the zip against `SHA256SUMS.txt` when present. Writes a *plain* Start menu shortcut; the app adds its app ID and notification activator on first run (`StartMenuShortcut.Ensure` only rewrites a shortcut pointing at its own exe). `-Uninstall` runs `WingetGui.exe -Unregister` (notification registration + Run key), removes shortcut and folders; `-RemoveSettings` also removes `%LOCALAPPDATA%\WingetGui`.
- **Release workflow** (`.github/workflows/release.yml`): tag `vX.Y.Z` → test → `publish.ps1 -Version X.Y.Z -Zip` → release with the zip, `SHA256SUMS.txt` and the install script. The tag sets the version; keep `<Version>` in the csproj and CHANGELOG in step.
- **Gotcha (publish.ps1)**: `$a = if (...) { @("x") }` unwraps to a string, and splatting a string into a native command passes it one character per argument (`/ p : V e r s i o n`). Wrap the whole `if` in `@( )`.
- **Checked locally**: publish + zip (102 MB) + SHA256SUMS; install from a "downloaded" folder refuses a tampered checksum and installs with the real one; the installed copy upgrades the shortcut (ID + activator) and its update check returns "up to date" (no releases yet). Self-update tests: digest required and well-formed, download must be this repo's, exact asset name, version compare, swap replaces files / refuses a non-install folder / rolls back when a copy fails / waits for the old app (59 tests).

## Open questions

- ~~App name~~ Winget GUI (`WingetGui`).
- ~~GitHub repo~~ public `wsimps6i/WingetGui`.
- Code signing later (SmartScreen will warn on an unsigned exe; it would also let self-update check who released a build, not just that it's the released file).
