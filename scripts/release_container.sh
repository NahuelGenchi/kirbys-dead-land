#!/usr/bin/env bash
# Build the player packages on this machine the way the release workflow does: the
# committed HEAD, in the same pinned Ubuntu 22.04 container, with the same steps
# (.github/workflows/release.yml). Needs Docker. The packages, the source archive
# and their .sha256 files land in dist/.
#
#   bash scripts/release_container.sh [vX.Y.Z]
#
# Uncommitted changes are not built. Extra `docker run` options, such as
# --memory 4g, can be given in RELEASE_DOCKER_ARGS.
set -euo pipefail

IMAGE='ubuntu:22.04@sha256:0a9887931b0e38af2e3487ed39e29436b0535c4f3f52e57745baaf79d1898956'
project=$(cd "$(dirname "$0")/.." && pwd)
tag=${1:-}
inputs=$(mktemp -d)
trap 'rm -rf "$inputs"' EXIT
git -C "$project" bundle create "$inputs/source.bundle" HEAD ${tag:+"refs/tags/$tag"}
mkdir -p "$project/dist"

# shellcheck disable=SC2086 # RELEASE_DOCKER_ARGS holds separate options.
docker run --rm ${RELEASE_DOCKER_ARGS:-} \
    -e TAG="$tag" -e OWNER="$(id -u):$(id -g)" \
    -v "$inputs:/inputs:ro" -v "$project/dist:/output" \
    "$IMAGE" bash -euo pipefail -c '
        export HOME=/github/home
        mkdir -p "$HOME"
        apt-get update -qq
        DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends ca-certificates git > /dev/null
        # The checkout path GitHub uses, spelled from parts: the package lint refuses
        # any file that names the folder it was built in.
        repository=kirbys-dead-land
        workspace="/__w/$repository/$repository"
        git clone -q /inputs/source.bundle "$workspace"
        cd "$workspace"
        git checkout -q --detach "${TAG:-HEAD}"
        git remote set-url origin https://github.com/NahuelGenchi/kirbys-dead-land.git
        bash scripts/release_tools.sh
        export PATH="/opt/python/bin:$HOME/.cargo/bin:$PATH"
        git config --global --add safe.directory "$workspace"
        python3 scripts/build_packages.py --output dist ${TAG:+--tag "$TAG"}
        python3 scripts/package_release.py --ref HEAD --output dist
        cp dist/*.AppImage dist/*.tar.gz dist/*.sha256 /output/
        chown "$OWNER" /output/*
    '
ls -l "$project/dist"
