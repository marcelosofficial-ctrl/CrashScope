# CrashScope project state

This file is a repository-owned durable handoff. Use it with `docs/AUTHORITATIVE_HANDOFF.md` and `docs/validation-log.md` so work can continue across chat/session boundaries without reconstructing state from memory.

Last updated: 2026-09-10

## Release posture

- Frozen historical v0.1 candidate commit: `7018a285181f9f2594b93503df29013589929ea6`
- `main` currently points to documentation commit `9b7e30a3563482bc997eef00386e84171646e2cc`.
- Active integration branch: `integration/next-beta`.
- Exact product head validated on the primary Windows machine: `9afb5418db9293a7fc9be0cc68be018ee4d89ff0`.
- Exact product-head CI: #393 / workflow run `34391659724`, PASS.
- The histories were later reconciled on integration through merge commit `36331bc6cbb2bf4b3b6e13599fce790741c9826e` without changing runtime product code.
- Reconciliation CI: #407 / workflow run `34396464945`, PASS.
- PR #59 (`main` <- `integration/next-beta`) is the integration PR and is mergeable after reconciliation.
- Issue #72 is CLOSED / completed.
- Do **not** merge PR #59 into `main`, create a `v0.1.0` tag, or publish the prerelease without explicit user approval.

Subsequent documentation-only commits may advance `integration/next-beta` after the validated product head. Do not repeat expensive product validation merely because documentation/bookkeeping changed.

## Product principles

CrashScope is a local-first, privacy-first, evidence-first Windows crash diagnostics application for games, GPU workloads, local-AI workloads, benchmarks, renderers, and unstable PCs.

Core rule:

> CrashScope should never make the workload it is measuring less trustworthy.

Important invariants:

- one lightweight Agent, browser viewer only
- localhost-only API/dashboard
- no cloud backend, analytics, account requirement, or automatic upload
- no normal Administrator requirement
- no Electron shell or Windows service for the beta architecture
- central 0.5 Hz background / 1 Hz active telemetry sampler
- no feature may introduce a second hardware polling loop
- unavailable sensors are explicit/null, never fake zeroes
- process exit alone is not automatically called a crash
- evidence roles remain Trigger / Corroborating / Context
- guidance remains evidence-first and avoids unsupported causal claims

## Integrated v0.1.0 / next-beta product work

`integration/next-beta` includes:

- Everyday Mode and conservative Auto Assist
- event-driven active-process exit monitoring and stale-observation race protection
- realtime Windows Event Log ingestion plus sparse fallback reconciliation
- evidence-first incident guidance
- SQLite persistence and retention controls
- versioned persistent settings with Auto Assist and retention policy
- per-user Start with Windows through HKCU and `--no-browser`
- lightweight native Win32 notification-area shell
- event-driven tray incident notifications and `TaskbarCreated` recovery
- privacy-safe support-bundle preview and explicit local export
- support runtime metadata with persistence/settings schemas and retention policy
- bounded startup-only retention pruning with active/linked-history protection
- on-demand read-only native Windows Error Reporting report-store comparison
- refreshed public-beta, troubleshooting, architecture, research, release, and portfolio documentation
- CrashScope dashboard/browser/executable branding
- hardened validator state preservation using durable external vaults, SHA-256 manifests, fail-closed orphan/legacy detection, quarantine semantics, and independently verified restoration
- synthetic adversarial validation-state safety proof wired into CI
- Windows PowerShell compatibility wrapper for the hardened wrapper/core split
- repository-backed authoritative handoff checkpoint

Native WER is intentionally not wired into live incident triggering until separate real-machine evidence justifies promotion.

## Final next-beta validation — COMPLETE

Issue #72 was the authoritative combined next-beta real-machine gate and is now closed.

The final normal-user Windows run on exact product head `9afb5418db9293a7fc9be0cc68be018ee4d89ff0` / CI #393 **PASSED**.

Exact validated artifact SHA-256:

`4e37376e7a26e59b82dcc30a38638d65fb425c9c4a653189895cca05c4b4d493`

Automated evidence:

- exact CI artifact provenance/checksum verified
- healthy CrashScope 0.1.0 startup
- loopback-only listeners verified
- browser Origin/security-header policy verified
- second-instance process safety verified
- workload attach and Active -> Background transition verified
- safe incident marker captured: `953beea4-7a4b-4826-a995-fcc9429e4dc3`
- SQLite restart persistence verified
- dashboard reconnect verified with 1 subscriber
- stream dropped stale frames: 0
- stream delivery misses: 0
- settings schema/default/range/persistence checks passed
- exact HKCU Start with Windows command and disable cleanup verified
- Explorer tray icon registration verified
- support-bundle curated structure/runtime metadata/privacy scan passed
- automatic upload remained false
- retention configuration/startup maintenance checks passed
- native WER comparison timed out after the 30-second validation budget and was correctly recorded as non-blocking; live triggering unchanged; promotion eligible false
- hardened user-state restoration independently verified
- no unresolved preservation vault, leftover CrashScope process, or listener remained after cleanup

