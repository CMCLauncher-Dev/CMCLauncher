"""Package the Linux/macOS frontend without losing its executable permission."""

import argparse
import os
import struct
from pathlib import Path
import tarfile


def validate_apphost(path: Path, rid: str) -> None:
    with path.open("rb") as stream:
        header = stream.read(8)
        if rid == "linux-x64":
            if header[:4] != b"\x7fELF":
                raise ValueError("The app host does not match the target platform.")
            return
        expected = {"osx-arm64": 0x0100000C, "osx-x64": 0x01000007}[rid]
        if len(header) != 8:
            raise ValueError("The Mach-O app host header is incomplete.")
        if header[:4] == b"\xcf\xfa\xed\xfe":
            architectures = [struct.unpack("<I", header[4:8])[0]]
        elif header[:4] == b"\xca\xfe\xba\xbe":
            count = struct.unpack(">I", header[4:8])[0]
            if not 0 < count <= 32:
                raise ValueError("Invalid universal Mach-O architecture table.")
            records = stream.read(count * 20)
            if len(records) != count * 20:
                raise ValueError("Incomplete universal Mach-O architecture table.")
            architectures = [struct.unpack_from(">I", records, index * 20)[0] for index in range(count)]
        else:
            raise ValueError("The app host does not match the target platform.")
        if expected not in architectures:
            raise ValueError("The Mach-O app host architecture does not match the requested RID.")


def package(publish: Path, output: Path, rid: str) -> None:
    publish = publish.resolve(strict=True)
    output = output.resolve()
    if publish == output or publish in output.parents:
        raise ValueError("The archive must be outside the publish directory.")
    required = ("CMCLauncher.Avalonia", "CMCLauncher.Avalonia.dll", "CMCLauncher.Core.dll")
    if not all((publish / name).is_file() for name in required):
        raise ValueError("Desktop runtime files are missing.")
    validate_apphost(publish / required[0], rid)
    files = sorted(publish.rglob("*"))
    key = os.environ.get("CURSEFORGE_API_KEY", "").encode()
    for file in files:
        if file.is_symlink():
            raise ValueError("Publish output contains a symbolic link.")
        if not file.is_file():
            continue
        if file.name.lower() in {"clientid.local.json", "curseforge.local.json", "settings.json", "credentials.json", "secrets.json"} or file.suffix.lower() in {".pfx", ".p12", ".key"}:
            raise ValueError("Publish output contains a local configuration or secret file.")
        # The developer credential is approved only inside compiled assemblies.
        if key and file.suffix.lower() not in {".dll", ".exe"} and key in file.read_bytes():
            raise ValueError("A credential was found outside compiled assemblies.")
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(output.name + ".partial")
    repo = Path(__file__).resolve().parent.parent
    try:
        with tarfile.open(temporary, "w:gz") as archive:
            for file in files:
                if not file.is_file():
                    continue
                relative = file.relative_to(publish).as_posix()
                if relative in {"README.md", "LICENSE"}:
                    continue
                info = archive.gettarinfo(str(file), arcname=relative)
                info.uid = info.gid = 0
                info.uname = info.gname = ""
                if relative == required[0]:
                    info.mode |= 0o111
                with file.open("rb") as stream:
                    archive.addfile(info, stream)
            for name in ("README.md", "LICENSE"):
                archive.add(repo / name, arcname=name, recursive=False)
        with tarfile.open(temporary, "r:gz") as archive:
            names = archive.getnames()
            if len(names) != len(set(names)) or not archive.getmember(required[0]).mode & 0o111:
                raise ValueError("The desktop archive failed verification.")
        temporary.replace(output)
    finally:
        temporary.unlink(missing_ok=True)
    print(f"Desktop archive verified: {rid}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publish", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--rid", choices=("linux-x64", "osx-arm64", "osx-x64"), required=True)
    options = parser.parse_args()
    package(options.publish, options.output, options.rid)
