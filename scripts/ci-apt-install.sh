#!/bin/sh
# Recover package installation on disposable hosted runners without depending on one DNS/mirror.
set -eu

if [ "${GITHUB_ACTIONS:-}" != true ]; then
    echo "This helper only changes APT sources on disposable GitHub Actions runners." >&2
    exit 1
fi
if [ "$#" -eq 0 ]; then
    echo "Pass the packages to install." >&2
    exit 1
fi

missing=false
for package in "$@"; do
    if [ "$(dpkg-query -W -f='${db:Status-Status}' "$package" 2>/dev/null || true)" != installed ]; then
        missing=true
        break
    fi
done
if [ "$missing" = false ]; then
    echo "All requested packages are already installed; skipping APT network access."
    exit 0
fi

dns_available() {
    timeout 8s getent ahostsv4 archive.ubuntu.com >/dev/null 2>&1
}

if ! dns_available; then
    echo "Ubuntu archive DNS resolution failed."
    if [ "${RUNNER_ENVIRONMENT:-}" != github-hosted ]; then
        echo "DNS recovery is only allowed on disposable GitHub-hosted runners." >&2
        exit 1
    fi
    repaired=false
    if command -v resolvectl >/dev/null 2>&1 && command -v ip >/dev/null 2>&1; then
        interface=$(ip -o -4 route show default | awk 'NR == 1 { print $5 }')
        if [ -n "$interface" ] && resolvectl dns "$interface" 1.1.1.1 8.8.8.8 && resolvectl domain "$interface" '~.'; then
            resolvectl flush-caches
            if dns_available; then repaired=true; fi
        fi
    fi
    if [ "$repaired" = false ]; then
        if [ ! -e /etc/resolv.conf.cmcl-backup ]; then
            cp -L /etc/resolv.conf /etc/resolv.conf.cmcl-backup
        fi
        printf 'nameserver 1.1.1.1\nnameserver 8.8.8.8\noptions timeout:2 attempts:2\n' > /etc/resolv.conf.cmcl
        mv -f /etc/resolv.conf.cmcl /etc/resolv.conf
        if ! dns_available; then
            echo "DNS remains unavailable after recovery; the runner network must be retried." >&2
            exit 1
        fi
    fi
fi

install_packages() {
    apt-get -o Acquire::ForceIPv4=true -o Acquire::Retries=2 -o Acquire::http::Timeout=20 -o Acquire::https::Timeout=20 -o APT::Update::Error-Mode=any update &&
        apt-get -o Acquire::ForceIPv4=true -o Acquire::Retries=2 -o Acquire::http::Timeout=20 -o Acquire::https::Timeout=20 install -y "$@"
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
