"""Check Linux package layout and metadata without invoking native packaging tools."""

import importlib.util
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("linux_packaging", Path(__file__).with_name("package-linux.py"))
packaging = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packaging)


class LinuxPackageTests(unittest.TestCase):
    def test_layout_and_prerelease_metadata(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            publish, output = root / "publish", root / "output"
            publish.mkdir()
            output.mkdir()
            (publish / packaging.APP).write_bytes(b"\x7fELFfixture")
            (publish / "CMCLauncher.Avalonia.dll").write_bytes(b"fixture")

            def command(*args, **kwargs):
                if args[0] == "rpmbuild":
                    top = Path(next(str(arg)[8:] for arg in args if str(arg).startswith("_topdir ")))
                    rpm = top / "RPMS/x86_64/fixture.rpm"
                    rpm.parent.mkdir(parents=True)
                    rpm.write_bytes(b"fixture")

            with patch.object(packaging, "run", side_effect=command):
                packaging.deb(publish, root, output, "1.2.3-beta.2")
                packaging.rpm(publish, root, output, "1.2.3-beta.2")
            control = (root / "deb/DEBIAN/control").read_text()
            self.assertIn("Version: 1.2.3~beta.2", control)
            self.assertIn("Architecture: amd64", control)
            self.assertIn("libsecret-tools", control)
            rpm = (root / "rpmbuild/SPECS/cmclauncher.spec").read_text()
            self.assertIn("Version: 1.2.3", rpm)
            self.assertIn("Release: 0.beta.2", rpm)
            self.assertIn("AutoReqProv: no", rpm)
            for name in ("deb", "rpm-root"):
                tree = root / name
                self.assertEqual((tree / "opt/cmclauncher/cmclauncher-distribution").read_text(), "package\n")
                launcher = tree / "usr/bin/cmclauncher"
                self.assertTrue(launcher.read_text().startswith("#!/bin/sh\n"))
                if os.name != "nt":
                    self.assertTrue(launcher.stat().st_mode & 0o111)
                self.assertTrue((tree / "opt/cmclauncher/LICENSE").is_file())
                self.assertIn("Exec=cmclauncher", (tree / "usr/share/applications/cmclauncher.desktop").read_text())
            self.assertTrue((output / "CMCLauncher-Avalonia-linux-x64.rpm").is_file())


if __name__ == "__main__":
    unittest.main()
