#!/usr/bin/env bash
# Local development only. Reuse the selected engine's tag before contacting a registry.
ensure_toolbox_image() {
    local runtime="$1" image="$2" normalized registry prefix fallback mapping
    "$runtime" image inspect "$image" >/dev/null 2>&1 && return 0
    "$runtime" pull "$image" >&2 && return 0
    normalized="${image#docker.io/}"
    registry=docker.io
    if [[ "$normalized" == */* && "${normalized%%/*}" == *[.:]* ]]; then
        registry="${normalized%%/*}"
        normalized="${normalized#*/}"
    elif [[ "$normalized" != */* ]]; then normalized="library/$normalized"; fi
    prefix=''
    # Optional registry=mirror-prefix pairs, separated by commas; no credentials.
    local -a mappings=()
    IFS=',' read -r -a mappings <<< "${TOOLBOX_IMAGE_MIRRORS:-}"
    for mapping in "${mappings[@]}"; do
        [[ "${mapping%%=*}" != "$registry" ]] || prefix="${mapping#*=}"
    done
    if [[ ! "$prefix" =~ ^[A-Za-z0-9.-]+(:[0-9]+)?/[A-Za-z0-9/_-]+/?$ ]]; then
        echo "Cannot pull '$image'. Configure its approved registry mirror in TOOLBOX_IMAGE_MIRRORS." >&2
        return 1
    fi
    fallback="${prefix%/}/$normalized"
    "$runtime" image inspect "$fallback" >/dev/null 2>&1 || "$runtime" pull "$fallback" >&2 || return 1
    "$runtime" tag "$fallback" "$image" >&2
}
