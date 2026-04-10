# SessionGuard

Minimal client-server system for centrally managing screen-time policies for Linux child accounts.

## MVP
- Dockerized server with a Blazor admin UI and SQLite persistence
- SQLite stored in a mounted volume
- Example `docker-compose.yml`
- Linux agent that polls for policy updates and caches the last valid policy offline
- Local `user -> child_id` mapping on the agent
- Usage tracking, remaining time display, persisted local usage state, and session lock on expiry or admin pause
- Versioned Linux agent Debian package with tray autostart support
- GitHub CI/CD
- Linux agent installation guide

## Non-goals
- Home Assistant integration
- Windows/macOS agents
- Web/app blocking
- Realtime push config updates
- HA-specific auth or entity models
- Full parental-control suite beyond daily screen-time budgets

## Architecture
- `Server.Api`: admin and agent HTTP endpoints
- `Server.Ui`: Blazor admin UI
- `Server.Infrastructure`: persistence and server-side wiring
- `Shared.Contracts`: transport contracts shared by server and agent
- `Agent.Core`: policy/state logic and offline cache behavior
- `Agent.Linux`: Linux session, lock, and system integration behind interfaces
- `Agent.Tray`: small local status UI for the Linux desktop session
- `packaging/deb`: Debian package assets for the Linux agent and tray autostart

## Repository layout
- `src/Server.Api`
- `src/Server.Ui`
- `src/Server.Infrastructure`
- `src/Agent.Core`
- `src/Agent.Linux`
- `src/Agent.Tray`
- `src/Shared.Contracts`
- `tests/*`
- `docs/install-linux-agent.md`
- `docs/operations.md`
- `docker-compose.yml`
- `docker-compose.build.yml`

## Technical rules
- Keep server and agent as separate deployables
- Keep SQLite path configurable and default it to a mounted volume path
- Agent must work offline with the last valid policy
- Linux-specific behavior must sit behind interfaces
- Root rights only for the agent system service, never for UI parts
- Config and secrets via env vars or config files, never hardcoded
- Protect deployed API endpoints with the optional shared API key in `SessionGuard__Security__ApiKey`
- Prefer polling over push for config refresh
- Avoid app/web filtering and other out-of-scope controls

## Data and policy model
- One daily minutes budget per `child_id`
- Policy responses carry a version string so the agent can detect changes and cache safely
- The server remains Linux-agnostic
- The agent owns local user mapping, session observation, and lock execution

## Testing
- Unit and integration tests with xUnit and NSubstitute
- Policy evaluation must be heavily unit tested
- API and SQLite paths should be covered by integration tests
- No PR without green tests

## CI/CD
- GitHub Actions for build, test, formatting, and Docker image build
- CI builds a versioned `sessionguard-agent_*_amd64.deb` artifact for workflow testing
- Publish images and releases only from `main` or tags
- Keep workflows simple and deterministic

## Deliverables
- Server API and UI Dockerfiles
- Example `docker-compose.yml`
- Volume-backed SQLite setup
- Linux agent service
- Agent install documentation
- CI/CD workflows

## Docker Compose usage
- Default: `docker compose up -d` pulls the published `main` images from GHCR.
- Override the image owner with `SESSIONGUARD_IMAGE_OWNER` if you publish from a fork or another org.
- Override the tag if needed with `SESSIONGUARD_IMAGE_TAG`, for example `v1.0.0`.
- Local fallback build: `docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build`
