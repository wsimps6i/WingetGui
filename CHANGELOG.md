# Changelog

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
