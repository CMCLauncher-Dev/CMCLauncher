"""Exercise the APT helper with mock commands and temporary configuration files."""

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

BASH = shutil.which("bash") or ("C:/Program Files/Git/bin/bash.exe" if os.name == "nt" else None)
HELPER = Path(__file__).with_name("ci-apt-install.sh")


@unittest.skipUnless(BASH and Path(BASH).is_file(), "Bash is required")
class AptInstallTests(unittest.TestCase):
    def posix(self, path):
        if os.name != "nt":
            return str(path)
        return subprocess.check_output([BASH, "-c", 'cygpath -u "$1"', "test", str(path)], text=True).strip()

    def fixture(self, root):
        apt = root / "etc/apt"
        (apt / "sources.list.d").mkdir(parents=True)
        (apt / "sources.list").write_text("azure mirror\n")
        (root / "etc/os-release").write_text("ID=ubuntu\nVERSION_CODENAME=jammy\n")
        (root / "etc/resolv.conf").write_text("nameserver 127.0.0.53\n")
        commands = root / "bin"
        commands.mkdir()
        scripts = {
            "dpkg-query": 'printf "%s" "${APT_TEST_INSTALLED:-missing}"',
            "timeout": 'shift; exec "$@"',
            "getent": '[ "${APT_TEST_DNS_FAILURE:-}" != always ] && [ -f "$APT_TEST_ROOT/dns-ready" ]',
            "ip": 'echo "default via 192.0.2.1 dev eth0"',
            "resolvectl": 'printf "%s\\n" "$*" >> "$APT_TEST_ROOT/resolver-calls"; touch "$APT_TEST_ROOT/dns-ready"',
            "apt-get": 'printf "%s\\n" "$*" >> "$APT_TEST_ROOT/apt-calls"; [ -f "$APT_TEST_ROOT/etc/apt/sources.list.d/cmcl-ci.sources" ]',
        }
        for name, text in scripts.items():
            path = commands / name
            path.write_text("#!/bin/sh\n" + text + "\n", newline="\n")
            path.chmod(0o755)
        source = HELPER.read_text().replace("/etc/", self.posix(root / "etc") + "/")
        script = root / "helper.sh"
        script.write_text(source, newline="\n")
        return commands, script

    def execute(self, root, **settings):
        commands, script = self.fixture(root)
        env = dict(os.environ, GITHUB_ACTIONS="true", RUNNER_ENVIRONMENT="github-hosted", APT_TEST_ROOT=self.posix(root), **settings)
        return subprocess.run([BASH, "-c", 'export PATH="$1:$PATH"; sh "$2" rpm', "test", self.posix(commands), self.posix(script)], env=env, capture_output=True, text=True)

    def test_installed_packages_need_no_network(self):
        with tempfile.TemporaryDirectory(prefix="cmcl-apt-test-") as directory:
            root = Path(directory)
            result = self.execute(root, APT_TEST_INSTALLED="installed")
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertFalse((root / "apt-calls").exists())
            self.assertFalse((root / "resolver-calls").exists())

    def test_dns_recovery_and_official_source_fallback(self):
        with tempfile.TemporaryDirectory(prefix="cmcl-apt-test-") as directory:
            root = Path(directory)
            result = self.execute(root)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn("dns eth0 1.1.1.1 8.8.8.8", (root / "resolver-calls").read_text())
            self.assertEqual(len((root / "apt-calls").read_text().splitlines()), 3)
            self.assertIn("https://archive.ubuntu.com/ubuntu/", (root / "etc/apt/sources.list.d/cmcl-ci.sources").read_text())

    def test_persistent_dns_failure_stops_before_apt(self):
        with tempfile.TemporaryDirectory(prefix="cmcl-apt-test-") as directory:
            root = Path(directory)
            result = self.execute(root, APT_TEST_DNS_FAILURE="always")
            self.assertNotEqual(result.returncode, 0)
            self.assertFalse((root / "apt-calls").exists())
            self.assertTrue((root / "etc/resolv.conf.cmcl-backup").is_file())


if __name__ == "__main__":
    unittest.main()