Human UX evidence also passed:

- tray right-click compact menu readable and usable
- selecting/capturing an incident scrolls/focuses Incident detail into view

## Current CI / automated coverage

Reconciliation CI #407 passed with:

- Core tests: 20 passed
- Agent tests: 128 passed
- Infrastructure tests: 37 passed
- total .NET tests: **185 passed, 0 failed**
- synthetic validation-state safety proof: PASS
- Windows PowerShell wrapper-layout guard: PASS
- dashboard production build: PASS
- self-contained Windows publish: PASS
- portable application smoke test: PASS

## Performance evidence

Primary reference machine:

- Windows 11
- AMD Ryzen 5 7500F
- AMD Radeon RX 9070 XT
- 32 GB DDR5

Final integrated 30-second Agent sample:

- average CPU: **0.2657%**
- average working set: **93 MB**
- peak working set: **95.83 MB**
- average private memory: **35.08 MB**
- stale-frame drops: 0
- delivery misses: 0

Historical useful baselines:

- direct sampler background: ~0.0577% CPU, ~76 MB working set
- direct sampler active: ~0.0578% CPU, ~75 MB working set
- dashboard-open milestone: ~0.0682% CPU, ~91 MB working set

The final integrated gate remains within the current <=0.5% average CPU / around <=100 MB working-set release budget. These are development/reference-machine measurements, not universal guarantees.

## Validation-state incident status

The earlier missing September 9 ~21:00 runtime DB delta is no longer being pursued by default.

Safety-net recovery:

- two genuine pre-incident VSS copies were recovered
- both are 65,536 bytes
- DB LastWriteTime: 2026-09-08 23:00:13 local
- SHA256: `9B9F60CE1D0393872D4498A6812F2C517013FF696FBD83EFE9E17137DE4FE5D3`
- both are hash-verified on a separate physical USB HDD under `F:\CrashScope-Recovery\...`

The preservation bug is addressed at the harness level. The synthetic adversarial safety proof passed in CI and locally on the primary machine. The final combined run independently verified restoration and left the pre-run absent user-data/startup state unchanged.

Do not restart deleted-file recovery unless the user explicitly asks.

## Release workflow

`.github/workflows/release.yml` supports:

- manual workflow dispatch
- `release-candidate/**` branch builds
- intentional `v*` tag builds
- deterministic dashboard build
- full .NET test suite
- self-contained `win-x64` publish
- provenance/license/notices
- portable smoke validation
- ZIP + SHA-256 generation
- artifact upload
- GitHub **prerelease** creation only for `v*` tags

Merging PR #59 by itself does **not** publish a release.

## Genuine remaining roadmap after release decision

These are not blockers for the completed primary-machine gate unless explicitly promoted to release blockers:

- #35 validate the exact GitHub Release ZIP on a second Windows PC
- #37 capture sanitized official portfolio/README screenshots
- #39 final public-repository launch/privacy/history review before changing visibility or promoting the repo publicly
- #36 installer and code-signing path after the portable beta is proven externally
- #55 native WER report-store evaluation/promotion research

NVIDIA GPU, Intel GPU, and Intel CPU real-hardware validation remain public-beta roadmap work. Do not overclaim support beyond architecture readiness until real-machine evidence exists.

## Immediate next action

The primary-machine release gate is complete and histories are reconciled. PR #59 is technically ready for an explicit repository/release decision.

Only with explicit user approval:

1. merge PR #59 into `main`
2. decide whether to create the `v0.1.0` tag immediately or keep `main` merged while doing another pre-release review
3. pushing `v0.1.0` will trigger the release workflow, which rebuilds/tests/packages the tag and creates a GitHub prerelease

Do not merge/tag/publish automatically.

## Continuity policy

Do not rely on chat transcripts as the sole record of important engineering milestones.

After meaningful changes, update:

- `docs/AUTHORITATIVE_HANDOFF.md`
- `docs/project-state.md`
- `docs/validation-log.md`

The Windows PowerShell validator preserves:

- durable transcript: `%TEMP%\CrashScope-next-beta-validation-transcript.txt`
- nested validator output: `%TEMP%\CrashScope-next-beta-validation-child-output.txt`
- consolidated JSON: `%TEMP%\CrashScope-next-beta-validation\next-beta-validation.json`
