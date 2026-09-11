# CrashScope v0.1.0 — first public beta

CrashScope v0.1.0 is the first portable Windows beta of the local-first, evidence-first crash-diagnostics platform.

## What this beta does

CrashScope runs a low-overhead Windows Agent and correlates CPU/GPU/RAM telemetry with Windows diagnostic evidence around failures. It includes a local React dashboard, automatic and manual workload monitoring, persistent sessions/incidents, native tray controls, bounded local retention, and privacy-safe support bundles.

The core design goal is not to invent a confident root cause. CrashScope preserves and organizes what was actually observed, separates evidence from inference, and shows useful next checks without claiming unsupported causality.

## Highlights

- self-contained Windows x64 portable build — no .NET or Node installation required
- normal `CrashScope.exe` launch starts the Agent and opens the local dashboard automatically
- second launch reuses the already-running healthy instance instead of racing for the localhost port
- `--no-browser` mode for automation, startup, and terminal-only use
- Everyday Mode with conservative Auto Assist enabled by default
- low-frequency foreground recognition plus explicit manual workload override
- 0.5 Hz background / 1 Hz active central telemetry sampling with one hardware-read loop
- CPU, system memory, GPU, VRAM, hotspot, temperature, clocks, power, and fan telemetry where exposed by the hardware provider
- unavailable sensors are represented as unavailable/null rather than fake zeroes
- 120-second rolling telemetry buffer with approximately 60 seconds pre-trigger and 30 seconds post-trigger incident capture
- realtime Windows Event Log ingestion with sparse reconciliation fallback
- Windows Event Log / WER / LiveKernel / display-driver / application fault & hang / Kernel-Power / WHEA evidence paths
- workload discovery with PID + process-start-time identity protection
- event-driven active-process exit monitoring with stale-observation race protection
- persistent SQLite sessions, environment snapshots, incidents, and evidence
- live WebSocket dashboard telemetry without an extra hardware-polling loop
- safe diagnostic markers to validate the incident pipeline without deliberately crashing anything
- evidence-first incident guidance organized as Observed → Meaning → What this does not prove → Next checks
- visible incident-detail scroll/focus behavior with reduced-motion support
- native Win32 notification-area shell with dashboard, Auto Assist, Start with Windows, and Exit controls
- event-driven tray recovery/notification behavior without a polling loop
- optional per-user Start with Windows through HKCU using `--no-browser`
- versioned persistent settings with atomic writes and migration support
- bounded history retention, 30 days by default and configurable from 1–365 days
- startup-only retention pruning that preserves active/linked history
- privacy-safe support-bundle preview/export with explicit local download only and no automatic upload
- support bundles exclude the raw SQLite DB, dump bytes, ETL/raw WER files, arbitrary logs, and raw settings
- localhost-only network binding plus browser-Origin enforcement against unrelated websites attempting local API/WebSocket access
- restrictive browser security headers and `no-store` caching for local diagnostic/UI responses
- deterministic dashboard dependency installation from a committed npm lockfile
- package SHA-256, build provenance, MIT license, and third-party notices
- **185 .NET automated tests** passing in the final reconciled CI gate, plus synthetic validation-state safety and Windows PowerShell wrapper-layout guards

## Final validation

The final normal-user combined beta gate passed on the primary Windows machine using exact validated product head:

`9afb5418db9293a7fc9be0cc68be018ee4d89ff0`

Exact CI workflow run:

