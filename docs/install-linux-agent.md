# Linux Agent Installation

This guide covers the MVP Linux agent for SessionGuard.

## What the agent does
- Polls the server for the current policy
- Caches the last valid policy locally
- Tracks local usage with a poll-interval-based MVP approximation for the mapped user and persists it across agent restarts
- Locks the session when the daily budget is exhausted or the profile is paused in the admin UI
- Shows remaining time in a small local UI

## Requirements
- A supported Linux distribution with systemd
- A non-root desktop session for the child account
- Network access to the SessionGuard server
- A configured `child_id` mapping for each managed local user
- `yad` for the Cinnamon tray icon when installing manually. The Debian package declares this dependency.

## Install steps
1. Install the versioned `.deb` package from the CI artifact, or install the published binaries manually.
2. Configure the server URL, optional API key, cache/status paths, and user mapping in `/etc/sessionguard/agent.env` and `/etc/sessionguard/user-map.json`.
3. For manual binary installs, install the example systemd unit from `docs/sessionguard-agent.service`.
4. The Debian package enables and restarts `sessionguard-agent.service` automatically after install or upgrade so the newly installed agent is used immediately.
5. The Debian package installs a user-session autostart entry for the tray status UI at `/etc/xdg/autostart/sessionguard-tray.desktop`.
6. Add each desktop user that should see the tray status to the `sessionguard` group and start a new login session.

## Artifact install example
Download the latest successful `main` pipeline artifact with GitHub CLI and install it:

```bash
set -euo pipefail

OWNER=bac83
REPO=SessionGuard
WORKFLOW=ci.yml
BRANCH=main
DOWNLOAD_DIR=/tmp/sessionguard-agent

RUN_ID="$(gh run list \
  --repo "$OWNER/$REPO" \
  --workflow "$WORKFLOW" \
  --branch "$BRANCH" \
  --status success \
  --limit 1 \
  --json databaseId \
  --jq '.[0].databaseId')"

rm -rf "$DOWNLOAD_DIR"
mkdir -p "$DOWNLOAD_DIR"
gh run download "$RUN_ID" \
  --repo "$OWNER/$REPO" \
  --pattern 'sessionguard-agent_*_amd64' \
  --dir "$DOWNLOAD_DIR"

DEB="$(find "$DOWNLOAD_DIR" -name 'sessionguard-agent_*_amd64.deb' -print -quit)"
sudo apt install -y "$DEB"
```

Configure the installed agent for your server and child user:

```bash
set -euo pipefail

SERVER_URL=http://192.168.178.188:38080
AGENT_ID=mond-linux
LOCAL_USER=childuser
CHILD_ID=child-01

sudo install -d -m 755 /etc/sessionguard
sudo tee /etc/sessionguard/agent.env >/dev/null <<EOF
SessionGuard__Agent__ServerBaseUrl=$SERVER_URL
SessionGuard__Agent__AgentId=$AGENT_ID
SessionGuard__Agent__UserMapPath=/etc/sessionguard/user-map.json
SessionGuard__Agent__CacheDirectory=/var/lib/sessionguard/cache
SessionGuard__Agent__StatusFilePath=/var/lib/sessionguard/status/agent-status.json
SessionGuard__Agent__PollIntervalSeconds=60
EOF

sudo tee /etc/sessionguard/user-map.json >/dev/null <<EOF
{
  "$LOCAL_USER": "$CHILD_ID"
}
EOF

sudo usermod -aG sessionguard "$LOCAL_USER"
sudo systemctl restart sessionguard-agent.service
```

The tray user needs a new login session before the new group membership is visible to Cinnamon autostart.

Test the service and local status:

```bash
set -euo pipefail

systemctl status sessionguard-agent.service --no-pager
journalctl -u sessionguard-agent.service -n 80 --no-pager
sudo test -s /var/lib/sessionguard/status/agent-status.json
sudo -u "$LOCAL_USER" SESSIONGUARD_STATUS_FILE=/var/lib/sessionguard/status/agent-status.json dotnet /opt/sessionguard/tray/Agent.Tray.dll --once
```

## Configuration
Use environment variables or a config file. Keep secrets out of source control.

Typical settings:
- `SessionGuard__Agent__ServerBaseUrl`
- `SessionGuard__Agent__ApiKey`
- `SessionGuard__Agent__AgentId`
- `SessionGuard__Agent__CacheDirectory`
- `SessionGuard__Agent__StatusFilePath`
- `SessionGuard__Agent__UserMapPath`
- `SessionGuard__Agent__PollIntervalSeconds`
- `SessionGuard__Agent__AgentVersion` is optional. When omitted, the packaged assembly version is reported.

## Operational notes
- The agent should continue working with the last valid policy if the server is temporarily unavailable.
- The provided systemd unit runs the agent as `root` in the MVP so it can resolve desktop sessions and request locks.
- Session locking first uses `loginctl lock-session`. If that fails, the agent also tries user-session fallbacks through the user's DBus session: first `cinnamon-screensaver-command --lock`, then `gnome-screensaver-command -l`.
- The tray UI must not require root rights.
- Usage is persisted in `/var/lib/sessionguard/cache/usage-state.json`. Package upgrades and service restarts must preserve this file.
- The status snapshot is written to `/var/lib/sessionguard/status/agent-status.json` with owner read/write and `sessionguard` group read permissions. Add tray users to the `sessionguard` group instead of granting global read access.

## Verification
- Confirm the agent can poll the server successfully.
- Confirm a policy is cached locally.
- Confirm a test budget expiration triggers a session lock.
- If the visible desktop does not lock, test the fallbacks directly with `sudo -u "$LOCAL_USER" DISPLAY=:0 XDG_RUNTIME_DIR=/run/user/$(id -u "$LOCAL_USER") DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/$(id -u "$LOCAL_USER")/bus cinnamon-screensaver-command --lock` and then `sudo -u "$LOCAL_USER" DISPLAY=:0 XDG_RUNTIME_DIR=/run/user/$(id -u "$LOCAL_USER") DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/$(id -u "$LOCAL_USER")/bus gnome-screensaver-command -l`.
- Confirm `journalctl -u sessionguard-agent.service` shows the agent id, version, cache path, status path, policy decision, lock attempt, and which lock method accepted the request.
