# CrashScope engineering case study

## Overview

CrashScope is a local-first Windows diagnostics application built around a narrower question than a traditional sensor dashboard:

> What did the system actually observe around the moment a game, GPU workload, application, driver, or PC failed?

The project combines low-overhead hardware telemetry, Windows diagnostic evidence, workload identity, persistent incident history and deterministic guidance. It deliberately avoids treating correlation as proof of root cause.

## Product constraints

The architecture is driven by explicit product constraints:

1. **Low overhead is a release requirement.** CrashScope should never make the workload it is measuring less trustworthy.
2. **Local first.** No account, cloud backend, analytics or automatic diagnostic upload.
3. **Least privilege.** Normal operation should not require Administrator rights.
4. **Evidence before causation.** A process exit, watchdog, Kernel-Power event or high utilization must not become an unsupported root-cause claim.
5. **One hardware sampling path.** Dashboard clients consume already-sampled telemetry and never create a second poller.
6. **Vendor-neutral core.** Provider-specific types do not leak into Core contracts.
7. **Missing data stays missing.** Unavailable sensors are never represented as fake zeros.
8. **Historical evidence remains interpretable.** Sessions preserve environment context such as OS, hardware, driver and CrashScope version.

## Architecture

CrashScope is a modular monolith built with .NET 10, ASP.NET Core, React, TypeScript, SQLite and WebSockets.

```text
Windows / hardware
       │
       ├── telemetry providers
       ├── Windows Event Log / WER evidence
       └── process identity / lifetime
       │
       ▼
CrashScope Agent
       │
       ├── one central sampler
       ├── rolling incident buffer
       ├── workload/session manager
       ├── incident coordinator
       ├── SQLite persistence
       ├── deterministic guidance
       ├── settings + support-export services
       └── loopback REST / WebSocket
       │
       ▼
React dashboard
```

A single lightweight Agent owns hardware polling and diagnostic state. The browser is only a viewer and control surface.

## One central sampling clock

Current telemetry rates are:

- Background: 0.5 Hz
- Active workload: 1 Hz

Each tick performs one provider hardware read. The resulting frame fans out to the rolling buffer, active-session aggregation and WebSocket subscribers.

Opening the dashboard therefore does not create another hardware polling loop.

## Protecting the sampler from the UI

WebSocket delivery uses bounded stale-frame behavior. If a browser cannot keep up, an obsolete UI frame may be discarded rather than allowing rendering latency to backpressure hardware sampling.

In the validated dashboard-open run:

- average Agent CPU: ~0.0682%
- average working set: ~91.09 MB
- one WebSocket subscriber
- zero stale-frame drops
- zero delivery misses

The performance target on the Ryzen 5 7500F reference system is no more than 0.5% average Agent CPU, with a stretch target of 0.25%, roughly 100 MB or less working set, no sustained GPU use and effectively zero intentional VRAM consumption.

## Automatic Everyday Mode

CrashScope should be useful without requiring users to manually choose every game.

Auto Assist therefore observes only the current foreground process at a low cadence and reuses the existing workload classifier. Automatic attach requires:

- a Recommended classification
- score at or above the conservative threshold
- a non-general, non-helper workload
- the same PID + process-start-time identity observed twice

Manual monitoring always wins. Manual Stop applies a cooldown so automation does not immediately override the user.

Auto Assist state can be changed at runtime and persists across restart. Disabling it stops future automatic attaches without interrupting the currently monitored session.

## Event-driven process lifetime

The normal active-session path no longer probes a PID every second.

Once CrashScope safely attaches to a process, it waits asynchronously for that process to exit. Sparse probing remains only as a fallback for restricted or unsupported process handles.

The completion path is identity-guarded so a stale observation for an earlier session cannot terminate a newer replacement session.

This preserves the core rule that PID alone is never workload identity.

## Event-driven Windows diagnostics

Frequent full Event Log traversal is also avoided on the normal supported path.

CrashScope subscribes only to relevant Windows providers with `EventLogWatcher`, stores matching events in a bounded 2,048-event in-memory ring and wakes the incident pipeline when relevant evidence arrives.

A sparse reconciliation scan remains for sleep/resume, missed-event safety and environments where subscriptions are restricted or unavailable.

The same in-memory evidence ring is reused by the Windows diagnostic artifact layer to avoid duplicate normal Event Log traversal.

This change added no Administrator requirement, broad always-on ETW session or hardware polling loop.

## Rolling incident evidence

CrashScope keeps approximately a 120-second rolling telemetry buffer and preserves about:

- 60 seconds before an incident
- 30 seconds after it

The safe `UserDiagnosticMarker` feature exercises this complete pipeline without intentionally crashing software or creating fake Windows failures.

## Evidence semantics

CrashScope separates evidence into:

- Trigger
- Corroborating
- Context

It also keeps source time, artifact time, observation time and incident time conceptually distinct because Windows diagnostic systems do not guarantee those timestamps are identical.

WER duplicates are not collapsed on a broad signature alone. Report identity, artifact path and signature context contribute to deduplication so unrelated failures are not silently merged.

