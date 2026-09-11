# CrashScope portfolio summary

Use this as source copy for a portfolio website, GitHub profile, resume project section, or project case study.

## Short title

**CrashScope — Local-first Windows crash diagnostics**

## One-line description

CrashScope is a low-overhead Windows diagnostics app that correlates live CPU/GPU/RAM telemetry with Windows crash, watchdog, and application evidence to help explain what happened around failures without inventing a root cause.

## Portfolio paragraph

I built CrashScope as a local-first diagnostics platform for gamers, hardware testers, overclockers, and local-AI users. A .NET 10 Agent collects low-overhead hardware telemetry and Windows diagnostic evidence, preserves telemetry around incidents, correlates evidence into structured reports, and persists sessions/incidents in SQLite. A React/TypeScript dashboard receives live telemetry over a bounded WebSocket stream without creating an additional hardware polling loop. The product includes conservative automatic workload detection, native Windows tray controls, per-user startup, bounded retention, privacy-safe support bundles, and a hardened validation/release pipeline. The project emphasizes evidence over unsupported root-cause claims, least-privilege Windows integration, vendor-neutral telemetry contracts, browser-safe localhost design, automated testing, measurable performance engineering, and reproducible Windows release engineering.

## Technical highlights

- C# / .NET 10 modular-monolith Agent
- ASP.NET Core localhost REST + WebSocket API
- React / TypeScript / Vite dashboard
- SQLite persistence with migrations and WAL
- LibreHardwareMonitor adapter behind CrashScope-owned telemetry interfaces
- Windows Event Log, WER, LiveKernel/watchdog, application fault/hang, display-driver, Kernel-Power and WHEA evidence
- realtime Event Log ingestion with sparse reconciliation fallback
- central 0.5 Hz background / 1 Hz active sampling clock
- 120-second rolling telemetry buffer and approximately -60/+30 second incident windows
- Everyday Mode with conservative Auto Assist plus manual workload override
- PID + process-start-time workload identity to protect against PID reuse
- event-driven process-exit monitoring with stale-observation race protection
- persistent environment snapshots for historical sessions
- bounded drop-stale WebSocket fanout that cannot backpressure the sampler
- safe user diagnostic markers for deterministic end-to-end incident validation
- evidence-role incident drill-down with Trigger / Corroborating / Context separation
- deterministic guidance structured as Observed → Meaning → What this does not prove → Next checks
- native Win32 tray shell with dashboard, Auto Assist, startup, and Exit controls
- event-driven tray recovery/notifications without a polling loop
- versioned atomic settings persistence and per-user HKCU Start with Windows
- bounded 30-day default retention with active/linked-history protection
- privacy-safe support-bundle preview/export with no automatic upload
- self-contained `win-x64` portable publishing with no .NET/Node install required for users
- automatic dashboard launch plus clean second-instance reuse
- browser-origin enforcement for localhost API/WebSocket access, CSP/security headers and no-store local diagnostic responses
- SHA-256 release checksums and build-provenance metadata
- bundled MIT/third-party notices in portable artifacts
- committed npm lockfile and deterministic `npm ci` frontend builds
- CI smoke-tests the exact published executable, version identity, loopback binding, security policy and bundled dashboard
- hardened validation-state preservation using durable external vaults, SHA-256 manifests, fail-closed recovery behavior, quarantine semantics, and independent restoration verification
- synthetic adversarial validation-state safety proof and Windows PowerShell wrapper-layout guard in CI
- **185 .NET automated tests passing, 0 failed**, across Core, Infrastructure and Agent projects in final reconciliation CI

## Measured performance

Primary reference system: Ryzen 5 7500F, Radeon RX 9070 XT, 32 GB DDR5, Windows 11.

Useful measurements:

- direct background sampler: ~0.0577% Agent CPU, ~76 MB working set
- direct active sampler: ~0.0578% Agent CPU, ~75 MB working set
- dashboard-open milestone: ~0.0682% Agent CPU, ~91 MB working set
- final integrated 30-second beta gate: **0.2657% average Agent CPU**, **93 MB average / 95.83 MB peak working set**, **35.08 MB average private memory**
- final integrated stream health: **0 stale-frame drops, 0 delivery misses**

The final integrated run remained within the current reference-machine product budget of ≤0.5% average Agent CPU and around ≤100 MB working set. These are development/reference-machine measurements rather than universal hardware guarantees.

## Validation proof

The final normal-user Windows combined beta gate passed on exact product head:

`9afb5418db9293a7fc9be0cc68be018ee4d89ff0`

Exact CI #393 / workflow run `34391659724` and exact artifact SHA-256:

`4e37376e7a26e59b82dcc30a38638d65fb425c9c4a653189895cca05c4b4d493`

The gate verified startup, loopback/browser security, second-instance behavior, workload attach/stop transition, incident capture, SQLite restart persistence, dashboard reconnect, settings, HKCU startup, Explorer tray registration, support-bundle privacy, retention behavior, and independent user-state restoration. Human tray-menu and incident-detail UX checks also passed.

