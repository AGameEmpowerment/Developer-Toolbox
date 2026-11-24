#!/usr/bin/env bash
# Local-development image acquisition regression tests; never contact an engine.
set -euo pipefail
export TOOLBOX_IMAGE_MIRRORS=docker.io=registry.example.invalid/docker-hub
source "$(dirname "${BASH_SOURCE[0]}")/../.devcontainer/ensure_image.sh"
calls=()
mode=cached
fake_engine() {
    calls+=("$*")
    if [[ "$1" == image ]]; then
        [[ "$mode" == cached || ( "$mode" == mirror && "${!#}" == registry.example.invalid/* ) ]]
    elif [[ "$1" == pull ]]; then
        echo pull-progress
        [[ "$mode" != denied && ( ( "$mode" != mirror && "$mode" != fallback ) || "${!#}" == registry.example.invalid/* ) ]]
    fi
}
for mode in cached missing mirror fallback denied; do
    calls=()
    failed=false
    ensure_toolbox_image fake_engine docker.io/library/redis:latest || failed=true
    [[ "$failed" == false || "$mode" == denied ]] || exit 1
    [[ "$mode" != denied || "$failed" == true ]] || exit 1
    [[ "$mode" != cached || "${#calls[@]}" == 1 ]] || exit 1
    [[ "$mode" != missing || "${calls[1]}" == 'pull docker.io/library/redis:latest' ]] || exit 1
    [[ ( "$mode" != mirror && "$mode" != fallback ) || "${calls[${#calls[@]}-1]}" == 'tag registry.example.invalid/docker-hub/library/redis:latest docker.io/library/redis:latest' ]] || exit 1
done
mode=missing
[[ -z "$(ensure_toolbox_image fake_engine docker.io/library/redis:latest)" ]] || exit 1
echo 'Image preparation tests passed: cached, missing, cached registry fallback, and denied registries.'
