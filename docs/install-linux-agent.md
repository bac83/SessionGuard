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

## Install steps
1. Install the agent package or binary for your environment.
2. Configure the server URL, optional API key, cache/status paths, and user mapping.
3. Install the example systemd unit from `docs/sessionguard-agent.service`.
4. Start the service and verify that it can fetch a policy.
5. Launch the tray UI in the user session if you want visible status.

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

## Operational notes
- The agent should continue working with the last valid policy if the server is temporarily unavailable.
- The provided systemd unit runs the agent as `root` in the MVP so `loginctl` session locking works reliably.
- The tray UI must not require root rights.

## Verification
- Confirm the agent can poll the server successfully.
- Confirm a policy is cached locally.
- Confirm a test budget expiration triggers a session lock.
