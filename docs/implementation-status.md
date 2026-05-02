# Implementation Status — SessionGuard "fertig"

Tracking-Datei für Restarbeiten bis Production-Ready. Quelle: `~/.claude/plans/snappy-sparking-tower.md`.

Legende: `[ ]` offen · `[~]` in progress · `[x]` erledigt

---

## 1. Usage Tracker — nur aktive Zeit ✅

- [x] `src/Agent.Core/Interfaces.cs` — Interface `IIdleDetector` mit `IsActiveAsync(UserChildMapping, CancellationToken)`
- [x] `src/Agent.Linux/LinuxIdleDetector.cs` neu — `loginctl show-session --property=LockedHint --property=IdleHint --property=Display --property=User`; XDG_SESSION_ID Ownership-Check; xprintidle Fallback; fail-open bei loginctl unavailable
- [x] LinuxIdleDetector Fallback `xprintidle` via `runuser` mit Threshold; Display/UserId regex-validiert
- [x] `src/Agent.Linux/AgentLinuxOptions.cs` — `IdleThresholdSeconds` (default 300)
- [x] `src/Agent.Linux/LocalUsageTracker.cs` — Konstruktor um `IIdleDetector`; idle → `LastSeen=now` persistiert, UsedMinutes unverändert (kein Backfill bei resume)
- [x] `src/Agent.Linux/Program.cs` — DI Registrierung IdleDetector
- [x] `tests/Agent.Core.Tests/AgentLinuxTests.cs` — Tracker freezes-while-idle, no-backfill-after-idle; Detector LockedHint/IdleHint/xprintidle/fail-open

## 2. Lock-Service — KDE / XFCE / xdg-screensaver

- [ ] `src/Agent.Linux/LinuxSessionLockService.cs` `attempts`-Array erweitern: `qdbus org.kde.screensaver /ScreenSaver Lock`, `xflock4`, `xdg-screensaver lock`
- [ ] `tests/Agent.Linux.Tests/` neu — Project anlegen (xUnit + NSubstitute), in Solution einhängen
- [ ] Test: loginctl success → kein Fallback aufgerufen
- [ ] Test: loginctl fail + KDE success → qdbus-Aufruf mit korrekten Args
- [ ] Test: alle Fallbacks fail → `InvalidOperationException` mit aussagekräftigem Detail

## 3. EF Migrations

- [ ] `src/Server.Infrastructure/Server.Infrastructure.csproj` — `Microsoft.EntityFrameworkCore.Design` PrivateAssets ergänzen
- [ ] `dotnet ef migrations add InitialCreate` (Schema inkl. `AgentVersion`/`LastPolicyVersion`)
- [ ] `src/Server.Infrastructure/DependencyInjection.cs` — `EnsureCreatedAsync` + manuelle ALTER raus, `MigrateAsync` rein
- [ ] Upgrade-Pfad: bestehende DB ohne `__EFMigrationsHistory` aber mit `Children` → Initial-Migration als applied markieren
- [ ] `tests/Server.Api.Tests/InfrastructureInitializationTests.cs` — Test fresh DB + legacy-DB-Upgrade
- [ ] `docs/operations.md` — Sektion "Schema-Upgrades laufen automatisch beim Container-Start"

## 4. CI / Release Publishing

- [ ] `.github/workflows/ci.yml` prüfen: bei Tag-Push (`v*`) wird .deb als GitHub Release Asset hochgeladen
- [ ] Falls fehlt: Step mit `softprops/action-gh-release` für `sessionguard-agent_*_amd64.deb`
- [ ] Server-Image-Publish auf GHCR bei Tags zusätzlich zu `main` verifiziert

## 5. Doku & Compose Schliff

- [ ] `docker-compose.yml` — Default `SESSIONGUARD_IMAGE_OWNER` auf echten GitHub-Owner setzen
- [ ] `docs/install-linux-agent.md` — Sektion "Idle-Verhalten" + `IdleThresholdSeconds`-Konfig
- [ ] `README.md` — MVP-Liste: aktive-Zeit-Tracking eintragen

## 6. Verification (End-to-End)

- [ ] `dotnet build SessionGuard.sln` grün
- [ ] `dotnet test` grün, neue Tests laufen
- [ ] Fresh-DB Smoke: leere SQLite → Container-Start → `__EFMigrationsHistory` korrekt befüllt
- [ ] Legacy-DB Smoke: heutiges Schema ohne History → Start ohne Doppel-Anlage
- [ ] Idle-Tracking VM-Test: Session lock → 2 Polls → `UsedMinutes` unverändert
- [ ] Lock-Fallback VM-Test: XFCE mit gemocktem loginctl-Fail → `xflock4`; KDE analog
- [ ] Release-Test: Tag `v0.2.0` push → Release enthält .deb Asset
- [ ] Compose smoke: `docker compose up -d` → `/healthz` = 200
