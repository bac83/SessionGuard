# Operations

This document describes the MVP deployment and day-to-day operations.

## Server deployment
- Run the server in containers.
- The server container hosts both the Blazor UI and `/api/*` endpoints on port `8080`.
- Mount the SQLite database into a persistent volume.
- Configure the server with environment variables rather than hardcoded values.
- Set `SessionGuard__Security__ApiKey` in deployed environments so `/api/*` requests require the shared API key header. The UI and health check remain reachable without that header.
- Keep the server Linux-agnostic.
- Keep the MVP API on a trusted network or behind a reverse proxy. Authentication and authorization are intentionally deferred for this first slice.

## SQLite persistence
- Store the SQLite database in a named or host-mounted volume.
- Back up the volume regularly.
- Avoid deleting the database file unless you intend to reset all data.

## Policy refresh model
- The agent polls the server on a fixed interval.
- The agent keeps the last valid policy locally for offline use.
- New policies replace cached policies only after successful validation.

## Monitoring
- Watch server logs for policy fetch and usage-report events.
- Watch agent logs for polling, cache updates, and lock attempts.
- Treat repeated poll failures as a connectivity issue before assuming policy corruption.

## Backups and recovery
- Back up the SQLite volume before upgrades or schema changes.
- Restore the database and restart the server to recover from a failed host.
- Reinstall or restart agents after connectivity or local cache issues.

## Scope reminders
- No web or app blocking.
- No Home Assistant integration.
- No Windows or macOS agent support.
- No realtime push updates in the MVP.
