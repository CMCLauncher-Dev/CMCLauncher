<div align="center">

<img src="docs/assets/cmc-logo.png" alt="CMCLauncher logo" width="120" height="120" />

# CMCLauncher

A Windows launcher for Minecraft Java Edition.

[English](README.md) · [简体中文](README_zh.md) · [日本語](README_ja.md)

<p>
<a href="https://github.com/CMCLauncher-Dev/CMCLauncher/releases"><img alt="Latest release" src="https://img.shields.io/github/v/release/CMCLauncher-Dev/CMCLauncher?style=flat-square&amp;color=2088ff" /></a>
<a href="https://github.com/CMCLauncher-Dev/CMCLauncher/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/CMCLauncher-Dev/CMCLauncher/total?style=flat-square&amp;color=35b879" /></a>
<a href="https://github.com/CMCLauncher-Dev/CMCLauncher/actions/workflows/ci.yml"><img alt="CI" src="https://img.shields.io/github/actions/workflow/status/CMCLauncher-Dev/CMCLauncher/ci.yml?branch=main&amp;style=flat-square&amp;label=build" /></a>
<a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/license-GPL--3.0-7654c4?style=flat-square" /></a>
<br />
<img alt="Windows x64" src="https://img.shields.io/badge/Windows-x64-0078d4?style=flat-square" />
<img alt=".NET 8" src="https://img.shields.io/badge/.NET-8-512bd4?style=flat-square" />
<img alt="WinUI 3" src="https://img.shields.io/badge/WinUI-3-00a4ef?style=flat-square" />
<a href="https://github.com/CMCLauncher-Dev/CMCLauncher/stargazers"><img alt="GitHub stars" src="https://img.shields.io/github/stars/CMCLauncher-Dev/CMCLauncher?style=flat-square" /></a>
</p>

### [Download](https://github.com/CMCLauncher-Dev/CMCLauncher/releases) · [Website](https://cmcl.ctfun.top/) · [Report an issue](https://github.com/CMCLauncher-Dev/CMCLauncher/issues)

</div>

Built with C#, .NET 8 and WinUI 3. CMCLauncher installs Minecraft and mod loaders, manages instances, and downloads community content.

<div align="center">
<img src="docs/assets/launcher-home.png" alt="CMCLauncher home" width="960" />
</div>

## Download and run

