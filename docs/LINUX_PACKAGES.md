# Linux packages

Linux uses the Avalonia frontend. CI produces these x86-64 builds:

| Format | Intended systems | Install or run |
| --- | --- | --- |
| DEB | Ubuntu 22.04/24.04, Debian 12 | `sudo apt install ./CMCLauncher-Avalonia-linux-x64.deb` |
| RPM | Fedora, Rocky Linux 9 and compatible RHEL-family systems | `sudo dnf install ./CMCLauncher-Avalonia-linux-x64.rpm` |
| AppImage | glibc-based desktop distributions with glibc 2.34 or later | `chmod +x CMCLauncher-Avalonia-linux-x64.AppImage` then run it |
| TAR.GZ | Portable directory installation | Extract it, then run `CMCLauncher.Avalonia` |

Package installation adds `cmclauncher` to PATH and installs a desktop-menu entry and icon. DEB/RPM contain the self-contained .NET application and declare native runtime dependencies. They do not install a separate .NET runtime. Alpine/musl and Linux ARM64 are not targets of these packages.

AppImage is assembled on Ubuntu 22.04. Its AppDir contains the launcher, bundled native dependencies, their available copyright notices, a desktop entry and an icon. It leaves glibc to the host. Both appimagetool 1.9.1 and the type-2 runtime release 20251108 are checked against pinned SHA-256 hashes. If FUSE is unavailable, use:

```sh
APPIMAGE_EXTRACT_AND_RUN=1 ./CMCLauncher-Avalonia-linux-x64.AppImage
```

Installed applications and mounted AppImages are read-only. frpc configurations are stored under the launcher's user-data directory. The first-run wizard offers that directory instead of attempting to create `.minecraft` inside the installed application. Updates for these distributions open the release page: install the replacement DEB/RPM with the package manager, or replace the AppImage. The portable TAR.GZ installation retains its staged in-place updater. Removing a package leaves user data intact.

To build the three distribution formats on Ubuntu 22.04:

```sh
sudo apt install rpm desktop-file-utils squashfs-tools libicu70 libssl3 libfontconfig1 libx11-6 libxrandr2 libxi6 libxcursor1 libxrender1 libice6 libsm6
python3 scripts/package-linux.py --publish .artifacts/avalonia-linux --output .artifacts/linux-packages --version 1.0.3
```

Start with a `linux-x64` self-contained publish directory. Packaging reuses the TAR publisher's validation before generating native packages. CI runs an AppImage UI check without FUSE, then installs, launches and removes native packages in Ubuntu 22.04, Ubuntu 24.04, Debian 12, Fedora 44 and Rocky Linux 9 containers. These checks gate tagged releases. Workflow filenames stay unversioned; release assets receive the release version.

Local Windows verification covers package-layout/metadata tests, Core regression tests, workflow linting and cross-compilation. Actual DEB/RPM installation and AppImage execution require the Linux CI jobs; cross-compilation alone does not establish distribution compatibility.
