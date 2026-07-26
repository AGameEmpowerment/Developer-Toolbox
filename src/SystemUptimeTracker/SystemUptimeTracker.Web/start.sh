#!/usr/bin/env bash
set -euo pipefail

cd /app || { echo "[start.sh] Failed to cd to /app"; exit 1; }

PORT="${PORT:-3001}"
export PORT
export HOSTNAME="0.0.0.0"

echo "[start.sh] Starting Next.js (PORT=$PORT NODE_ENV=${NODE_ENV:-unset})"

# Basic artifact validations for external build process
if [[ ! -d node_modules ]]; then
	echo "[start.sh] ERROR: node_modules directory missing. External build step must provide it." >&2
	exit 1
fi

if [[ ! -d .next ]]; then
	echo "[start.sh] ERROR: .next build output missing. External build step must run 'next build'." >&2
	exit 1
fi

if [[ ! -f package.json ]]; then
	echo "[start.sh] WARN: package.json not found (unexpected). Proceeding." >&2
fi

echo "[start.sh] Node version: $(node -v)"
if command -v jq >/dev/null 2>&1; then
	app_ver=$(jq -r '.version // empty' package.json 2>/dev/null || true)
	[[ -n "$app_ver" ]] && echo "[start.sh] App version: $app_ver"
fi

# Prefer the standalone server if present (Next.js standalone build)
if [[ -f .next/standalone/server.js ]]; then
	echo "[start.sh] Detected standalone build (.next/standalone/server.js). Launching without 'next start' warning..."
	exec node .next/standalone/server.js
fi

# Legacy path: some pipelines may flatten standalone server.js to root
if [[ -f server.js ]]; then
	echo "[start.sh] Detected flattened standalone build (server.js). Launching..."
	exec node server.js
fi

# Fallback: if .next directory exists, use next start
if [[ -d .next ]]; then
	if ! command -v node > /dev/null; then
		echo "[start.sh] Node not found in PATH"; exit 1; fi
	if [[ -x ./node_modules/.bin/next ]]; then
		echo "[start.sh] Using local next binary: ./node_modules/.bin/next start -p $PORT"
		exec ./node_modules/.bin/next start -p "$PORT"
	fi
	if command -v next >/dev/null 2>&1; then
		echo "[start.sh] Using global next: next start -p $PORT"
		exec next start -p "$PORT"
	fi
	echo "[start.sh] ERROR: No local or global 'next' binary found in external artifact image." >&2
	exit 1
fi

echo "[start.sh] ERROR: No standalone server.js or .next build output found. Did the build stage run?" >&2
ls -al . || true
exit 1
