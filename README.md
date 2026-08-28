<div align="center">
  <img src="LocaleGameHub/Resources/VNAR.png" alt="VNAR logo" width="240" />

# VNAR

**Your visual novel library, without the locale headaches.**

A lightweight Windows launcher for visual novels with Locale Emulator integration, VNDB metadata, cover editing, developer browsing, favorites, WebP support, and smart desktop shortcuts.

[Español](README.es.md) · [Download Beta 1.1](https://github.com/JAVCIF/vnar/releases/tag/v1.0.0-beta.1.1)
</div>

## Downloads

> **Windows 10/11 x64**

| Package | Description | Download |
| --- | --- | --- |
| Installer | Self-contained installation with Start Menu integration and optional desktop shortcut | [VNAR-Setup.exe](https://github.com/JAVCIF/vnar/releases/download/v1.0.0-beta.1.1/VNAR-Setup.exe) |
| Portable | Self-contained portable build; extract and run `VNAR.exe` | [VNAR-Portable-win-x64.zip](https://github.com/JAVCIF/vnar/releases/download/v1.0.0-beta.1.1/VNAR-Portable-win-x64.zip) |

Locale Emulator is **not bundled** with VNAR. On first launch, VNAR can use an existing `LEProc.exe` or download the official Locale Emulator release for you.

The portable package needs no installation, but both packages store settings, the game library, and artwork in `%LOCALAPPDATA%\VNAR`. Back up that folder to preserve your library. Builds are currently unsigned, so Windows may show a SmartScreen warning. Release assets include `SHA256SUMS.txt` for integrity checks.

## What is VNAR?

VNAR is a Windows launcher focused on visual novels and other region-dependent games. It keeps your library in one place and launches configured games through Locale Emulator without changing the Windows system locale.

Each game can keep its own executable, command-line arguments, administrator preference, VNDB metadata, cover artwork, favorite status, and desktop shortcut behavior.

## Features

- **Locale Emulator integration** using the real `Run in Japanese` and `Run in Japanese (Admin)` profiles.
- **First-run Locale Emulator setup**, including optional download and extraction of the official release.
- **EXE drag & drop**, individual executable import, and recursive game-folder scanning.
- **VNDB integration** for visual novel titles, covers, and developer metadata.
- **Developer browser** that groups configured VNDB games by developer.
- **Favorites** with a dedicated tab and quick star toggle directly on game cards.
- **Configurable pagination** from 10 to 50 entries per page.
- **English and Spanish UI**, with the initial language selected from the Windows UI language (Spanish for `es-*`, English otherwise).
- **Non-destructive cover editing** with zoom, positioning, black/white/transparent/blur backgrounds, and high-quality export.
- **WebP compatibility** through SkiaSharp normalization.
- **Browser image drag & drop** for game and developer artwork.
- **Desktop shortcut generation** with selectable icons extracted from executables in the game folder.
- **Smart shortcuts** launch by VNAR game ID, so later changes to the game's Locale Emulator/admin configuration remain effective.
- **Double-click launch** and contextual actions directly from the library.
- **Dark VNAR interface** with themed controls and scrollbars.
- **No background service required.**

## Quick start

1. Download either the installer or portable build.
2. Launch VNAR.
3. On first launch, select your existing `LEProc.exe` or use **Download / configure LE**.
4. Add an executable, drag a game folder into VNAR, or scan a library folder.
5. Configure the game and optionally add its VNDB ID.
6. Double-click its cover or press **Play**.

## Locale Emulator

VNAR interoperates with [Locale Emulator](https://github.com/xupefei/Locale-Emulator) as an external application. It reads the configured Locale Emulator profiles and launches games using `LEProc.exe -runas <profile-guid>`.

VNAR does not modify the game executable and does not require changing the Windows system locale.

## VNDB and cover search

VNDB integration is used for visual novel metadata, cover artwork, and developer associations. Google Images results inside VNAR are optional and require a user-provided SerpApi key. Browser drag & drop and VNDB search remain available without SerpApi.

## Building from source

Requirements:

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

```powershell
dotnet restore .\LocaleGameHub\LocaleGameHub.csproj
dotnet run --project .\LocaleGameHub\LocaleGameHub.csproj
```

Portable release build:

```powershell
dotnet publish .\LocaleGameHub\LocaleGameHub.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The repository also includes `build_portable.bat` and `build_small.bat` for local Windows builds.

## Project status

Current public build: **Beta 1.1 (`1.0.0-beta.1.1`)**.

Bug reports and suggestions are welcome through GitHub Issues.

## License

VNAR source code is licensed under the [MIT License](LICENSE).

Third-party components and online services retain their own licenses and terms. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Disclaimer

VNAR is not affiliated with Locale Emulator, VNDB, SerpApi, or any visual novel developer or publisher. Game artwork, covers, executable icons, and user-imported assets belong to their respective copyright holders.
