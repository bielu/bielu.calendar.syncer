#!/usr/bin/env bash
# Publishes the syncer and registers it as a macOS LaunchAgent so it starts at login.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LABEL="io.bielu.calendar.syncer"
INSTALL_DIR="${HOME}/Library/Application Support/bielu/calendar-syncer"
LOG_DIR="${HOME}/Library/Logs/bielu"
PLIST="${HOME}/Library/LaunchAgents/${LABEL}.plist"

if [[ "$(uname)" != "Darwin" ]]; then
  echo "This installer targets macOS launchd." >&2
  exit 1
fi

echo "==> Publishing to ${INSTALL_DIR}"
rm -rf "${INSTALL_DIR}"
mkdir -p "${INSTALL_DIR}" "${LOG_DIR}" "$(dirname "${PLIST}")"
dotnet publish "${REPO_ROOT}/src/Bielu.Calendar.Syncer.Service" \
  --configuration Release \
  --output "${INSTALL_DIR}" \
  --nologo

echo "==> Writing ${PLIST}"
cat > "${PLIST}" <<PLIST_CONTENT
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>${LABEL}</string>
    <key>ProgramArguments</key>
    <array>
        <string>${INSTALL_DIR}/Bielu.Calendar.Syncer.Service</string>
    </array>
    <key>WorkingDirectory</key>
    <string>${INSTALL_DIR}</string>
    <key>EnvironmentVariables</key>
    <dict>
        <key>DOTNET_ENVIRONMENT</key>
        <string>Production</string>
    </dict>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <dict>
        <key>SuccessfulExit</key>
        <false/>
    </dict>
    <key>StandardOutPath</key>
    <string>${LOG_DIR}/calendar-syncer.log</string>
    <key>StandardErrorPath</key>
    <string>${LOG_DIR}/calendar-syncer.error.log</string>
    <key>ProcessType</key>
    <string>Background</string>
</dict>
</plist>
PLIST_CONTENT

echo "==> Loading the agent"
launchctl bootout "gui/$(id -u)/${LABEL}" 2>/dev/null || true
launchctl bootstrap "gui/$(id -u)" "${PLIST}"
launchctl enable "gui/$(id -u)/${LABEL}"

echo
echo "Installed. Dashboard: http://127.0.0.1:5252"
echo "Logs:                 ${LOG_DIR}/calendar-syncer.log"
echo
echo "Set your OAuth client ids before connecting calendars, for example:"
echo "  dotnet user-secrets --project src/Bielu.Calendar.Syncer.Service set \"CalendarSyncer:Google:ClientId\" \"...\""
echo "or edit ${INSTALL_DIR}/appsettings.json and run: launchctl kickstart -k gui/$(id -u)/${LABEL}"
