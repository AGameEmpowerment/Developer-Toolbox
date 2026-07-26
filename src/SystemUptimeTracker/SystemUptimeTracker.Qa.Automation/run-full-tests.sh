#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_PATH="${SCRIPT_DIR}/SystemUptimeTracker.Qa.Automation.csproj"
SETTINGS_PATH="${SCRIPT_DIR}/SystemUptimeTracker.Qa.Automation.full.runsettings"

dotnet test "${PROJECT_PATH}" --settings "${SETTINGS_PATH}" "$@"