Download the Windows x64 ZIP or EXE from [Releases](https://github.com/CMCLauncher-Dev/CMCLauncher/releases). Extract the ZIP before running `CMCLauncher.exe`. The single EXE extracts its runtime on first launch. Use a writable folder.

Requires Windows 10 version 2004 (build 19041) or later. Release builds include .NET and the Windows App SDK; Java is installed separately.

1. Choose where to store Minecraft files on first launch. You can import existing instances or add more directories later.
2. Scan, import or download Java in **Settings → Java**.
3. Sign in under **Settings → Accounts**.
4. Download a game version or import a modpack, then select an instance and account to launch.

Microsoft sign-in requires a Minecraft Java Edition profile. Account availability depends on the launcher's ownership checks and system region. Browser authorization is followed by Xbox and Minecraft profile verification.

## Features

- **Instances:** isolated game folders, multiple Minecraft directories, favorites, and imports from PCL, HMCL, MultiMC and Prism. Migration is available for directories created and recorded by CMCLauncher.
- **Games and modpacks:** Minecraft releases and snapshots; Fabric, Forge and NeoForge; Modrinth `.mrpack` files and ZIP archives with recognized manifests.
- **Community content:** Modrinth and CurseForge search, filters, pagination, project details and compatible file selection. Mod updates keep previous files in `mod-backups`.
- **Worlds and schematics:** CurseForge map downloads, world backup and restore; schematic import, export, rename and folder browsing. Blueprint downloads from Schematicraft, MCS and CMS can be saved to compatible instances.
- **Java and downloads:** runtime discovery, Temurin downloads, automatic Java selection, memory allocation, transfer progress and cancellation.
- **Tools:** Minecraft skin avatars exported as PNG, Frp Client downloads and saved INI/TOML configurations, runtime logs and game error reports.
- **Appearance:** light, dark and system modes; JSON theme presets, fonts and scaling. Seven interface languages are included.

Forge and NeoForge compatibility varies by game version. ZIP imports need a recognized manifest. Shaders require an appropriate shader loader, and schematic compatibility depends on the installed mod. CurseForge downloads also depend on application credentials and the author's distribution settings.

CMS browsing reads HTML pages. Searches run only when submitted, results are cached, and requests are rate-limited. Access restrictions or changes to the site can interrupt browsing; the page provides a link to the original website.

## Settings and data

| Location | Contents |
| --- | --- |
| `%APPDATA%/CMCLauncher.Native/settings.json` | Settings, instance records and encrypted account credentials |
| `%APPDATA%/CMCLauncher.Native/library/` | Default library; game directories can be changed in the launcher |
| `%APPDATA%/CMCLauncher.Native/runtimes/` | Downloaded Java runtimes |
| `logs/` beside the executable | Launcher logs |
| `logs/` and `crash-reports/` in each instance | Minecraft logs and crash reports |

Accounts use Windows DPAPI encryption. Moving settings to another Windows user or device may require signing in again. Back up saves from their actual instance directories.

Frp Client configurations are copied into the launcher's managed configuration directory; imported originals are left untouched. The client is downloaded separately from official frp releases. New configurations should use TOML; INI support depends on the chosen client version.

Launcher updates are available under **More → About**. Startup checks and the Beta channel can be changed there. Updates verify the release package's SHA-256 digest and require running tasks and games to finish first.

Language packs and theme presets use editable JSON files with built-in fallbacks. See [language packs](docs/LOCALIZATION.md) and [theme presets](docs/THEMES.md).

## Build from source

Install the .NET 8 SDK and Windows SDK tools for WinUI 3 development, then run:

```powershell
git clone https://github.com/CMCLauncher-Dev/CMCLauncher.git
cd CMCLauncher
dotnet run --project CMCLauncher.WinUI/CMCLauncher.WinUI.csproj -p:Platform=x64
```

For a self-contained test build, run `build-and-run.cmd`. It builds into `.artifacts/local-runs/` and launches the result using your normal launcher settings. To build without launching:

```powershell
./build-and-run.ps1 -NoLaunch
```

### Login configuration

Microsoft sign-in needs a public OAuth client ID in the Git-ignored `.config/clientid.local.json`:

```json
{
  "clientId": "YOUR_MICROSOFT_CLIENT_ID"
}
```

Use a valid GUID for an application configured to access Microsoft and Minecraft services. The ID is embedded during compilation. Tagged CI builds read it from the `MICROSOFT_CLIENT_ID` repository secret.

For local CurseForge testing:

```cmd
build-and-run.cmd "-curseforgekey=YOUR_KEY" -NoLaunch
```

The build script uses a temporary configuration file. Embedded developer keys can be recovered from binaries; do not treat build-time injection as secret storage. See [CurseForge integration](docs/CURSEFORGE.md).

### Publish

```powershell
dotnet publish CMCLauncher.WinUI/CMCLauncher.WinUI.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o dist/cmc-launcher
./scripts/package-release.ps1 -PublishDirectory dist/cmc-launcher -ArchivePath dist/CMCLauncher-win-x64.zip
```

For a single EXE:

```powershell
./scripts/publish-singlefile.ps1
```

Publish into a fresh directory to avoid retaining obsolete files. Release builds use partial trimming and preserve application assemblies for reflection and WinUI. Add `-p:PublishTrimmed=false` when comparing with an untrimmed build.

CI runs Core tests on all three systems and Windows regression tests before packaging. Windows builds WinUI ZIP/EXE packages; Linux x64 builds Avalonia DEB, RPM, AppImage and TAR.GZ packages; macOS ARM64 builds an Avalonia TAR.GZ package. Published WinUI/Linux pages and the Windows single-file startup are checked in CI. Workflow artifacts use fixed filenames; tagged release assets include the version. Pushes to `main`, pull requests and manual runs build artifacts; supported version tags also create a GitHub release.

### Avalonia frontend

```sh
dotnet run --project CMCLauncher.Avalonia/CMCLauncher.Avalonia.csproj
dotnet publish CMCLauncher.Avalonia/CMCLauncher.Avalonia.csproj -c Release -r linux-x64 --self-contained true -o .artifacts/avalonia-linux
```

Windows uses WinUI. Publish Avalonia with `linux-x64` or `osx-arm64` for Linux/macOS. See the [frontend notes](CMCLauncher.Avalonia/README.md) for credential-store requirements and platform verification status, and [Linux packages](docs/LINUX_PACKAGES.md) for installation and compatibility. CI packages Unix applications as TAR archives to retain executable permissions.

### Tests

Portable core checks:

```powershell
dotnet run --project CMCLauncher.Tests/Core/CMCLauncher.Core.Tests.csproj -c Release
```

Windows regression tests (requires Java on PATH):

```powershell
dotnet run --project CMCLauncher.Tests/Windows/CMCLauncher.Tests.csproj -c Release
```

After publishing, run the UI check:

```powershell
& ./dist/cmc-launcher/CMCLauncher.exe --verify-ui "$PWD/.artifacts/winui-check"
```

UI checks use isolated data and write screenshots, `passed.txt` and, on failure, `failure.txt`.

## Source layout

See [architecture and platform boundaries](docs/ARCHITECTURE.md) for the migration status.

| Directory | Contents |
| --- | --- |
| [CMCLauncher.Core/](CMCLauncher.Core/) | `net8.0` domain services, networking, storage and resource providers |
| [CMCLauncher.Platform/](CMCLauncher.Platform/) | Host contracts and Windows/Linux/macOS integration |
| [CMCLauncher.WinUI/](CMCLauncher.WinUI/) | Windows frontend and entry point |
| [CMCLauncher.Avalonia/](CMCLauncher.Avalonia/) | Linux/macOS frontend; see its README for validation status |
| [CMCLauncher.Tests/Core/](CMCLauncher.Tests/Core/) | Portable core checks |
| [CMCLauncher.Tests/Windows/](CMCLauncher.Tests/Windows/) | Windows regression tests and fixtures |
| [scripts/](scripts/) | Build, packaging and maintenance scripts |
| [docs/](docs/) | Website, documentation and screenshots |

The WinUI application targets Windows x64. Linux/macOS use Avalonia over the shared Core and platform adapters; Linux/macOS game execution still requires host testing. See the [dependency and portability audit](docs/DEPENDENCY_AUDIT.md) for test results and remaining work.

## Contributing

Report bugs through [Issues](https://github.com/CMCLauncher-Dev/CMCLauncher/issues). Include your launcher and Windows versions, Minecraft and loader versions, steps to reproduce, and relevant logs. Remove tokens and other account information before sharing logs.

For code changes, run the relevant tests. Include screenshots for UI changes and check cancellation and cleanup when changing installation or downloads.

## License

[GNU General Public License v3.0](LICENSE). Third-party dependencies retain their own licenses.

Minecraft, mods and modpacks belong to their respective owners. CMCLauncher is not affiliated with Mojang Studios, Microsoft or NetEase.