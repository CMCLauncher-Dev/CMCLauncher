# CMCLauncher 1.0.6

This stable release adds Java distribution management, Options backups, and a graphical data pack editor, and brings more of the WinUI experience to Avalonia.

## New features

- **Java management on Windows:** Choose the Java major version, distribution, and installation location. Windows x64 supports Eclipse Temurin, Amazon Corretto, Microsoft Build of OpenJDK, Azul Zulu, BellSoft Liberica, and SAP SapMachine. The distribution picker lists the brands available for the selected major version.
- **Java updates and file management:** Each entry has compact buttons for updating, moving, removing the entry, and deleting the installation folder. Launcher-managed installations update in place. Supported imported installations can switch to a separately installed update while keeping their original files. Moving an installation preserves its outer folder and internal layout; deleting it requires confirming the exact path.
- **Options backups on both frontends:** Back up existing Minecraft settings before launch, including resource pack selection, key bindings, and graphics settings, along with OptiFine settings when present. Set a retention limit, create a backup on demand, and restore or delete individual backups. After a resource reload failure, the launcher offers recovery and prefers a backup verified by a normal game exit.
- **Data pack editor on both frontends:** Select a Minecraft version to get the matching data pack format. The wizard provides templates, graphical entry editing, source editing, namespace and JSON validation, a file preview, and export.
- **Instance diagnostics:** View the saved multiplayer servers for an instance, enable per-instance debug mode, and inspect game output. Crash reports surface clues such as out-of-memory errors, Java incompatibilities, Mixin failures, and missing dependencies.
- **Mod dependencies and translation:** Check for missing dependencies and version conflicts before launch. Read mod language files and generate translation resource packs. Mod list names prefer local translations and use the configured translation service as a fallback.
- **WinUI personalization:** Use transparent glass or a Windows 7 Aero-style effect, adjust opacity with a live slider, and control card outlines from a collapsible settings section. Set an image or video wallpaper, with compact background controls and a separate file label.
- **System monitoring:** Both frontends show disk activity and live network throughput, with improved CPU and GPU sampling. Linux adds DRM GPU metrics and a fallback for unsupported drivers.
- **Languages and support:** Added Classical Chinese, LOLSpeak, and Back Slang; expanded UI and launcher log translations. The About page links to the project repository and issue tracker.

## Fixes and improvements

- A game exit handled by the Windows launcher no longer hides and restarts the launcher. The independent watchdog still recovers a closed or unresponsive launcher. Crash logging and cancellation feedback also improved.
- Restoring or maximizing the window now saves the startup mode while preserving its last windowed size and position.
- Java selection can use compatible newer runtimes for games that request an older major version. Affected Java4Shell runtimes are flagged with a high-contrast warning and receive the existing launch-time mitigation.
- Moving Java into an empty folder with the same name no longer creates a duplicate folder level. Existing installations are not overwritten.
- Fabric dependency metadata can specify multiple alternative version ranges without crashing the pre-launch check. Any matching range is accepted; a conflict is still reported if none match.
- Fixed HTML tags appearing in search summaries, incorrect mod name translations, and mod translation behavior in novelty languages. Shader filters now follow the download source, with aligned headings and more consistent controls.
- The 32-DIP custom window buttons sit flush with the right edge. The glass title bar keeps a minimal hit-test surface so clicks do not pass through to windows behind it. Fixed rounded corners, window edge coverage, and wallpaper behind the title bar. Glass settings saved as enabled receive one startup off/on cycle.
- Fixed shrinking sidebar labels, confirmation flyout corners and width, the toast timer, and cancellation being shown as failure.
- Fixed a settings save race that could prevent Avalonia from rebuilding navigation and the current page after changing language.
- Removed fixed file size caps from shared downloads, blueprint downloads, and launcher update packages. Java packages are verified against their published checksums. CMS blueprint downloads stream to disk and invalid responses no longer leave an empty install folder. Integrity checks and extraction safeguards remain.
- Bundled language packs and themes now live under their respective Default directories and update with the launcher. User overrides take precedence, and unmodified legacy copies are cleaned up. ZIP, single-file, and desktop update packaging follow the new layout.

## Platforms and maintenance

- Linux and macOS Avalonia continue to align with WinUI across resource filters, FRP draft handling and live logs, direct Terracotta downloads, MinecraftPFP, Java management, file actions, system status, and themed controls. Removed obsolete Avalonia UI code.
- Linux CI adds platform integration checks, CJK and Arabic rendering coverage, Fedora 45 RPM installation coverage, and diagnostic artifacts from installed-app UI checks. Added regression coverage for data packs, Options recovery, release packaging, and window behavior.
- Updated the Chinese, English, and Japanese READMEs, localization documentation, and issue templates. Removed Visual Studio cache files from the repository.
- **Windows x64** is actively maintained. **Linux x64** remains experimental and is available as DEB, RPM, AppImage, or TAR.GZ. **macOS Intel and Apple silicon** builds are early-stage, have limited testing, and are not signed or notarized.

## Downloads

- **Windows:** Extract the ZIP before launching, or use the single-file EXE.
- **Linux:** Choose the appropriate DEB or RPM, or use the AppImage or TAR.GZ.
- **macOS:** Choose the Intel (x64) or Apple silicon (ARM64) TAR.GZ.
- If an older launcher asks you to update manually, download the appropriate package from this release. Packages include .NET; Java and game files are downloaded or imported separately.

[Full comparison from 1.0.5 to 1.0.6](https://github.com/CMCLauncher-Dev/CMCLauncher/compare/CMCL-v1.0.5-release...CMCL-v1.0.6-release)
