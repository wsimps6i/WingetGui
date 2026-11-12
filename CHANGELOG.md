# Changelog

## 0.1.7 (2026-11-12)

- Skip a version: hide one available version of a package from updates (Update all, the count, notifications and `-UpdateAll`); a newer version shows up again.
- Install options per package: scope for new installs, architecture and extra installer arguments, remembered and used for every install and update of that package.
- Installer details: type, architecture, scope and whether it needs admin, plus the download link, size and SHA-256.

## 0.1.6 (2026-11-12)

- Filter panel on Installed, Updates and Search: tick which sources to show or search, show only packages that have an update, are pinned or need a reinstall, and on Search match the name, ID or both, optionally exactly. The filter button next to the actions hides it.
- The window opens wider (still fitted to smaller screens) to make room for the panel.
- Search now matches name and ID only, not tags.

## 0.1.5 (2026-11-12)

- Settings → About: the update buttons are stacked, so the version and download status are no longer cut off.

## 0.1.4 (2026-11-12)

- The window opens in the middle of the screen, above the taskbar, and fits smaller screens.

## 0.1.3 (2026-11-12)

- Settings page laid out as cards in sections, like the Windows Settings app.

## 0.1.2 (2026-11-12)

- Activity shows download progress as a percentage and size ("Downloading 42% (12.3 of 29.1 MB)"), and install progress when the installer reports it.

## 0.1.1 (2026-11-11)

- If a self-update can't be applied, the app now says why in Settings (with a dot on Settings) the next time it starts, instead of only in the log.

## 0.1.0 (2026-11-11)

First release.

- Installed, Updates and Search pages with a details pane; install (version and scope), update, uninstall, one at a time with progress and cancel (Activity).
- Updates that can't be applied in place shown greyed out.
- Pins, sources (add / remove / reset, elevated), export and import.
- Notification area icon, close to tray, background update checks with notifications, start at sign-in.
- Command line: `-ListUpdates`, `-UpdateAll`, `-Install`, `-Export`, `-Import`.
- Light / dark / system theme, daily logs.
- Self-update from GitHub Releases with SHA-256 verification; per-user installer.
