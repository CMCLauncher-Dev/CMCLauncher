"""Build DEB, RPM and AppImage distributions from a verified linux-x64 publish."""

import argparse
import hashlib
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import urllib.request

REPO = Path(__file__).resolve().parent.parent
APP = "CMCLauncher.Avalonia"
TOOL_URL = "https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage"
TOOL_SHA256 = "ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0"
RUNTIME_URL = "https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-x86_64"
RUNTIME_SHA256 = "2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d"


def run(*args, **kwargs):
    subprocess.run([str(arg) for arg in args], check=True, **kwargs)


def write(path, text, executable=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8", newline="\n")
    path.chmod(0o755 if executable else 0o644)


def payload(publish, destination, distribution):
    shutil.copytree(publish, destination)
    (destination / APP).chmod(0o755)
    write(destination / "cmclauncher-distribution", distribution + "\n")
    for name in ("LICENSE", "README.md"):
        shutil.copy2(REPO / name, destination / name)


def desktop_files(root):
    target = root / "usr/share/applications/cmclauncher.desktop"
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(REPO / "packaging/linux/cmclauncher.desktop", target)
    icon = root / "usr/share/icons/hicolor/1024x1024/apps/cmclauncher.png"
    icon.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(REPO / "CMCLauncher.WinUI/assets/cmc-logo.png", icon)


def native_tree(publish, root):
    payload(publish, root / "opt/cmclauncher", "package")
    desktop_files(root)
    write(root / "usr/bin/cmclauncher", '#!/bin/sh\nexec /opt/cmclauncher/CMCLauncher.Avalonia "$@"\n', True)


def deb(publish, work, output, version):
    root = work / "deb"
    native_tree(publish, root)
    size = sum(file.stat().st_size for file in root.rglob("*") if file.is_file())
    write(root / "DEBIAN/control", f"""Package: cmclauncher
Version: {version.replace('-', '~', 1)}
Architecture: amd64
Maintainer: com-in <com-in@users.noreply.github.com>
Section: games
Priority: optional
Homepage: https://github.com/com-in/CMCLauncher
Installed-Size: {(size + 1023) // 1024}
Depends: libc6 (>= 2.34), libgcc-s1, libstdc++6, zlib1g, libicu70 | libicu72 | libicu74 | libicu76 | libicu77 | libicu78, libssl3 | libssl3t64, libfontconfig1, libx11-6, libxrandr2, libxi6, libxcursor1, libxrender1, libice6, libsm6
Recommends: libsecret-tools, libglib2.0-bin
Description: Minecraft launcher
 Manage Minecraft instances, mods, worlds and downloads.
""")
    path = output / "CMCLauncher-Avalonia-linux-x64.deb"
    run("dpkg-deb", "--root-owner-group", "--build", root, path)
    run("dpkg-deb", "--info", path)


def rpm(publish, work, output, version):
    root = work / "rpm-root"
    native_tree(publish, root)
    top = work / "rpmbuild"
    for name in ("BUILD", "RPMS", "SOURCES", "SPECS", "SRPMS", "BUILDROOT"):
        (top / name).mkdir(parents=True)
    base, _, pre = version.partition("-")
    release = "0." + pre.replace("-", ".") if pre else "1"
    spec = top / "SPECS/cmclauncher.spec"
    write(spec, f"""Name: cmclauncher
Version: {base}
Release: {release}
Summary: Minecraft launcher
License: GPL-3.0-only
URL: https://github.com/com-in/CMCLauncher
BuildArch: x86_64
AutoReqProv: no
Requires: glibc >= 2.34, libgcc, libstdc++, zlib, libicu, openssl-libs, fontconfig, libX11, libXrandr, libXi, libXcursor, libXrender, libICE, libSM
Recommends: libsecret, glib2
%description
Manage Minecraft instances, mods, worlds and downloads.
%prep
%build
%install
mkdir -p %{{buildroot}}
cp -a {root}/. %{{buildroot}}/
%files
%defattr(-,root,root,-)
/opt/cmclauncher
/usr/bin/cmclauncher
/usr/share/applications/cmclauncher.desktop
/usr/share/icons/hicolor/1024x1024/apps/cmclauncher.png
""")
    # .NET bundles are already built; stripping/debug extraction can damage their metadata.
    run("rpmbuild", "-bb", spec, "--define", f"_topdir {top}", "--define", "__os_install_post %{nil}", "--define", "debug_package %{nil}")
    candidates = list((top / "RPMS").rglob("*.rpm"))
    if len(candidates) != 1:
        raise RuntimeError("Expected one RPM package.")
    path = output / "CMCLauncher-Avalonia-linux-x64.rpm"
    shutil.copy2(candidates[0], path)
    run("rpm", "-qip", path)


def bundle_libraries(appdir):
    """Resolve native dependencies on the Ubuntu 22.04 builder; never bundle glibc."""
    library_root = appdir / "usr/lib"
    system = subprocess.check_output(["ldconfig", "-p"], text=True)
    extras = ["libssl.so.3", "libcrypto.so.3", "libicuuc.so.70", "libicui18n.so.70", "libicudata.so.70"]
    # The optional .NET LTTng provider targets an older tracing ABI, not normal app startup.
    queue = [path for path in (library_root / "cmclauncher").glob("*.so") if path.name != "libcoreclrtraceptprovider.so"] + [library_root / "cmclauncher" / APP]
    for name in extras:
        match = re.search(r"^\s*" + re.escape(name) + r"\s.*=>\s+(\S+)", system, re.MULTILINE)
        if not match:
            raise RuntimeError("Missing AppImage build dependency: " + name)
        queue.append(Path(match[1]))
    visited = set()
    licenses = set()
    excluded = re.compile(r"^(ld-linux|lib(c|m|mvec|pthread|dl|rt|util|anl|resolv|nss_[^.]+|thread_db|BrokenLocale)\.so)")
    while queue:
        path = queue.pop()
        if path in visited:
            continue
        visited.add(path)
        text = subprocess.check_output(["ldd", str(path)], text=True, stderr=subprocess.STDOUT)
        if "not found" in text:
            raise RuntimeError("Unresolved AppImage library dependency.")
        for value in re.findall(r"(?:=>\s+|^\s*)(/\S+)\s+\(", text, re.MULTILINE):
            dependency = Path(value)
            if not excluded.match(dependency.name):
                target = library_root / dependency.name
                if not target.exists():
                    shutil.copyfile(dependency, target)
                queue.append(dependency)
        if path.is_absolute() and appdir not in path.parents and not excluded.match(path.name):
            target = library_root / path.name
            if not target.exists():
                shutil.copyfile(path, target)
        for dependency in (path,):
            if appdir in dependency.parents:
                continue
            owner = subprocess.run(["dpkg-query", "-S", str(dependency)], capture_output=True, text=True)
            if owner.returncode != 0:
                owner = subprocess.run(["dpkg-query", "-S", str(dependency.resolve())], capture_output=True, text=True)
            if owner.returncode == 0:
                name = owner.stdout.split(": ", 1)[0].split(":", 1)[0]
                copyright_file = Path("/usr/share/doc") / name / "copyright"
                if name not in licenses and copyright_file.is_file():
                    destination = appdir / "usr/share/licenses/cmclauncher/native" / (name + ".copyright")
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(copyright_file, destination)
                    licenses.add(name)


def appimage(publish, work, output):
    appdir = work / "CMCLauncher.AppDir"
    payload(publish, appdir / "usr/lib/cmclauncher", "appimage")
    desktop_files(appdir)
    shutil.copy2(REPO / "packaging/linux/AppRun", appdir / "AppRun")
    (appdir / "AppRun").chmod(0o755)
    shutil.copy2(appdir / "usr/share/applications/cmclauncher.desktop", appdir / "cmclauncher.desktop")
    shutil.copy2(REPO / "CMCLauncher.WinUI/assets/cmc-logo.png", appdir / "cmclauncher.png")
    shutil.copy2(appdir / "cmclauncher.png", appdir / ".DirIcon")
    write(appdir / "usr/bin/cmclauncher", '#!/bin/sh\nexec "$(dirname "$0")/../lib/cmclauncher/CMCLauncher.Avalonia" "$@"\n', True)
    bundle_libraries(appdir)
    tool = work / "appimagetool.AppImage"
    with urllib.request.urlopen(TOOL_URL, timeout=60) as response, tool.open("wb") as stream:
        shutil.copyfileobj(response, stream)
    if hashlib.sha256(tool.read_bytes()).hexdigest() != TOOL_SHA256:
        raise RuntimeError("appimagetool checksum mismatch.")
    tool.chmod(0o755)
    runtime = work / "runtime-x86_64"
    with urllib.request.urlopen(RUNTIME_URL, timeout=60) as response, runtime.open("wb") as stream:
        shutil.copyfileobj(response, stream)
    if hashlib.sha256(runtime.read_bytes()).hexdigest() != RUNTIME_SHA256:
        raise RuntimeError("AppImage runtime checksum mismatch.")
    path = output / "CMCLauncher-Avalonia-linux-x64.AppImage"
    env = dict(os.environ, ARCH="x86_64", APPIMAGE_EXTRACT_AND_RUN="1", SOURCE_DATE_EPOCH=os.environ.get("SOURCE_DATE_EPOCH", "0"))
    run(tool, "--no-appstream", "--runtime-file", runtime, appdir, path, env=env)
    path.chmod(0o755)
    run(path, "--appimage-version")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publish", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-(?:alpha|beta|rc)[0-9.-]*)?", args.version):
        parser.error("Invalid package version.")
    publish = args.publish.resolve(strict=True)
    output = args.output.resolve()
    if publish == output or publish in output.parents:
        parser.error("Output must be outside the publish directory.")
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="cmcl-linux-") as directory:
        work = Path(directory)
        # Reuse the portable package validation before copying anything into native packages.
        run(os.sys.executable, REPO / "scripts/package-desktop.py", "--publish", publish, "--output", work / "validated.tar.gz", "--rid", "linux-x64")
        deb(publish, work, output, args.version)
        rpm(publish, work, output, args.version)
        appimage(publish, work, output)
