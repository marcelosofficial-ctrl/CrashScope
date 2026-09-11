# CrashScope Engineering Handoff

Updated: 2026-09-10

This file is the repository-owned engineering continuity checkpoint for CrashScope. Read it together with `docs/project-state.md` and `docs/validation-log.md` before making release or architecture changes.

## Project identity

- Repository: `marcelosofficial-ctrl/CrashScope`
- Product: local-first, privacy-first, evidence-first Windows crash diagnostics and workload observation for gamers, hardware testers, benchmarks, local AI, and GPU-heavy workloads.
- Architecture: modular monolith with one lightweight Agent; the browser dashboard is a viewer/control surface, not the telemetry engine.
- Platform targets: AMD/NVIDIA GPU and AMD/Intel CPU.
- Product invariant: **CrashScope should never make the workload it is measuring less trustworthy.**
- Localhost only on port 5077. No LAN listener, cloud account, analytics backend, Windows Service, Electron shell, or normal Administrator requirement.
- Performance design: one hardware read per sampling tick; approximately 0.5 Hz background and 1 Hz active; no UI-driven hardware polling loops and no intentional GPU/VRAM load.
- Evidence model: Trigger / Corroborating / Context. Process exit alone is not automatically a crash. Missing sensors remain unavailable/null rather than fake zeroes.
- Guidance model: Observed -> Meaning -> What this does not prove -> Next checks.
- Persistence: SQLite via Microsoft.Data.Sqlite, schema v3.
- Runtime: .NET 10 LTS, ASP.NET Core 10, React 19 / TypeScript / Vite, WebSockets, native Win32 tray.

## Current release posture

- `main`: `9b7e30a3563482bc997eef00386e84171646e2cc`
- active integration branch: `integration/next-beta`
- PR #59: open from `integration/next-beta` to `main`, mergeable at last review
- exact product head validated on the primary Windows machine: `9afb5418db9293a7fc9be0cc68be018ee4d89ff0`
- exact product-head CI: #393 / workflow run `34391659724`: PASS
- exact validated product artifact SHA256: `4e37376e7a26e59b82dcc30a38638d65fb425c9c4a653189895cca05c4b4d493`
- history/documentation reconciliation commit: `36331bc6cbb2bf4b3b6e13599fce790741c9826e`
- reconciliation CI #407 / workflow run `34396464945`: PASS
- audit-helper head `b8a0fa895dc27d4de4fa5d2b7860cae3da912463`: CI #421 PASS
- Issue #72: CLOSED / completed
- Issue #42: CLOSED / superseded by #72
- Issue #56: CLOSED / completed
- Issue #61: CLOSED / completed

Do **not** merge PR #59 to `main`, create `v0.1.0`, publish a release, or change repository visibility without an explicit release/publication decision.

## Integrated v0.1.0 product work

- Everyday Mode and conservative Auto Assist
- event-driven active-process exit monitoring and stale-observation hardening
- realtime Windows Event Log ingestion with sparse reconciliation
- evidence-first incident guidance
- SQLite persistence and bounded retention
- versioned persistent settings with atomic writes and migration
- per-user Start with Windows through HKCU using `--no-browser`
- lightweight native Win32 tray with dashboard, Auto Assist, startup, and Exit controls
- event-driven tray notifications and TaskbarCreated recovery
- privacy-safe support-bundle preview/export
- support runtime metadata with persistence/settings schema and retention-policy context
- startup-only retention pruning with active/linked-history protection
- on-demand, read-only native Windows Error Reporting report-store evaluation
- dashboard/browser/executable branding
- hardened validation-state preservation using durable external vaults, SHA-256 manifests, fail-closed orphan/legacy detection, quarantine semantics, and independently verified restoration
- synthetic adversarial validation-state safety proof in CI
- Windows PowerShell compatibility guard for the validation wrapper/core split
- deterministic dashboard dependency installation through the committed npm lockfile
- self-contained Windows x64 publishing with provenance, license, third-party notices, and SHA-256 packaging

Native WER is intentionally not part of live incident triggering until separate real-machine evidence justifies promotion.

## Final automated next-beta gate — PASS

Validated exact product head:

`9afb5418db9293a7fc9be0cc68be018ee4d89ff0`

Exact CI:

- CI #393
- workflow run `34391659724`
- PASS

Exact artifact SHA256:

`4e37376e7a26e59b82dcc30a38638d65fb425c9c4a653189895cca05c4b4d493`

Verified:

- CrashScope 0.1.0 healthy normal-user startup
- loopback-only `::1:5077` and `127.0.0.1:5077`
- browser Origin/security-header policy
- second-instance process safety
- workload attach and Active -> Background transition
- safe incident capture
- SQLite persistence across restart
- dashboard WebSocket reconnect
- stale-frame drops: 0
- delivery misses: 0
- persistence schema 3
- settings schema/default/range/persistence
- exact HKCU Start with Windows behavior and disable cleanup
- Explorer notification-icon registration
- privacy-safe support-bundle structure/runtime metadata/identity scan
- retention configuration/startup maintenance
- validation-state restoration independently verified
- no unresolved preservation vault, leftover CrashScope process, or listener on port 5077 after cleanup

## Final human UX gate — PASS