`34391659724` (CI #393)

Exact validated CI package SHA-256:

`4e37376e7a26e59b82dcc30a38638d65fb425c9c4a653189895cca05c4b4d493`

The automated gate verified:

- healthy normal-user startup
- loopback-only listeners on `127.0.0.1` and `::1`
- browser Origin/security-header policy
- second-instance process safety
- workload attach and Active → Background transition
- safe incident capture and SQLite persistence across restart
- dashboard WebSocket reconnect
- zero stale-frame drops and zero stream-delivery misses
- settings defaults/range/persistence
- exact HKCU Start with Windows behavior and disable cleanup
- Explorer notification-icon registration
- support-bundle structure/runtime metadata/privacy scan
- retention configuration/startup maintenance
- independently verified restoration of pre-validation user state
- no unresolved validation vault, leftover CrashScope process, or port 5077 listener after cleanup

The two remaining human UX checks also passed: the tray menu was readable/usable and selecting/capturing an incident correctly scrolled/focused Incident detail. Issue #72 is closed as completed.

## Validated performance

Primary reference machine:

- Windows 11
- AMD Ryzen 5 7500F
- AMD Radeon RX 9070 XT
- 32 GB DDR5

Final integrated 30-second Agent sample:

- average Agent CPU: **0.2657%**
- average working set: **93 MB**
- peak working set: **95.83 MB**
- average private memory: **35.08 MB**
- stale stream frames dropped: **0**
- delivery misses: **0**

A separate lighter dashboard-open milestone measured approximately 0.068% Agent CPU and 91 MB working set. Direct sampler baselines measured approximately 0.058% CPU and 75–76 MB working set.

CrashScope's current reference-machine budget remains ≤0.5% average Agent CPU and around ≤100 MB working set. These are development/reference-machine measurements, not universal hardware guarantees.

## Validated hardware

Primary real-machine validation:

- Windows 11
- AMD Ryzen 5 7500F
- AMD Radeon RX 9070 XT

Radeon GPU telemetry is validated end-to-end. Some Ryzen temperature/power/clock readings are unavailable through the current least-privilege sensor path and are intentionally represented as unavailable rather than zero.

NVIDIA, Intel GPU, and Intel CPU validation is still in progress. The architecture is vendor-neutral, but v0.1.0 does not claim comprehensive real-hardware validation for those platforms yet.

## Native WER status

CrashScope includes a read-only, on-demand native Windows Error Reporting report-store evaluation path. During the final normal-user gate, the native comparison exceeded its 30-second validation budget.

That timeout is intentionally non-blocking research evidence. Native WER is **not** promoted into continuous/live incident triggering in v0.1.0. Existing evidence sources remain authoritative until separate real-machine coverage and timing evidence justify a future promotion decision.

## Privacy / networking

- loopback-only listeners (`localhost`, `127.0.0.1`, `::1`)
- browser-originated API/WebSocket requests restricted to CrashScope's own loopback origins
- local diagnostic/UI responses marked `no-store`
- no account
- no mandatory cloud backend
- no analytics
- no automatic diagnostic upload
- no external runtime network dependency required for normal operation
- persistent data lives under `%LOCALAPPDATA%\CrashScope`
- support bundle creation is explicit and local

The localhost API is intended for the current user and local tooling. It is not an isolation boundary against arbitrary native processes already running with the same Windows user's permissions. See the repository threat model for details.

## Release verification

The release workflow builds the dashboard, runs the complete test suite, publishes a self-contained Windows application, launches the exact published executable, and verifies:

- reported CrashScope version
- packaged dashboard and release notices
- loopback-only listener addresses
- expected browser security headers
- trusted localhost browser Origin accepted
- unrelated external browser Origin rejected
- second-instance process safety
- `/api/status` health

The same tested publish output is then compressed, SHA-256 hashed, uploaded as the release artifact, and attached to the GitHub prerelease when an intentional `v*` tag is pushed.

## Download and verification

Download the Windows x64 portable ZIP and its matching `.sha256` file from the GitHub Release.

Verify the ZIP in PowerShell:

```powershell
Get-FileHash .\CrashScope-v0.1.0-win-x64.zip -Algorithm SHA256
```

Compare the resulting hash with the value in the published `.sha256` file before extracting.

Then extract the entire ZIP and run:

```text
CrashScope.exe
```

The dashboard should open automatically. If needed, open:

```text
http://localhost:5077
```

Keep the extracted files together; the portable package includes the self-contained runtime, local dashboard assets, license/third-party notices, and build provenance.

## Known limitations

- early beta binary is unsigned and may trigger Windows SmartScreen reputation warnings
- current strongest real-hardware validation is AMD Radeon RX 9070 XT / Ryzen 5 7500F / Windows 11
- NVIDIA/Intel real-hardware validation remains a beta roadmap item
- CPU sensor availability depends on the underlying least-privilege hardware-provider path
- incident correlation is evidence-focused and is not a guarantee of component-level root cause
- native WER report-store ingestion remains research-only/read-only and is not a live trigger
- no installer yet; the first beta is intentionally portable so distribution can be proven before installer complexity is added
- an unrelated application already occupying localhost port 5077 can prevent CrashScope from starting; improved port-conflict messaging remains a polish item

## Feedback requested

The most useful beta reports include:

- CrashScope version
- CPU/GPU model
- Windows version
- GPU driver version
- which telemetry metrics appear or remain unavailable
- whether Auto Assist recognized the workload correctly
- whether manual workload attach/stop works
- whether safe diagnostic marker capture completes and persists across restart
- tray/startup behavior
- observed Agent CPU / memory use if measured
- whether antivirus or SmartScreen interfered with launch

Please avoid attaching personal crash dumps, private paths, credentials, WER archives, or unrelated sensitive machine diagnostics to public issues.
