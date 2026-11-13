<div align="center">

<img src="docs/assets/cmc-logo.png" alt="CMCLauncher logo" width="120" height="120" />

# CMCLauncher

**Your Minecraft Java world starts here.**

[English](README.md) · [简体中文](README_zh.md) · [日本語](README_ja.md)

<p>
<a href="https://github.com/CMCLauncher-Dev/CMCLauncher/releases"><img alt="Latest release" src="https://img.shields.io/github/v/release/CMCLauncher-Dev/CMCLauncher?style=flat-square&amp;color=2088ff" /></a>
<a href="https://github.com/CMCLauncher-Dev/CMCLauncher/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/CMCLauncher-Dev/CMCLauncher/total?style=flat-square&amp;color=35b879" /></a>
<a href="https://github.com/CMCLauncher-Dev/CMCLauncher/actions/workflows/ci.yml"><img alt="CI" src="https://img.shields.io/github/actions/workflow/status/CMCLauncher-Dev/CMCLauncher/ci.yml?branch=main&amp;style=flat-square&amp;label=build" /></a>
<a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/license-AGPL--3.0-7654c4?style=flat-square" /></a>
<br />
<img alt="Windows x64" src="https://img.shields.io/badge/Windows-x64-0078d4?style=flat-square" />
<img alt=".NET 8" src="https://img.shields.io/badge/.NET-8-512bd4?style=flat-square" />
<img alt="WinUI 3" src="https://img.shields.io/badge/WinUI-3-00a4ef?style=flat-square" />
<a href="https://github.com/CMCLauncher-Dev/CMCLauncher/stargazers"><img alt="GitHub stars" src="https://img.shields.io/github/stars/CMCLauncher-Dev/CMCLauncher?style=flat-square" /></a>
</p>

### [Download](https://github.com/CMCLauncher-Dev/CMCLauncher/releases) · [Website](https://cmcl.ctfun.top/) · [Report an issue](https://github.com/CMCLauncher-Dev/CMCLauncher/issues)

</div>

A native Windows launcher for Minecraft Java Edition, built with C#, .NET 8 and WinUI 3.

English, Simplified Chinese, Japanese, Russian, French, Spanish and Arabic interface translations are included. Choose automatic or manual language selection in **Settings → General → Language**.

## Platform support

| Platform | Interface | Compatibility | Updates & maintenance | Supported systems / hardware |
| --- | --- | --- | --- | --- |
| Windows | WinUI 3 | ✅ Supported | Actively maintained | Windows 10 (2004+) / 11, x64 |
| Linux | Avalonia | ⚠️ Experimental | Experimental updates until stable | Debian-based, Arch-based and Red Hat-based distributions, x64 |
| macOS | Avalonia | ⚠️ Experimental; **may have serious issues** | Low-priority maintenance | Intel (x64) and Apple M-series (ARM64) |

Linux and macOS builds are for testing. macOS builds are **not signed or notarized**. Back up your instances before testing.

See the [Linux package guide](docs/LINUX_PACKAGES.md) and [Avalonia frontend notes](CMCLauncher.Avalonia/README.md) for platform requirements and known limitations.

## Preview

<div align="center">
<img src="docs/assets/launcher-home.png" alt="CMCLauncher home" width="960" />
</div>

## Ready for your next world

<table>
<tr>
<td width="33%" valign="top"><h3>🧩 Manage your instances</h3><p>Separate game directories, local imports, and support for PCL / HMCL instance formats.</p></td>
<td width="33%" valign="top"><h3>📦 Modpacks and loaders</h3><p>Fabric, Forge, NeoForge, Modrinth modpacks and supported ZIP imports.</p></td>
<td width="33%" valign="top"><h3>🔎 Discover community content</h3><p>Find mods, resource packs, shaders and more on Modrinth / CurseForge.</p></td>
</tr>
<tr>
<td width="33%" valign="top"><h3>🎨 Make it your own</h3><p>Light and dark themes, custom colors and fonts, and seven interface languages.</p></td>
<td width="33%" valign="top"><h3>📈 Monitor your system</h3><p>Live CPU, memory and GPU charts, with automatic Java memory allocation.</p></td>
<td width="33%" valign="top"><h3>🔄 Updates and connections</h3><p>Verified launcher updates, an optional Beta channel, and Frp Client version and connection management.</p></td>
</tr>
</table>

