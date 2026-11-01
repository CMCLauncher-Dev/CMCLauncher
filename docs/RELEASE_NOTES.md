# CMCLauncher 1.0.2-beta

This is a prerelease for testing the recent interface, monitoring, and launch changes.

## Highlights

- Refresh the About page with launcher identity, build information, update controls, and contributors. Unify the displayed release version with update comparisons.
- Expand system status with responsive half-width CPU, memory, and per-GPU area charts. Cache graphics adapter information and offer continuous sampling after leaving the page.
- Improve account management layout, show external sign-in directly, and add a LittleSkin server preset.
- Refine community filter spacing and alignment, and add deletion of downloaded Frp Client versions with confirmation.
- Improve README presentation with the project logo, badges, feature cards, and English, Chinese, and Japanese documentation.
- Add a default-on high-performance GPU preference for the selected game Java executable, prefer javaw.exe when available, and retain previous Windows graphics preferences for restoration.

## GPU preference limitation

The graphics option requests a Windows preference; it does not force hardware selection. Disabling it restores recorded previous settings and may still select a discrete GPU. Check the Minecraft F3 renderer or the OpenGL renderer line in the game log to confirm the GPU used. Game-level GPU selection across all hybrid systems is not verified.

## Downloads

- `CMCLauncher-1.0.2-beta-win-x64.zip`: extract the complete archive and run `CMCLauncher.exe`.
- `CMCLauncher-1.0.2-beta-win-x64.exe`: standalone self-extracting launcher.

The updater offers this version when **Receive Beta releases** is enabled. Java, game files, and Frp Client are downloaded separately.
