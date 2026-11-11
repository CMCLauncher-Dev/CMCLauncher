#!/bin/sh
# Hosted Ubuntu runners sometimes lose access to their Azure mirror. Retry official sources once.
set -eu

if [ "${GITHUB_ACTIONS:-}" != true ]; then
    echo "This helper only changes APT sources on disposable GitHub Actions runners." >&2
    exit 1
fi
if [ "$#" -eq 0 ]; then
    echo "Pass the packages to install." >&2
    exit 1
fi

install_packages() {
    apt-get -o Acquire::Retries=2 -o Acquire::http::Timeout=20 -o Acquire::https::Timeout=20 -o APT::Update::Error-Mode=any update &&
        apt-get -o Acquire::Retries=2 -o Acquire::http::Timeout=20 -o Acquire::https::Timeout=20 install -y "$@"
}

if install_packages "$@"; then
    exit 0
fi

. /etc/os-release
if [ "$ID" != ubuntu ]; then
    echo "APT failed; official-source fallback is only configured for Ubuntu." >&2
    exit 1
fi
case "$VERSION_CODENAME" in
    *[!a-z]*|'') echo "Invalid Ubuntu codename." >&2; exit 1 ;;
esac

echo "APT failed; retrying with the official Ubuntu archive and security sources."
for source in /etc/apt/sources.list /etc/apt/sources.list.d/*.list /etc/apt/sources.list.d/*.sources; do
    if [ -f "$source" ]; then
        mv -- "$source" "$source.cmcl-disabled"
    fi
done
cat > /etc/apt/sources.list.d/cmcl-ci.sources <<EOF
Types: deb
URIs: https://archive.ubuntu.com/ubuntu/
Suites: $VERSION_CODENAME $VERSION_CODENAME-updates $VERSION_CODENAME-backports
Components: main restricted universe multiverse
Signed-By: /usr/share/keyrings/ubuntu-archive-keyring.gpg

Types: deb
URIs: https://security.ubuntu.com/ubuntu/
Suites: $VERSION_CODENAME-security
Components: main restricted universe multiverse
Signed-By: /usr/share/keyrings/ubuntu-archive-keyring.gpg
EOF
install_packages "$@"
