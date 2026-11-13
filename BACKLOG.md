# Backlog

## Known limitations

- **Not code-signed.** SmartScreen warns on first run, and self-update trusts the SHA-256 digest GitHub publishes for the release zip: that proves the download is the released file, not who released it. Signing (and checking the signature before applying an update) is the fix.
- **Size.** The release zip is ~100 MB because .NET and the Windows App SDK are bundled (self-contained, so nothing else needs installing). Trimming or framework-dependent builds could shrink it.
- **Source add/remove/reset on a separate admin account**: the elevated process may not have the `winget` app alias; the error says so.
- **Pin list parsing** reads `winget pin list`'s table (no JSON output). A localised header with a space in an earlier column name would shift columns; reading winget's `pinning.db` would fix it.
- **Notifications** depend on the Start menu shortcut (it carries the app ID and click handler). Deleting it stops notifications until the app recreates it on next start. The click handler's CLSID is read from the Windows App SDK's registry entry, which isn't public API.
- **Search results** don't refresh after installing from them (the Installed page does).
- **Installer download link** (details pane) is read from `winget show`'s English "Installer Url:" label, since the COM API doesn't expose it. With winget in another display language the link isn't shown; the installer type line (from the API) still is.
- **Filter panel choices** (sources, filters, search mode) reset when the app restarts.
- **Search matches name and ID only** (since 0.1.6), not tags or monikers, so "zip" no longer finds 7-Zip.
- **Arm64 as a saved architecture** is ignored on Windows 10 1809, which has no Arm64 value in the API; winget picks the installer there.

## Not tested yet

Tray icon clicks and menu (UI Automation can't reach the overflow area), `-UpdateAll` and `-Import` end to end, exit confirmation with close-to-tray off, a background timer tick, Windows 10, a non-default install scope on a real install, an install or update that uses saved install options (saving, forgetting and skipping were tested; the options reaching winget weren't), the filter panel in a narrow window.

Checked on real releases: self-update (0.1.0 → 0.1.7, one release at a time), the About card's stacked buttons during an update, the filter panel, the centred window.

## Ideas

- **Release notes in the app** before installing a Winget GUI update. Settings → About already links to the GitHub release (What's new); this would show the notes inline.
- **Operation history** kept between restarts: Activity forgets everything on exit. UniGetUI keeps the last 1,000 operations with their output.
- **Automatic updates for chosen packages only.** Would replace the "built-in scheduled updates" entry under Considered and not doing, which assumed all-or-nothing `-UpdateAll`.
- **Control installer shortcuts**: stop installers adding desktop shortcuts, re-applied after every update. Fiddly (watching for new `.lnk` files).
- **Portable mode**: settings and logs next to the exe instead of `%LOCALAPPDATA%`. Small: `AppSettings.DataRoot` and `Log.Folder`.
- Auto-close apps that block their own update (WinGet-Manager does this).
- More languages.

## Considered and not doing

- **Parsing `winget.exe` output for everything**: breaks on long names, locales and new winget versions; the COM API gives typed results and progress. `winget.exe` is used only where the API has nothing (pins, export, elevated source changes, the installer download link).
- **Running elevated**: the COM API's out-of-proc server isn't meant for elevated callers. Installers that need admin ask for it themselves.
- **WPF**: WinUI 3 was the brief and runs fine on Windows 10/11 desktops (the Server 2019 problem BrowserDeploy hit doesn't apply here).
- **Built-in scheduled updates of everything**: `-UpdateAll` in a Task Scheduler task covers it. (Chosen packages only is under Ideas.)
- **Private release repository**: self-update would need a token stored in the app.
- **Other package managers** (Scoop, Chocolatey, pip, npm…), as UniGetUI does: each needs its own CLI parsing, which is the fragility the COM API avoids. Winget only.
- **Backup sync to a GitHub Gist and share links** (UniGetUI): need accounts or tokens; export/import covers moving machines.
