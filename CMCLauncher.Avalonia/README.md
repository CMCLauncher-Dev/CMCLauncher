# Avalonia desktop frontend

.NET 8 / Avalonia 11 frontend for Linux and macOS. Windows uses CMCLauncher.WinUI. Core services and host contracts are shared.

Run from the repository root:

```sh
dotnet run --project CMCLauncher.Avalonia/CMCLauncher.Avalonia.csproj
```

Publish with RID `linux-x64` or `osx-arm64` (cross-publishing from Windows is supported):

```sh
dotnet publish CMCLauncher.Avalonia/CMCLauncher.Avalonia.csproj -c Release -r linux-x64 --self-contained true -o .artifacts/avalonia-linux
```

Pages cover instances and directories, game installation, community resources/maps/blueprints, content, mod updates, world backups, modpack export, accounts, Java, appearance, network, frpc, portraits, logs, tasks and status. Shared Core services handle installation, authentication, caching and archive safety. CMS retains its global request gate and three-second access notice.

Views own controls and dialogs; ViewModels own session, task state and export selection. Language and theme JSON are shared with WinUI. Navigation also uses a shared Core catalog. Home cards read and write the same layout model, and installed content/account skins reuse the same caches. See [frontend coverage](../docs/FRONTEND_PARITY.md) for the implemented routes and verification boundary.

Linux credentials require an installed, unlocked Secret Service and `secret-tool`; trash requires `gio`. macOS uses Keychain and Finder trash. Neither falls back to plaintext credentials. Credentials encrypted on another OS require signing in again.

Verification uses an isolated store and rejects unexpected network traffic:

```sh
dotnet run --project CMCLauncher.Avalonia/CMCLauncher.Avalonia.csproj -- --verify-ui .artifacts/avalonia-ui
```

Check `passed.txt` and absence of `failure.txt`. Earlier development checks rendered the pages on Windows; distribution now uses WinUI exclusively on Windows. Linux/macOS packages were cross-published; actual game launching, desktop dialogs and credential services still require those hosts. System telemetry uses platform adapters; GPU data is shown only where available. Updates select a frontend-specific OS/architecture package, verify its SHA-256 digest and stage it before replacement. The separate updater preserves user data and rolls back copied files on failure; unavailable platform assets open the release page instead.

Linux distribution formats and read-only installation behavior are documented in [Linux packages](../docs/LINUX_PACKAGES.md).
