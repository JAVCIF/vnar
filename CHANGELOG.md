# Changelog

## 1.0.0-beta.1.2

- Fix taskbar icon collisions between the VNAR launcher and game shortcuts targeting the same executable.
- Assign a stable launcher AppUserModelID and a distinct ID per game, before creating windows.
- Persist game IDs in shortcut properties without changing their icons, targets, or arguments.
- Set the launcher ID on installer-created shortcuts.
- Add Windows integration checks for process identity and shortcut persistence, with normal/admin game settings.


## 1.0.0-beta.1.1

First public GitHub beta package.

Highlights:

- Locale Emulator profiles and first-run setup.
- Game library import, drag & drop, scanning, search, pagination, favorites, and developer grouping.
- English and Spanish UI.
- VNDB metadata and cover integration.
- Non-destructive cover editor with HQ export and multiple background modes.
- WebP normalization through SkiaSharp.
- Smart desktop shortcuts with selectable executable icons.
- Themed context menus and scrollbars.