## Quick start

### Requirements

- Windows 10 version 2004 (build 19041) or later, x64; Linux; or macOS.
- Sign in with a Microsoft account that has a Minecraft Java Edition profile. To use an offline account, first sign in with at least one Microsoft account that owns the game.

### Getting started

1. Download `CMCLauncher-1.0.5-win-x64.zip` from [Releases](https://github.com/CMCLauncher-Dev/CMCLauncher/releases), extract the entire archive, and run `CMCLauncher.exe`.
2. Open **Settings → Java** to scan or import an existing Java installation, or download Temurin Java.
3. Open **Settings → Accounts** and choose Microsoft device-code sign-in. Complete authorization in your browser, then wait for the launcher to verify your Minecraft Java profile.
4. Download a game version, import an existing `.minecraft` folder, or import a modpack. Select a version and account on the home page, then launch.

External sign-in (authlib-injector) is available under **Settings → Accounts** after a Microsoft account passes Minecraft Java Edition profile verification.

Release archives include .NET and the Windows App SDK runtime, so you do not need to install .NET separately.

## Features

| Area | Supported features |
| --- | --- |
| Games and instances | Browse releases, snapshots and April Fools versions; create isolated instances; import local game folders |
| Loaders | Find and install Fabric, Forge and NeoForge; identify loaders used by imported versions |
| Modpacks | Import Modrinth `.mrpack` files, ZIP archives with supported manifests, and MultiMC / Prism ZIP exports; drag-and-drop import |
| Community content | Choose Modrinth, CurseForge or both; browse paginated results, view project versions, and check game, loader and target-instance compatibility |
| Content management | View mod metadata and icons; import, disable or remove content; manage data packs per world |
| Schematics | Search, import, export, rename and delete `.litematic`, `.schematic`, `.schem` and `.nbt` files |
| Java and memory | Scan, import and download Temurin Java; match Java versions automatically; use automatic or manual heap allocation |
| Frp Client | Download official Windows x64 releases, validate INI/TOML configurations, and start or stop the client |
| Appearance and languages | Light, dark and system themes; colors, fonts, scaling and appearance presets; launcher and game language settings |
| Accounts and diagnostics | Microsoft device-code sign-in, external authlib-injector sign-in, Windows DPAPI credential encryption, runtime logs and game error-report export |

Interface translations load from editable JSON files in `locales/` beside the executable. Additional valid language packs appear in the language selector. See [language packs](docs/LOCALIZATION.md) for the schema, reload behavior and translation guide.

## Support and limitations

### Frp Client

Open **More → Frp Client**, choose a version from the online release list, and select **Download**. **Refresh releases** updates the list; **Load more releases** retrieves older versions. Downloads come from [official frp releases](https://github.com/fatedier/frp/releases) and must pass official SHA-256 verification. The client is downloaded on demand and is not bundled with the launcher.

## Data and logs

| Location | Contents |
| --- | --- |
| `%APPDATA%/CMCLauncher.Native/settings.json` | Launcher settings, instance records and encrypted account credentials |
| `%APPDATA%/CMCLauncher.Native/library/` | Default instances and libraries; a different game root can be selected in the launcher |
| `%APPDATA%/CMCLauncher.Native/runtimes/` | Java installations downloaded by the launcher |
| `logs/launcher-yyyyMMdd.log` beside the executable | Launcher runtime logs |
| `logs/` and `crash-reports/` within each instance | Game logs and crash reports |

## Run from source

On Windows, install the .NET 8 SDK and the Windows SDK / development tools required to build WinUI 3 projects. The project targets `net8.0-windows10.0.19041.0`, x64. Clone the repository, then run these commands from its root:

```powershell
git clone https://github.com/CMCLauncher-Dev/CMCLauncher.git
cd CMCLauncher
dotnet run --project CMCLauncher.WinUI/CMCLauncher.WinUI.csproj -p:Platform=x64
```

For a one-click local build, double-click `build-and-run.cmd` in the repository root. It publishes a self-contained Release build to `.artifacts/local-runs/`.

To build without opening the application, run `./build-and-run.ps1 -NoLaunch` in PowerShell.

For local CurseForge testing, run `build-and-run.cmd "-curseforgekey=YOUR_KEY"` in Command Prompt.

### Configure Microsoft sign-in

Source builds without a client ID can run the interface, but Microsoft sign-in is unavailable. To enable it locally, create `.config/clientid.local.json` with your configured public OAuth client ID:

```json
{
  "clientId": "YOUR_MICROSOFT_CLIENT_ID"
}
```

### Build and package

Build the Release configuration:

```powershell
dotnet build CMCLauncher.WinUI/CMCLauncher.WinUI.csproj -c Release -p:Platform=x64
```

Publish a self-contained directory and create a ZIP archive:

```powershell
dotnet publish CMCLauncher.WinUI/CMCLauncher.WinUI.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o dist/cmc-launcher
./scripts/package-release.ps1 -PublishDirectory dist/cmc-launcher -ArchivePath dist/CMCLauncher-1.0.5-win-x64.zip
```

### Tests

Run the native regression tests:

```powershell
dotnet run --project CMCLauncher.Tests/Windows/CMCLauncher.Tests.csproj -c Release
```

These tests use Windows DPAPI and must run on Windows. The final local Java detection check also requires `java` on PATH.

After publishing the application directory, run the WinUI integration check:

```powershell
& ./dist/cmc-launcher/CMCLauncher.exe --verify-ui "$PWD/.artifacts/winui-check"
```

This check uses an isolated data directory and produces screenshots and `passed.txt`. Failures are recorded in `failure.txt`.

## Source layout

| Directory | Contents |
| --- | --- |
| [`CMCLauncher.WinUI/`](CMCLauncher.WinUI/) | Desktop entry point, windows, pages, controls and UI services |
| [`CMCLauncher.Core/`](CMCLauncher.Core/) | Downloads, authentication protocols, resource browsing, storage and platform-independent services |
| [`CMCLauncher.Platform/Windows/`](CMCLauncher.Platform/Windows/) | Windows credentials, memory and region queries, registry integration and update installation |
| [`CMCLauncher.Tests/Core/`](CMCLauncher.Tests/Core/) | Portable core regression checks |
| [`CMCLauncher.Tests/Windows/`](CMCLauncher.Tests/Windows/) | Native regression tests and certificate fixtures |
| [`scripts/`](scripts/) | Publishing, resource maintenance and packaging scripts |
| [`docs/`](docs/) | Project website source and screenshot assets |
| [`.github/workflows/`](.github/workflows/) | Automated tests, builds and tagged releases |

## Feedback and contributions

Report issues through [Issues](https://github.com/CMCLauncher-Dev/CMCLauncher/issues). Include the launcher version, Windows version, Minecraft and loader versions, and steps to reproduce. For launch failures, attach an error report or relevant logs. Check for account information and local paths before sharing.

Before submitting a pull request, run the tests relevant to your changes. Include screenshots for layout changes, and check cancellation and failure cleanup when modifying downloads, installation or imports.

## License

CMCLauncher is released under the [GNU Affero General Public License v3.0](LICENSE). Third-party dependencies retain their own licenses.

Minecraft, mods and modpacks belong to their respective owners. CMCLauncher is not affiliated with Mojang Studios, Microsoft or NetEase.
