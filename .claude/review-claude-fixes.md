# Review: `claude/fixes` branch — Findings & Refactors

Branch: `claude/fixes` vs `main` — 4 commits, ~1247 LOC, 20 files.

> Datei liegt unter `.claude/` (untracked). Nicht committen.

---

## Commits

- `28507d7` feat(agent): freeze usage tracking when session idle/locked
- `b2f3f1d` feat(agent): broaden desktop lock fallback coverage
- `a355c93` refactor(server): replace EnsureCreated with EF migrations
- `9d1c6bc` ci: publish agent .deb to GitHub Release on tag

---

## Findings

### `28507d7` — Idle detector

- [x] **F1** Regex `^[a-z_][a-z0-9_-]*[$]?$` dupliziert in `src/Agent.Linux/LinuxIdleDetector.cs:107,187` und `LinuxSessionLockService.cs`. — gelöst via `LinuxIdentifiers`.
- [x] **F2** Inline-`Regex.IsMatch` an `LinuxIdleDetector.cs:107,177,182,187` — pro Aufruf rekompiliert. `[GeneratedRegex]` nutzen. — gelöst.
- [x] **F3** Hints werden zweimal gelesen: `LinuxIdleDetector.cs:63` (XDG-Pfad) + `:30` erneuter `loginctl show-session` Round-Trip. Hints in `SessionResolution` cachen. — gelöst.
- [x] **F4** Hardcoded Display-Fallback `:0` an `LinuxIdleDetector.cs:176` ohne Log-Hinweis bei Trigger. — gelöst (LogDebug bei Fallback).
- [x] **F5** Generic `catch (Exception)` an `LinuxIdleDetector.cs:200` — akzeptabel, aber `LogDebug` statt `LogWarning` bei wiederholtem Fehlschlag. — akzeptiert (xprintidle optional, Fail-Open Pfad bleibt Debug).

### `b2f3f1d` — Lock fallbacks

- [x] **F6** String-basierter `runuser`-Argbau mit manuellem Whitespace-Handling in `LinuxSessionLockService.cs:193–195`. — akzeptiert (Display/UID/User regex-validiert, Test-Helper deckt Format ab; ProcessStartInfo-Refactor wäre Scope-Creep).
- [x] **F7** Test-Mocks nutzen `Contains(...)` Substring-Match in `tests/Agent.Core.Tests/AgentLinuxTests.cs` — passieren auch bei vertauschter Argumentreihenfolge. Exakte Equality wo Reihenfolge stabil. — gelöst via `BuildRunUserArgs` Helper.
- [x] **F8** Bei vollständigem Fallback-Fail: keine Diagnose welche Methoden versucht/letzter stderr. Aggregat-Log ergänzen. — gelöst (failures-Liste in `LogWarning`).

### `a355c93` — EF migrations

- [x] **F9** Hardcoded EF-Version-Fallback `"10.0.0"` an `src/Server.Infrastructure/DependencyInjection.cs:154`. Begründung als Kommentar (display-only in `__EFMigrationsHistory.ProductVersion`). — gelöst.
- [x] **F10** Redundantes `reader.CloseAsync()` an `DependencyInjection.cs:103` — `await using` disposed bereits. Entfernen. — gelöst (pragma+reader in eigenem Scope).
- [x] **F11** Doppelter Legacy-Check `:57` + `:60` innerhalb Transaction. Funktional korrekt, eine redundante Query im Race-Fall. Akzeptabel; Kommentar warum. — gelöst (Kommentar ergänzt).
- [x] **F12** SqliteException-Filter `code == 1 && Message.Contains("duplicate column")` an `:110–111` — message-gating reicht aber fragil bei lokalisierten SQLite-Builds. Kommentar dazu. — gelöst (Kommentar erklärt: SQLite-Messages immer Englisch).
- [x] **F13** Test fehlt: 2-von-3 Legacy-Tables vorhanden → `MigrateAsync` muss laut scheitern, kein Stamping. Ergänzen in `tests/Agent.Core.Tests/InfrastructureInitializationTests.cs`. — gelöst (`OnPartialLegacySchema_FailsLoudlyWithoutStamping`, korrekter Pfad `tests/Server.Api.Tests/`).

### `9d1c6bc` — CI publish

- [x] **F14** Hardcoded `bac83` als `SESSIONGUARD_IMAGE_OWNER` Default in `docker-compose.yml:3`. Forks brechen leise. README-Hinweis. — bereits in README:86–87 dokumentiert.
- [x] **F15** Tag-Pattern `v*` ohne SemVer-Enforcement. Akzeptabel; Doku in `docs/operations.md` erwähnen. — gelöst (Releases-Sektion ergänzt).

### Cross-cutting

- [x] **F16** Log-Level inkonsistent: `LinuxIdleDetector` = `LogDebug`, `LinuxSessionLockService` = `LogInformation`/`LogWarning` für ähnliche Fälle. Konvention: Debug=fail-open, Info=Statuswechsel, Warning=Fallback erschöpft. — akzeptiert (nach R5: Lock-Service erfüllt Konvention; IdleDetector bleibt durchgängig Debug, da Idle-Erkennung kein Statuswechsel-Event sondern Polling-Beobachtung).
- [ ] **F17** `LocalUsageTracker` in `Agent.Linux` aber Logik plattform-agnostisch. **Skip jetzt** — bei Windows-Port refactoren.

---

## Refactor-Tasks (priorisiert)

### High value, low risk

- [x] **R1** Neue Datei `src/Agent.Linux/LinuxIdentifiers.cs` mit `IsValidLocalUser(string)` via `[GeneratedRegex]`. Ersetzt F1+F2.
- [x] **R2** `SessionHints` durch `SessionResolution` durchreichen — eliminiert Doppel-Read (F3).
- [x] **R3** `reader.CloseAsync()` entfernen, `DependencyInjection.cs:103` (F10).
- [x] **R4** Kommentar zu Version-Fallback `DependencyInjection.cs:154` (F9).

### Medium

- [x] **R5** Lock-Fallback-Diagnose: Aggregat-`LogWarning` mit versuchten Methoden + letztem stderr (F8).
- [x] **R6** Test-Assertions exakt machen, `AgentLinuxTests.cs` (F7).
- [x] **R7** Legacy-2-von-3-Tables Test, `InfrastructureInitializationTests.cs` (F13).

### Low / Docs

- [x] **R8** README: `SESSIONGUARD_IMAGE_OWNER` Override dokumentieren (F14) — bereits vorhanden.
- [x] **R9** Log-Level-Konvention vereinheitlichen (F16) — Konvention dokumentiert in F16; bestehender Code erfüllt sie.

### Skip

- ~~`LocalUsageTracker` umziehen~~ (F17 — premature).
- ~~Globale Regex-Inline-Beseitigung~~ (Scope-Creep).

---

## Verifikation

- [x] `dotnet build` clean
- [x] `dotnet test` grün inkl. neuer Legacy-Test (76/76)
- [ ] Server gegen drei DB-States starten: frisch / legacy / bereits migriert. Alle erfolgreich, `__EFMigrationsHistory` korrekt.
- [ ] Linux-Host: Idle-Detection unter Lock-Screen, X11-Idle, loginctl-only (ohne xprintidle). Usage-Minuten frieren ein.
- [ ] Test-Tag `vX.Y.Z-rc` auf Fork → `.deb` landet auf GitHub Release.
