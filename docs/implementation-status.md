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

## 2. Lock-Service — Freedesktop / XFCE / xdg-screensaver ✅

- [x] `src/Agent.Linux/LinuxSessionLockService.cs` `attempts`-Array erweitert: `dbus-send … org.freedesktop.ScreenSaver.Lock` (deckt Plasma 5/6 + Mate + andere ab), `xflock4`, `xdg-screensaver lock`. Empty-Args-Pfad ohne trailing space.
- [x] Tests in bestehender `tests/Agent.Core.Tests/AgentLinuxTests.cs` (kein neues Projekt nötig — Solution-Split nicht gerechtfertigt)
- [x] Test: loginctl success → kein Fallback aufgerufen (existing)
- [x] Test: Freedesktop ScreenSaver Fallback nach Cinnamon+GNOME fail
- [x] Test: XFCE xflock4 Fallback nach screensavers fail
- [x] Test: xdg-screensaver als letzter Fallback, Reihenfolge via `Received.InOrder` verifiziert
- [x] Test: alle Fallbacks fail → `InvalidOperationException` (existing)

**Follow-up (außerhalb dieser Slice):** logind `LockedHint` flippt nicht zuverlässig wenn Lock via externem Fallback erfolgt → IdleDetector könnte Session weiter als aktiv lesen. Mitigation: tracker hat eigene Lock-Awareness durch coordinator state, nicht kritisch im MVP.

## 3. EF Migrations ✅

- [x] `src/Server.Infrastructure/Server.Infrastructure.csproj` + `src/Server.Ui/Server.Ui.csproj` — `Microsoft.EntityFrameworkCore.Design` PrivateAssets ergänzt
- [x] `dotnet ef migrations add InitialCreate` — Schema inkl. `AgentVersion`/`LastPolicyVersion`/Indizes
- [x] `src/Server.Infrastructure/DependencyInjection.cs` — `EnsureCreatedAsync` + Patch-Helpers raus, `MigrateAsync` rein
- [x] Upgrade-Pfad: bestehende DB (alle drei Tabellen vorhanden, kein `__EFMigrationsHistory`) → fehlende Spalten ergänzt, alle Migrations als applied gestempelt; in BEGIN/COMMIT-Transaktion mit Re-Check; INSERT-OR-IGNORE auf PK-Konflikt; ALTER tolerant gegen `duplicate column`
- [x] `tests/Server.Api.Tests/InfrastructureInitializationTests.cs` — Concurrent legacy upgrade, fresh DB, legacy stamp + data preserve, idempotent re-run
- [x] `docs/operations.md` — Sektion "Schema upgrades" ergänzt

## 4. CI / Release Publishing ✅

- [x] `.github/workflows/ci.yml` `publish` job: bei Tag-Push (`v*`) wird .deb-Artifact gedownloaded und via `softprops/action-gh-release@v2` als GitHub Release Asset attached, mit auto-generated release notes
- [x] `contents: write` Permission im publish-Job gesetzt
- [x] Server-Image-Publish auf GHCR bei main + Tags (existierend, verifiziert)

## 5. Doku & Compose Schliff ✅

- [x] `docker-compose.yml` — Default `SESSIONGUARD_IMAGE_OWNER` auf `bac83` (Repo-Owner) gesetzt
- [x] `docs/install-linux-agent.md` — Idle-Verhalten + `IdleThresholdSeconds` + erweiterte Lock-Fallbacks dokumentiert
- [x] `README.md` — MVP-Liste: aktive-Zeit-Tracking + Multi-Desktop Lock-Fallbacks eingetragen

## 6. Verification (End-to-End)

- [ ] `dotnet build SessionGuard.sln` grün
- [ ] `dotnet test` grün, neue Tests laufen
- [ ] Fresh-DB Smoke: leere SQLite → Container-Start → `__EFMigrationsHistory` korrekt befüllt
- [ ] Legacy-DB Smoke: heutiges Schema ohne History → Start ohne Doppel-Anlage
- [ ] Idle-Tracking VM-Test: Session lock → 2 Polls → `UsedMinutes` unverändert
- [ ] Lock-Fallback VM-Test: XFCE mit gemocktem loginctl-Fail → `xflock4`; KDE analog
- [ ] Release-Test: Tag `v0.2.0` push → Release enthält .deb Asset
- [ ] Compose smoke: `docker compose up -d` → `/healthz` = 200
