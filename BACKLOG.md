# Backlog

## Known limitations

- **Not code-signed.** SmartScreen warns on first run, and self-update trusts the SHA-256 digest GitHub publishes for the release zip: that proves the download is the released file, not who released it. Signing (and checking the signature before applying an update) is the fix.
- **Size.** The release zip is ~100 MB because .NET and the Windows App SDK are bundled (self-contained, so nothing else needs installing). Trimming or framework-dependent builds could shrink it.
- **Source add/remove/reset on a separate admin account**: the elevated process may not have the `winget` app alias; the error says so.
- **Pin list parsing** reads `winget pin list`'s table (no JSON output). A localised header with a space in an earlier column name would shift columns; reading winget's `pinning.db` would fix it.
- **Notifications** depend on the Start menu shortcut (it carries the app ID and click handler). Deleting it stops notifications until the app recreates it on next start. The click handler's CLSID is read from the Windows App SDK's registry entry, which isn't public API.
- **Search results** don't refresh after installing from them (the Installed page does).

## Not tested yet

Tray icon clicks and menu (UI Automation can't reach the overflow area), `-UpdateAll` and `-Import` end to end, exit confirmation with close-to-tray off, a background timer tick, Windows 10, a non-default install scope on a real install.

## Ideas

- Release notes for a Winget GUI update shown in the app.
- Auto-close apps that block their own update (WinGet-Manager does this).
- More languages.

## Considered and not doing

- **Parsing `winget.exe` output for everything**: breaks on long names, locales and new winget versions; the COM API gives typed results and progress. `winget.exe` is used only where the API has nothing (pins, export, elevated source changes).
- **Running elevated**: the COM API's out-of-proc server isn't meant for elevated callers. Installers that need admin ask for it themselves.
- **WPF**: WinUI 3 was the brief and runs fine on Windows 10/11 desktops (the Server 2019 problem BrowserDeploy hit doesn't apply here).
- **Built-in scheduled updates**: `-UpdateAll` in a Task Scheduler task covers it.
- **Private release repository**: self-update would need a token stored in the app.
