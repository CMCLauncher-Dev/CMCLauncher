"""Verify macOS app-host architecture checks and portable archive contents."""
import importlib.util
from pathlib import Path
import struct
import tarfile
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("desktop_package", Path(__file__).with_name("package-desktop.py"))
packaging = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packaging)


class DesktopPackageTests(unittest.TestCase):
    def test_macos_architecture_and_execute_permission(self):
        for rid, cpu in (("osx-arm64", 0x0100000C), ("osx-x64", 0x01000007)):
            with self.subTest(rid=rid), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                publish = root / "publish"
                publish.mkdir()
                (publish / "CMCLauncher.Avalonia").write_bytes(b"\xcf\xfa\xed\xfe" + struct.pack("<I", cpu) + bytes(24))
                for name in ("CMCLauncher.Avalonia.dll", "CMCLauncher.Core.dll"):
                    (publish / name).write_bytes(b"fixture")
                output = root / "desktop.tar.gz"
                packaging.package(publish, output, rid)
                with tarfile.open(output, "r:gz") as archive:
                    self.assertTrue(archive.getmember("CMCLauncher.Avalonia").mode & 0o111)
                    self.assertIn("LICENSE", archive.getnames())
                wrong = "osx-x64" if rid == "osx-arm64" else "osx-arm64"
                with self.assertRaises(ValueError):
                    packaging.package(publish, root / "wrong.tar.gz", wrong)
                self.assertFalse((root / "wrong.tar.gz").exists())

    def test_invalid_and_universal_headers(self):
        with tempfile.TemporaryDirectory() as directory:
            host = Path(directory) / "apphost"
            host.write_bytes(b"\xcf\xfa\xed\xfe")
            with self.assertRaises(ValueError):
                packaging.validate_apphost(host, "osx-arm64")
            records = b"".join(struct.pack(">IIIII", cpu, 0, 0, 0, 0) for cpu in (0x01000007, 0x0100000C))
            host.write_bytes(b"\xca\xfe\xba\xbe" + struct.pack(">I", 2) + records)
            packaging.validate_apphost(host, "osx-arm64")
            packaging.validate_apphost(host, "osx-x64")
            host.write_bytes(b"\xca\xfe\xba\xbe" + struct.pack(">I", 2))
            with self.assertRaises(ValueError):
                packaging.validate_apphost(host, "osx-arm64")


if __name__ == "__main__":
    unittest.main()