The primary Windows-machine review confirmed:

1. the tray right-click menu is readable and usable
2. selecting/capturing an incident scrolls/focuses Incident detail correctly

Issue #72 is closed as completed.

## Automated coverage

Post-reconciliation CI #407 passed:

- Core tests: 20
- Agent tests: 128
- Infrastructure tests: 37
- total .NET tests: **185 passed, 0 failed**
- synthetic validation-state safety proof: PASS
- Windows PowerShell wrapper-layout guard: PASS
- production dashboard build: PASS
- self-contained Windows publish: PASS
- portable executable smoke validation: PASS

## Performance

Primary reference machine: Windows 11, Ryzen 5 7500F, Radeon RX 9070 XT, 32 GB DDR5.

Final integrated 30-second Agent sample:

- average CPU: **0.2657%**
- average working set: **93 MB**
- peak working set: **95.83 MB**
- average private memory: **35.08 MB**
- stale-frame drops: 0
- delivery misses: 0

Useful historical baselines:

- direct background sampler: ~0.0577% CPU, ~76 MB working set
- direct active sampler: ~0.0578% CPU, ~75 MB working set
- dashboard-open milestone: ~0.0682% CPU, ~91 MB working set

Current reference-machine release budget: <=0.5% average Agent CPU and around <=100 MB working set. These are reference-machine measurements, not universal guarantees.

## Validation harness safety

The release gate uncovered a state-preservation weakness in earlier validation tooling. That harness issue was fixed before final validation.

Current safeguards include:

- state preservation outside disposable working directories
- SHA-256 manifests
- fail-closed handling of unresolved prior backups
- unique quarantine paths for test-created state
- restoration verification independent of validator success
- adversarial synthetic CI coverage for interrupted runs, tampered backups, legacy backup detection, and originally-empty state

The final combined gate verified clean post-run state restoration.

## Native WER status

Native WER report-store access remains:

- read-only
- on-demand
- metadata-only
- excluded from live triggering

The final combined gate recorded a timeout after the 30-second research budget. This was correctly non-blocking and `promotionEligible = false`. Issue #55 remains open for separate research and real-machine evidence.

## Public launch / privacy status

Issue #39 is the authoritative public-repository launch gate.

Completed checks:

- primary-machine validation complete
- exact CI artifact validation complete
- release workflow reviewed; merging `main` does not automatically publish
- intentional `v*` tag rebuilds/tests/packages and creates a GitHub prerelease
- `.gitignore` covers dotenv files, build outputs, CrashScope research telemetry/raw folders, dumps, ETL, sysdata, PFX/publish settings, and related local artifacts
- CI rejects tracked dump/database/generated artifact patterns
- SECURITY.md accurately describes loopback/browser-Origin boundaries and same-user native-process limitations
- hardware-validation and release wording do not overclaim NVIDIA/Intel validation
- MIT license present
- full local history audit found zero high-risk credential/key signatures, zero sensitive historical filenames, and zero forbidden current tracked artifacts
- seven review-only pattern matches were classified as synthetic security/privacy test fixtures or documentation
- release notes, CHANGELOG, project state, README/performance docs, CONTRIBUTING, and portfolio summary are current

Remaining public-launch decisions/work:

- historical Git author/committer metadata contains a personal non-noreply email; resolve by explicit acceptance, history rewrite, or a clean public snapshot/mirror strategy before public visibility
- capture sanitized official screenshots (#37)
- create/publish the intentional `v0.1.0` prerelease only after explicit approval
- finalize public repository topics/metadata
- change repository visibility only after explicit approval
- pin the repository and update portfolio links only after stable public URLs exist

The repository is currently private.

## Genuine remaining roadmap

Keep open:

- #35 validate the exact GitHub Release ZIP on a second Windows PC
- #36 installer/code-signing path after #35
- #37 sanitized official screenshots
- #39 public repository launch gate
- #55 native WER report-store research

NVIDIA GPU, Intel GPU, and Intel CPU real-hardware validation remain beta-roadmap work. Architecture readiness is not equivalent to real-machine validation.

## Release workflow

`.github/workflows/release.yml` supports manual dispatch, `release-candidate/**` branch builds, and intentional `v*` tag builds.

For a `v*` tag it:

1. builds the dashboard deterministically
2. restores/builds/tests .NET
3. publishes self-contained win-x64
4. adds provenance/license/notices
5. smoke-tests the exact published executable
6. packages ZIP + SHA-256
7. uploads the tested package
8. creates a GitHub prerelease

Merging PR #59 alone does not publish a release.

## Immediate engineering actions

Do not rerun the expensive combined product validator unless runtime/product code changes.

Next actions:

1. resolve the public commit-email strategy
2. capture sanitized official screenshots
3. explicitly decide whether to merge PR #59 into private `main`
4. explicitly decide whether to create/tag `v0.1.0`
5. keep #35/#36/#55 as post-release roadmap work

## Continuity rule

Before significant release or architecture work:

1. read this file
2. read `docs/project-state.md`
3. review the latest entries in `docs/validation-log.md`
4. refresh live GitHub state for `main`, `integration/next-beta`, PR #59, #39, #37, #35, #36, and #55

Do not repeat completed expensive validation unless product code changes invalidate the recorded evidence.