# Changelog

## Unreleased

## 1.0.0-beta.1.3

- Data files (`library.json`, `developers.json`, `settings.json`) are written atomically, keeping the previous version as `.bak`.
- A damaged data file is no longer replaced by an empty library: it is set aside as `*.corrupt-<date>.json`, the `.bak` is restored when valid, and VNAR tells the user what happened (also logged to `recovery.log`).
- Only one library window per session; launching VNAR again brings the existing window to the front.
- Saves from the library window and from game shortcuts are serialized across processes, and the "last played" time recorded by a shortcut is no longer lost.
- Library cards decode covers at their on-screen pixel size (DPI-aware, never upscaled) with high-quality scaling, cache them, and no longer keep image files locked. Much lower memory use with large covers.
- Folder scans run in the background with a progress dialog and can be cancelled; dropping a folder no longer freezes Explorer's drag operation.
- Searching in the Developers tab no longer rewrites `developers.json` on every keystroke.
- Unused cover files (old renders, covers of removed games, interrupted saves) are moved to `covers/_trash` at startup and deleted after 30 days. Files newer than 24 hours and originals the cover editor may still need are kept, and cleanup is skipped after any data recovery.
- Downloads are size-limited (40 MB per image, 100 MB for the Locale Emulator archive) and never leave partial files behind.
- The SerpApi key is stored encrypted with Windows DPAPI for the current user; existing plain-text keys are migrated automatically. A portable copy moved to another PC or user will ask for the key again.
- New "Modern" shortcut type (default): a `.url` that opens `vnar://launch/<id>`, the same approach as Steam and Playnite. No shortcut points at `VNAR.exe`, so Windows no longer shows VNAR with the name or icon of the last game opened. Modern shortcuts cannot be pinned to the taskbar; "Classic" `.lnk` shortcuts remain available for that, and existing ones keep working. The `vnar://` protocol is registered per user only when a modern shortcut is first created, refreshed at startup if the portable folder moves, and accepts only valid launch links.
- Shortcuts can use a custom `.ico`; it is validated and copied into VNAR's data folder (`icons\<game>_<hash>.ico`, so the same icon is never stored twice) and the copy is removed if creating the shortcut fails. Icons of removed games are cleaned up at startup.
- Modern shortcuts that use the generic icon point to a stable copy in the data folder, so moving a portable VNAR does not leave them without an icon.
- `VNAR.ShellTests` now covers modern shortcuts end to end on Windows: protocol registration and refresh, the `.url` written through COM (URL and icon), and a double-click that starts VNAR, launches the game through a stand-in LEProc and records it. The user's `vnar://` registry key is backed up and restored.
- Importing many games (scan, multi-select, drag and drop) saves the library once instead of once per game.
- Library card covers are decoded on background threads; page changes and searches no longer wait for image decoding.
- Cover cleanup also runs after the whole library is emptied (it still skips fresh installs and any session with a data recovery).
- New `VNAR.CoreTests` suite, run in CI: storage and recovery, saves from several processes at once, bulk import, cover cleanup, decode sizing, scanning, download limits, `vnar://` links, version and DPAPI. Tests use a temporary data folder (`VNAR_DATA_DIR`) and refuse to run against a real library.
- The version is defined once in `Directory.Build.props`; the User-Agent, installer version, release tag and release title derive from it, and release notes come from the matching CHANGELOG section.

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
