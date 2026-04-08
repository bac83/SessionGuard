# AGENTS.md

## Mission
Build a small, production-minded MVP for centralized Linux screen-time management.

## Hard guardrails
- Keep server and Linux agent separate
- Keep implementations small and incremental
- Do not add Home Assistant support yet
- Do not add Windows/macOS support
- Do not add app/web filtering
- Prefer polling over push for config refresh
- Use SQLite for MVP server persistence
- Store SQLite in a defined mounted volume
- Agent must cache and use the last valid policy offline

## Architecture rules
- Shared contracts may be reused across server and agent
- No Linux-specific code in server projects
- No UI code in agent core
- Linux/session/lock behavior must be hidden behind interfaces
- Avoid premature abstractions not needed by current scope

## Subagents
Use subagents when helpful. Preferred roles:
- architecture-review
- server-backend
- linux-agent
- ui
- tests
- ci-cd
- docs

Keep outputs aligned with this file. Resolve conflicts in favor of simplicity and the MVP.

## Testing rules
- Use latest xUnit and NSubstitute
- Add or update tests for every meaningful behavior change
- Prefer unit tests for policy/state logic
- Prefer integration tests for API, persistence, and wiring
- Do not merge code with failing tests

## PR rules
- Keep PRs small and focused
- Link the issue if one is mentioned
- Include a short summary, risks, and test notes
- Include screenshots for UI changes
- Mention migrations/config changes explicitly
- Do not mix broad refactors with new features unless required

## Definition of done
A task is done only if:
- code builds
- tests pass
- docs are updated where relevant
- no unnecessary TODO placeholders remain
- CI is green

## Coding style
- Prefer clear names over clever code
- Keep files and classes small
- Minimize hidden behavior
- Fail explicitly, log clearly
- Make local development easy

## Delivery priority
1. Shared contracts
2. Server API + UI + SQLite + Docker
3. Example docker-compose
4. Linux agent core
5. Agent install docs
6. GitHub CI/CD