## Process exit is not automatically a crash

A monitored application can exit because the user closed it, it completed normally, it was terminated, it restarted, or it crashed.

Session-end reason is therefore separate from incident classification. A process disappearing is not enough to create a confident crash conclusion.

## Explicit unavailable telemetry

A missing sensor is not zero.

Metrics preserve unavailable and invalid states so missing CPU temperature, power, clocks or vendor-specific GPU sensors cannot become false `0 °C`, `0 W` or `0 MHz` evidence.

This matters both for the live dashboard and for historical incident interpretation.

## SQLite persistence

CrashScope persists structured data with Microsoft.Data.Sqlite and direct SQL.

Persisted domains include:

- incidents
- workload sessions
- session/incident associations
- environment snapshots

The database uses WAL, `synchronous=NORMAL`, foreign keys and a busy timeout. Historical environment snapshots allow an old incident to be interpreted after driver, OS or hardware changes.

Human-facing Windows environment snapshots prefer the Windows product caption when available so Windows 11 is not displayed as a misleading low-level Windows 10 compatibility version string.

## Actionable but cautious guidance

Incident detail is organized as:

1. Observed
2. Meaning
3. What this does not prove
4. Next checks

Guidance is deterministic rather than AI-generated.

For a graphics/watchdog incident, CrashScope can recommend checking recent driver changes, temporarily returning GPU tuning to stock and comparing a second demanding workload. It does not jump from a watchdog to “your GPU is defective.”

For Kernel-Power, CrashScope states that Windows observed an abnormal power/state transition but does not automatically blame the PSU.

## Privacy-safe support bundles

A user can preview and download a small sanitized bundle for one incident.

Curated entries include:

- README.txt
- manifest.json
- incident.json
- session.json when related
- runtime.json

The default bundle excludes:

- the SQLite database
- crash dumps
- ETL traces
- raw WER files/folders
- arbitrary logs
- arbitrary environment variables
- full executable paths
- automatic uploads

Defense-in-depth text redaction covers the current profile path, username, machine name, email, IPv4/IPv6, MAC addresses and obvious credential-style values. The UI still tells users to preview before sharing because arbitrary diagnostic text cannot honestly be guaranteed risk-free.

The archive is produced in memory for explicit browser download and is not persisted server-side by CrashScope.

## Localhost security lesson

Loopback binding alone is not sufficient for a browser-served localhost application because a hostile remote webpage can attempt requests to localhost.

CrashScope therefore combines loopback-only listeners with browser-Origin validation and restrictive response headers.

Trusted browser origins are limited to CrashScope's own localhost, IPv4 loopback and IPv6 loopback URLs on port 5077. Requests without an Origin remain available for native local tools such as PowerShell.

CI verifies trusted Origin acceptance, hostile Origin rejection, loopback-only listeners, security headers and the absence of a LAN listener against the published application.

## Portable release engineering

The Windows build is treated as a reproducible product artifact rather than a hand-copied `dotnet run` folder.

CI:

1. restores the dashboard with `npm ci`
2. builds the production frontend
3. builds .NET with warnings treated as errors
4. runs the automated tests
5. publishes self-contained `win-x64`
6. includes license, third-party notices and build provenance
7. launches the exact published executable from outside its own directory
8. validates API identity and dashboard serving
9. checks loopback-only networking and browser-origin security
10. verifies second-instance behavior
11. packages the exact tested files
12. calculates SHA-256
13. uploads the exact tested artifact

A separate one-command validator downloads the exact CI artifact for a PR, checks its SHA and runs the real-machine hardware/security/performance gate. The user PC is therefore a hardware-validation machine rather than a build server.

## Startup architecture

CrashScope has a tested per-user startup foundation using only:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

The startup command uses the current executable path plus `--no-browser`. This avoids Administrator rights, a Windows service and an unnecessary browser renderer at logon.

The user-facing startup toggle is intentionally kept separate until the complete behavior is composed and validated.

## What the project demonstrates

CrashScope's complexity comes from real engineering constraints rather than framework quantity:

- Windows systems programming
- hardware/provider abstraction
- asynchronous producer/consumer design
- process identity correctness and PID reuse
- event-driven diagnostics with bounded reconciliation
- evidence modeling and uncertainty
- SQLite persistence/migrations
- browser/WebSocket integration
- localhost web security
- privacy-aware export design
- performance measurement
- reproducible CI/release artifacts
- safe runtime settings
- UX that prioritizes useful interpretation over fake certainty

## Current validation direction

Frozen `main` remains the hardened v0.1.0 release-candidate line. Post-v0.1 work is integrated and tested on `integration/next-beta` until the release decision and consolidated real-machine gate are complete.

Before the public repository/release gate, CrashScope still needs broader real hardware validation, especially NVIDIA and Intel systems, plus the deliberate Git-history privacy decision documented elsewhere in the project.

The project can become more sophisticated later, but low overhead, evidence quality, least privilege and privacy remain higher priorities than feature quantity or AI-generated root-cause speculation.
