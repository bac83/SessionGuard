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
For every non-trivial coding task, use subagents unless explicitly impossible.
At minimum, delegate one review or verification task to a subagent before finalizing.
Preferred roles:
- architecture-review
- server-backend
- linux-agent
- ui
- tests
- ci-cd
- docs

If subagents are not used, explicitly state why in the final response.

Keep outputs aligned with this file. Resolve conflicts in favor of simplicity and the MVP.

## Testing rules
- Use latest xUnit and NSubstitute
- Add or update tests for every meaningful behavior change
- Prefer unit tests for policy/state logic
- Prefer integration tests for API, persistence, and wiring
- Do not merge code with failing tests

## Branch and PR rules

- Before making any code changes, create or switch to a new branch with prefix `codex/` and a descriptive name.
- Do not implement code changes directly on `main` or the current user branch unless the user explicitly asks for it.
- After implementation and successful local verification, commit the changes.
- Push the branch and open a PR when GitHub credentials/remotes are available.
- If push or PR creation is not possible, state the blocker and provide the exact branch name, commit status, and recommended PR title/body.
- Every update on a PR must request a review from `@copilot`.
- On PR update every comment that has been worked on must be answered and marked as resolved.

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

## Repo Skill Preferences

- Use `caveman` when the user asks for brief, terse, token-saving, or “caveman mode” responses.
- Use `caveman-commit` whenever generating commit messages or after staging changes.
- Use `caveman-review` whenever reviewing diffs, PRs, or code-review comments.
- Use `compress` when the user asks to compress memory files, AGENTS-style notes, CLAUDE.md, TODO docs, preferences, or long repo guidance.

## Short Commands

- `/caveman` means: use the `caveman` skill.
- `/commit` means: use the `caveman-commit` skill.
- `/review` means: use the `caveman-review` skill.
- `/compress <file>` means: use the `compress` skill on that file.