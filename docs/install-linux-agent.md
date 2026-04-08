# Linux Agent Installation

This guide covers the MVP Linux agent for SessionGuard.

## What the agent does
- Polls the server for the current policy
- Caches the last valid policy locally
- Tracks local usage for the active user session
- Locks the session when the daily budget is exhausted
- Shows remaining time in a small local UI

## Requirements
- A supported Linux distribution with systemd
- A non-root desktop session for the child account
- Network access to the SessionGuard server
- A configured `child_id` mapping for each managed local user

## Install steps
1. Install the agent package or binary for your environment.
2. Create a dedicated system service user for the agent.
3. Configure the server URL, cache/status paths, and user mapping.
4. Install the example systemd unit from `docs/sessionguard-agent.service`.
5. Start the service and verify that it can fetch a policy.
6. Launch the tray UI in the user session if you want visible status.

## Configuration
Use environment variables or a config file. Keep secrets out of source control.

Typical settings:
- `SessionGuard__Agent__ServerBaseUrl`
- `SessionGuard__Agent__AgentId`
- `SessionGuard__Agent__CacheDirectory`
- `SessionGuard__Agent__StatusFilePath`
- `SessionGuard__Agent__UserMapPath`
- `SessionGuard__Agent__PollIntervalSeconds`

## Operational notes
- The agent should continue working with the last valid policy if the server is temporarily unavailable.
- The system service may need elevated permissions for session lock integration.
- The tray UI must not require root rights.

## Verification
- Confirm the agent can poll the server successfully.
- Confirm a policy is cached locally.
- Confirm a test budget expiration triggers a session lock.