Final reconciliation CI #407 passed **185 .NET tests**, the synthetic validation-state safety proof, Windows PowerShell wrapper guard, dashboard build, self-contained publish, and portable smoke validation.

## Product principles

- local-first and loopback-only
- browser-origin protection in addition to loopback binding
- no account, cloud backend, analytics, or automatic telemetry upload
- evidence-first diagnostic language
- process disappearance is not automatically classified as a crash
- missing hardware telemetry remains unavailable rather than being represented as zero
- slow dashboard clients cannot backpressure hardware sampling
- normal operation targets least privilege rather than Administrator access
- performance overhead is treated as a product requirement
- native WER report-store work remains read-only/on-demand research until real-machine evidence justifies promotion

## Suggested website feature bullets

- **Understand failures, not just temperatures.** Correlates system telemetry with Windows crash/watchdog evidence.
- **Designed not to disturb the workload.** Final integrated gate measured 0.2657% average Agent CPU on the primary reference PC, with lighter dashboard-only operation around 0.068%.
- **Local by design.** Data stays on the PC; the dashboard/API are loopback-only and browser-origin restricted.
- **Evidence before conclusions.** CrashScope reports what Windows and the telemetry actually observed instead of inventing a root cause.
- **Built like a real product.** 185 passing .NET tests, CI, SQLite migrations, release engineering, performance budgets, native Windows integration, security hardening, checksums and portable-binary smoke tests.

## Suggested website project card

**CrashScope**  
Windows diagnostics platform for games, GPU workloads, local AI, and unstable PCs. Correlates low-overhead hardware telemetry with Windows crash/watchdog evidence, preserves incident windows, and presents structured evidence in a local React dashboard.

**Stack:** C# · .NET 10 · ASP.NET Core · React · TypeScript · SQLite · WebSockets · Win32 · Windows diagnostics

**Proof points:** 185 passing .NET tests · 0.2657% average Agent CPU in the final integrated gate · zero stream drops/misses · self-contained Windows portable release pipeline · browser-hardened localhost-only architecture

## Suggested calls to action

When the repository and beta are public:

- **Download Windows beta**
- **View source on GitHub**
- **Read the engineering case study**

## Suggested case-study sections

1. **Problem** — Windows crash evidence and hardware telemetry are fragmented across tools.
2. **Constraint** — the diagnostic tool must not materially disturb the workload being measured.
3. **Architecture** — one central sampler, rolling incident buffer, Windows evidence sources, SQLite, localhost API and bounded WebSocket UI.
4. **Correctness** — PID + start-time identity, explicit unavailable metrics, evidence deduplication, source-time handling and cautious incident classification.
5. **Performance** — measured CPU/memory budgets and no extra dashboard hardware polling.
6. **Security** — loopback-only binding plus browser-origin restrictions, CSP/security headers and no cached diagnostic responses.
7. **Native Windows integration** — event-driven tray shell, per-user startup, Event Log handling and read-only WER research.
8. **Release engineering** — deterministic frontend dependencies, automated tests, self-contained Windows publish, executable smoke tests, provenance, checksums and state-safe real-machine validation.
9. **What I learned** — Windows diagnostics, async pipelines, persistence, performance measurement, localhost application security, release automation and designing software that distinguishes evidence from inference.

## Resume-sized version

**CrashScope — C#/.NET, React, TypeScript, SQLite, Windows diagnostics**  
Built a low-overhead local Windows crash-diagnostics platform that correlates CPU/GPU/RAM telemetry with Event Log/WER/LiveKernel evidence, tracks workloads with PID+start-time identity, persists structured sessions/incidents in SQLite, and streams live data to a React dashboard over bounded WebSockets. Added native Win32 tray/startup integration, conservative Auto Assist, retention controls, privacy-safe support bundles, and hardened state-safe validation. Final integrated reference-machine gate measured 0.2657% average Agent CPU with zero stream drops/misses; final CI passed 185 .NET tests plus release/validation guards.

## Interview talking points

- Why a modular monolith was a better fit than services for a local diagnostics tool.
- How one central sampling clock prevents dashboard/API consumers from multiplying hardware reads.
- Why a bounded drop-stale WebSocket channel protects the sampler from slow clients.
- Why PID alone is not a safe process identity on Windows.
- How event-driven process-exit monitoring avoids a high-frequency polling loop while still handling PID reuse/races.
- Why Windows Event Log observation time and the source incident time can differ.
- Why Kernel-Power 41, watchdog events and high VRAM usage are evidence, not automatic proof of root cause.
- Why loopback binding alone is not enough for a browser-served localhost application, and how Origin enforcement reduces browser-based cross-origin attacks.
- How native tray/startup functionality was implemented without Electron or a Windows Service.
- How a validation-state incident led to durable external safety vaults, manifest hashing, quarantine semantics, adversarial tests, and fail-closed restoration behavior.
- How CI launches the exact self-contained binary from outside its publish directory before a release artifact is accepted.
- How measured overhead changed architecture decisions rather than being treated as a cosmetic benchmark.
