# Terracotta in the toolbox

Open **More → Toolbox → Terracotta**. CMCLauncher does not bundle Terracotta or download it in the background. Click **Download Terracotta** to fetch the latest official release for your operating system and CPU. The download must have the size and SHA-256 digest published by GitHub. A failed or cancelled update leaves the previous component available.

Before downloading, only the download controls are shown. After downloading, the multiplayer lobby offers **I want to host** and **I want to join**. The host entry immediately starts the component and scans for a LAN world. The join entry opens an invitation-code dialog; cancelling it makes no network request. Confirming a valid code starts the connection. Leaving a room returns to the lobby and hides the room actions. The host must open a Minecraft world to LAN. Guests use the server address shown after connecting. Players need compatible game and mod versions. Room connectivity depends on the network and Terracotta's relay infrastructure.

On macOS, click **Install macOS application** after downloading, complete the official installer, and allow local-network access when macOS asks. The launcher does not silently elevate privileges or bypass system permissions.

Downloaded files are stored under `tools/terracotta/<os>-<architecture>/` in CMCLauncher's data directory. Files are checked again before execution. **Settings → Remove Terracotta** stops and removes the managed component after confirmation, returning its page to the download state. It does not uninstall the separate macOS system application. **Leave room** disconnects the current room; **Stop Terracotta** stops the local service. This service can be shared with another launcher. Closing a page or CMCLauncher does not shut down that shared service.

## Integration

Terracotta runs as an independent, unmodified program. CMCLauncher invokes `--hmcl <port-file>`, reads the assigned loopback port and uses the [official HTTP protocol](https://github.com/burningtnt/Terracotta/blob/master/sites/docs/http-api.md). Local requests do not use the download proxy and do not follow redirects. State and member-list revisions are tracked independently. The client polls only after a user starts the component, more slowly when its page is inactive; it does not automatically restart a disconnected service.

The implementation was verified against v0.4.2. Future upstream protocol or packaging changes may require an update. Unsupported packages, missing checksums and incompatible running versions fail with an error instead of executing an unverified download.

Terracotta is © burningtnt, licensed under [AGPL-3.0-or-later](https://github.com/burningtnt/Terracotta/blob/master/LICENSE), with an [integration exception in its README](https://github.com/burningtnt/Terracotta#许可). Its copyright and source link are displayed on the tool page. See the [upstream project](https://github.com/burningtnt/Terracotta) for its source and networking documentation.
