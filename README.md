# SessionGurad

Minimal client-server system for centrally managing screen-time policies for Linux child accounts.

## MVP
- Dockerized server with web UI and SQLite
- SQLite stored in a mounted volume
- Example `docker-compose.yml`
- Linux agent with local fallback to last valid policy
- Local user -> `child_id` mapping
- Usage tracking, remaining time display, session lock on expiry
- GitHub CI/CD
- Linux agent installation guide

## Non-goals
- Home Assistant integration
- Windows/macOS agents
- Web/app blocking
- Realtime push config updates
- HA-specific auth or entity models

## Architecture
- `server`: API + admin UI + SQLite
- `agent`: Linux service + small user UI
- `shared`: contracts only, no Linux or UI-specific logic

## Suggested structure
- `src/Server.Api`
- `src/Server.Ui`
- `src/Server.Infrastructure`
- `src/Agent.Core`
- `src/Agent.Linux`
- `src/Agent.Tray`
- `src/Shared.Contracts`
- `tests/*`

## Technical rules
- Keep server and agent as separate deployables
- SQLite path must be configurable and default to a mounted volume path
- Agent must work offline with last valid policy
- Linux-specific behavior must sit behind interfaces
- Root rights only for the agent system service, never for UI parts
- Config and secrets via env vars or config files, never hardcoded

## Testing
- Unit and integration tests with latest xUnit and NSubstitute
- Policy evaluation must be heavily unit tested
- API + SQLite paths covered by integration tests
- No PR without green tests

## CI/CD
- GitHub Actions for build, test, formatting, Docker image build
- Publish images/releases only from `main` or tags
- Keep workflows simple and deterministic

## Deliverables
- Server Dockerfile
- Example `docker-compose.yml`
- Volume-backed SQLite setup
- Linux agent service
- Agent install documentation
- CI/CD workflows
