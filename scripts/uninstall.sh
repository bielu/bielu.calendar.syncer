#!/usr/bin/env bash
# Removes the LaunchAgent and the published binaries. Connected accounts and sync state are left alone.
set -euo pipefail

LABEL="io.bielu.calendar.syncer"
INSTALL_DIR="${HOME}/Library/Application Support/bielu/calendar-syncer"
PLIST="${HOME}/Library/LaunchAgents/${LABEL}.plist"

echo "==> Unloading the agent"
launchctl bootout "gui/$(id -u)/${LABEL}" 2>/dev/null || true
rm -f "${PLIST}"

echo "==> Removing ${INSTALL_DIR}"
rm -rf "${INSTALL_DIR}"

echo
echo "Uninstalled. Accounts and sync state are still in ~/.bielu/calendar-syncer"
echo "Delete that directory too if you want a clean slate."
