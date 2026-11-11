"""Package the Linux/macOS frontend without losing its executable permission."""

import argparse
import os
from pathlib import Path
import tarfile


def package(publish: Path, output: Path, rid: str) -> None:
    publish = publish.resolve(strict=True)
    output = output.resolve()
    if publish == output or publish in output.parents:
        raise ValueError("The archive must be outside the publish directory.")
    required = ("CMCLauncher.Avalonia", "CMCLauncher.Avalonia.dll", "CMCLauncher.Core.dll")
    if not all((publish / name).is_file() for name in required):
        raise ValueError("Desktop runtime files are missing.")
    with (publish / required[0]).open("rb") as stream:
        magic = stream.read(4)
    allowed = {b"\x7fELF"} if rid == "linux-x64" else {b"\xcf\xfa\xed\xfe", b"\xca\xfe\xba\xbe"}
    if magic not in allowed:
        raise ValueError("The app host does not match the target platform.")
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
    parser.add_argument("--rid", choices=("linux-x64", "osx-arm64"), required=True)
    options = parser.parse_args()
    package(options.publish, options.output, options.rid)
