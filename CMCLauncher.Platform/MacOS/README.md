macOS adapter: Keychain credentials, system memory and CPU, standard/Homebrew JDK discovery and Finder trash.

CI runs macOS platform checks on Apple Silicon and Intel runners before publishing the matching TAR archive. The checks exercise native memory/CPU probes and installed JDK inspection without modifying the user's Keychain.

Game rendering, Finder file dialogs, Keychain authorization prompts and Gatekeeper behavior still require an interactive Mac. No Developer ID signing or notarization is configured. GPU counters are unavailable when no supported interface exists.
