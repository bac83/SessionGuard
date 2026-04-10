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
4. Start the service and verify that it can fetch a policy.
5. The Debian package installs a user-session autostart entry for the tray status UI at `/etc/xdg/autostart/sessionguard-tray.desktop`.

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
- The provided systemd unit runs the agent as `root` in the MVP so `loginctl` session locking works reliably.
- The tray UI must not require root rights.
- Usage is persisted in `/var/lib/sessionguard/cache/usage-state.json`. Package upgrades and service restarts must preserve this file.
- The status snapshot is written to `/var/lib/sessionguard/status/agent-status.json` with read permissions so the user-session tray can display it.

## Verification
- Confirm the agent can poll the server successfully.
- Confirm a policy is cached locally.
- Confirm a test budget expiration triggers a session lock.
- Confirm `journalctl -u sessionguard-agent.service` shows the agent id, version, cache path, status path, policy decision, and lock attempt.
