#!/usr/bin/env bash
# Install the pinned tools that scripts/build_packages.py needs, as root on a fresh
# Ubuntu 22.04: the release workflow's container, or the same image run locally
# (docs/RELEASE.md). Python goes to /opt/python and Rust to $CARGO_HOME (~/.cargo);
# under GitHub Actions both are added to the PATH of the later steps.
set -euo pipefail

# The launcher's own Python pin (launcher/src/setup.rs).
PYTHON_URL='https://github.com/astral-sh/python-build-standalone/releases/download/20261009/cpython-3.14.8%2B20261009-x86_64-unknown-linux-gnu-install_only.tar.gz'
PYTHON_SHA256='83f9cb480b702548c592443f86209cf0fc03448d90692df257f095c01791dccc'
RUSTUP_URL='https://static.rust-lang.org/rustup/archive/1.29.1/x86_64-unknown-linux-gnu/rustup-init'
RUSTUP_SHA256='dda7234360b7f578ca8b0ddcb80145646fa61a67c1720a5abc7051b35c9fcb71'
RUST_TOOLCHAIN='1.99.0'

export DEBIAN_FRONTEND=noninteractive
apt-get update
# C and C++ compilers and CMake for melonDS and the bridge, ALSA headers for an
# audio crate melon-rs depends on, fontconfig headers for the launcher's text, ICU
# and OpenSSL for the .NET SDK, squashfs-tools for the AppImage, and
# desktop-file-utils to check the desktop entry.
apt-get install -y --no-install-recommends \
    build-essential ca-certificates cmake curl desktop-file-utils git libasound2-dev \
    libfontconfig-dev libicu70 libssl3 pkg-config squashfs-tools

fetch() {
    curl -fsSL --retry 3 -o "$2" "$1"
    echo "$3  $2" | sha256sum --check --strict -
}
downloads=$(mktemp -d)
fetch "$PYTHON_URL" "$downloads/python.tar.gz" "$PYTHON_SHA256"
rm -rf /opt/python
tar -xzf "$downloads/python.tar.gz" -C /opt
fetch "$RUSTUP_URL" "$downloads/rustup-init" "$RUSTUP_SHA256"
chmod +x "$downloads/rustup-init"
"$downloads/rustup-init" -y --no-modify-path --profile minimal --default-toolchain "$RUST_TOOLCHAIN"
rm -rf "$downloads"

cargo_bin="${CARGO_HOME:-$HOME/.cargo}/bin"
if [ -n "${GITHUB_PATH:-}" ]; then
    printf '%s\n' /opt/python/bin "$cargo_bin" >> "$GITHUB_PATH"
fi
/opt/python/bin/python3 --version
"$cargo_bin/rustc" --version
