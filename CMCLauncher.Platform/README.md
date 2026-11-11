# Platform integration

Abstractions targets net8.0 with no Core or UI dependencies. Core references these contracts. Windows contains DPAPI, registry, recycle-bin, restart-monitor and updater integration. Linux supplies Secret Service credentials, /proc memory, JDK discovery and gio trash. macOS supplies Keychain credentials, system memory, JDK discovery and Finder trash.

Hosts initialize LauncherPlatform at startup. File pickers remain frontend-owned. Contracts expose no WinUI, WinRT or Core model types. Avalonia uses a separate game-exit watchdog, with PID start-time checks, to recover even after its UI hangs.
