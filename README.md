# CrashScope

**Local-first Windows crash diagnostics for games, GPU workloads, AI tools, and unstable PCs.**

CrashScope correlates low-overhead CPU/GPU/RAM telemetry with Windows diagnostic evidence so users can answer a practical question:

> **What happened around the moment my application, GPU driver, or PC failed?**

It is designed for gamers, PC enthusiasts, overclockers/undervolters, hardware testers, local-AI users, and anyone debugging an unstable Windows system.

## Quick links

- [Architecture](docs/architecture.md)
- [Performance budget](docs/performance-budget.md)
- [Hardware validation matrix](docs/hardware-validation.md)
- [Public beta validation checklist](docs/beta-validation-checklist.md)
- [One-command portable validation](docs/local-release-validation.md)
- [v1.1.0 release notes](docs/release-notes-v1.1.0.md)
- [v1.0.0 release notes](docs/release-notes-v1.0.0.md)
- [v0.1.0 release notes](docs/release-notes-v0.1.0.md)
- [Changelog](CHANGELOG.md)
- [Contributing](CONTRIBUTING.md)
- [Security policy](SECURITY.md)
- [Threat model](docs/threat-model.md)
- [Third-party notices](THIRD-PARTY-NOTICES.md)
- [Portfolio summary](docs/portfolio-summary.md)

## Why CrashScope

Most monitoring tools show graphs. Windows also records crash, watchdog, WER, display-driver, hang, shutdown, and hardware-error evidence, but those sources are fragmented and easy to misread.

CrashScope combines them into evidence-focused incident reports while deliberately avoiding unsupported claims about root cause.

Instead of saying:

> â€œVRAM caused your crash.â€

CrashScope is designed to say things like:

> â€œImmediately before the incident, GPU utilization was high, VRAM usage approached its observed peak, and Windows recorded a display-driver/watchdog event.â€

## Current capabilities

- Live CPU, per-logical-processor, RAM, GPU, VRAM, temperature, hotspot, clock, power, and fan telemetry where the hardware exposes it
- Central 0.5 Hz background / 1 Hz active sampler
- 120-second rolling in-memory telemetry buffer
- Incident capture window of approximately **60 seconds before** and **30 seconds after** a trigger
- Windows Event Log, Windows Error Reporting, LiveKernel/watchdog, display-driver, application-fault/hang, Kernel-Power, and WHEA evidence ingestion
- Evidence deduplication across repeated Windows reports
- Structured incident classification and cautious evidence-first assessments
- Persistent SQLite incident and workload-session history
- Workload discovery and PID + process-start-time identity tracking to protect against PID reuse
- Automatic active sampling while a selected game/application/workload is monitored
- Session environment snapshots including OS, CPU, GPU, driver, RAM, runtime, and CrashScope version
- React/TypeScript dashboard with live WebSocket telemetry
- Bounded, drop-stale WebSocket fanout so a slow browser cannot backpressure hardware sampling
- Safe user diagnostic markers for testing the full incident pipeline without deliberately crashing anything
- Incident drill-down with telemetry summary, process context, and trigger/corroborating/context evidence roles
- Generic process-isolated external evidence-provider architecture
- Optional bundled ConfigTrace 1.0.1 configuration-change evidence provider, disabled by default
- User-selected ConfigTrace root with workload-scoped sidecar lifecycle and bounded Context evidence correlation
- Safe user diagnostic markers that can include nearby provider Context evidence without changing marker classification
- Second-instance detection that reuses an already-running healthy CrashScope instance
- Normal portable launch opens the dashboard automatically; `--no-browser` supports automation/CI
- Localhost browser-origin protection against unrelated websites attempting API/WebSocket access
- Browser hardening headers and `no-store` caching for local diagnostic data/UI responses
- Localhost-only API/dashboard; no account, cloud backend, analytics, or automatic telemetry upload

## Measured overhead

Real-machine measurements on a Ryzen 5 7500F / Radeon RX 9070 XT development system:

| Scenario | Average Agent CPU | Working set |
| --- | ---: | ---: |
| Current background sampler baseline | **0.0577%** | **76.12 MB avg / 77.74 MB peak** |
| Current active sampler baseline | **0.0578%** | **75.31 MB avg / 75.87 MB peak** |
| SQLite background baseline | ~0.064% | ~75 MB |
| Dashboard open, live WebSocket connected | **~0.068%** | **~91 MB** |
| Frozen 1.0 final integrated gate | **0.2327%** | **91.36 MB avg / 94.41 MB peak** |

The current sampler baseline used 20-second measurement windows after a 3-second settle delay, normalized process CPU by 12 logical processors, sampled memory once per second, and recorded zero incidents during the run. Background sampling was 0.5 Hz and active sampling was 1 Hz.

During the separate dashboard-open validation:

- 1 live WebSocket subscriber
- 0 stale telemetry frames dropped
- 0 delivery misses
- no additional hardware polling loop created by the dashboard

The frozen 1.0 final integrated gate also recorded 0 stale-frame drops and 0 stream-delivery misses while validating the integrated product path. These are development-machine baselines rather than universal hardware guarantees. Performance is treated as a product requirement, with an Agent CPU budget of <= 0.5% and working-set budget of <= 100 MB.

## Architecture

CrashScope is a modular monolith rather than a collection of services:

```text
Hardware / Windows evidence
          â”‚
          â–¼
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚ CrashScope Agent (.NET 10) â”‚
â”‚                            â”‚
â”‚ central telemetry sampler  â”‚
â”‚ rolling incident buffer    â”‚
â”‚ process/session tracking   â”‚
â”‚ Windows evidence sources   â”‚
â”‚ incident correlation       â”‚
â”‚ SQLite persistence         â”‚
â”‚ localhost REST/WebSocket   â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
              â”‚ loopback only
              â–¼
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚ React + TypeScript UI      â”‚
â”‚ live telemetry             â”‚
â”‚ workload selection         â”‚
â”‚ sessions + incident detail â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

The dashboard consumes the same telemetry frames already produced by the central sampler. Opening the UI does **not** create another hardware-read loop.

More detail: [`docs/architecture.md`](docs/architecture.md) and [`docs/adr/`](docs/adr/).

## Hardware support

The telemetry architecture is provider-based and intentionally vendor-neutral.

Currently validated on the primary development system:

- AMD Ryzen 5 7500F
- AMD Radeon RX 9070 XT
- Windows 11

AMD GPU telemetry is working end-to-end. Some Ryzen temperature/power/clock readings are not exposed by the current least-privilege sensor path and are represented as unavailable rather than fake zeroes.

NVIDIA, Intel GPU, and Intel CPU validation are part of the public-beta hardware test roadmap. The core contracts are not coupled to LibreHardwareMonitor types.

See the maintained [hardware validation matrix](docs/hardware-validation.md) for the distinction between architecture readiness and real-machine validation.

## Windows release candidates

CrashScope now has locally validated **Windows x64 installer and portable release-candidate pipelines**. Public publication is still intentionally gated; these artifacts are not being presented as a public stable release yet.

### Installer

The installer is the recommended format for most Windows users once a release is published.

It:

- installs per-user under `%LOCALAPPDATA%\Programs\CrashScope`
- does not require Administrator privileges for normal installation or operation
- creates a Start Menu shortcut
- offers a desktop shortcut as an optional, unchecked choice
- does not install a Windows service
- does not automatically enable `Start with Windows`
- preserves CrashScope user data under `%LOCALAPPDATA%\CrashScope` during repair installs and uninstall
- includes the same self-contained application payload as the validated portable package

Installer candidates use the versioned filename:

```text
CrashScope-Setup-<version>.exe
```

### Portable

The portable ZIP remains supported for users who prefer not to install CrashScope.

The package is self-contained. End users do not need to install the .NET SDK, .NET runtime, Node.js, or a web server.

Portable candidates use:

```text
CrashScope-v<version>-win-x64.zip
```

Keep the complete extracted directory together rather than copying only `CrashScope.exe`.

### Verification

Published installer and portable artifacts should be accompanied by SHA-256 checksum files.

A Windows user can independently verify either artifact with:

```powershell
Get-FileHash .\CrashScope-Setup-*.exe -Algorithm SHA256
Get-FileHash .\CrashScope-v*-win-x64.zip -Algorithm SHA256
```

Do not run an artifact if its calculated checksum does not match the checksum published with that exact release.

### Release validation

CrashScope's local release engineering now proves the relevant product and packaging paths without relying on private GitHub Actions minutes:

1. deterministic dashboard dependency installation with `npm ci`
2. production dashboard build
3. .NET restore/build and the complete automated test suite
4. self-contained `win-x64` publish
5. license, third-party notice, and `BUILD-INFO.txt` provenance
6. packaged portable smoke validation
7. versioned portable ZIP and SHA-256 generation
8. versioned per-user Inno Setup installer and SHA-256 generation
9. installer lifecycle validation covering install, repair, shortcuts, HKCU registration, data preservation, startup non-interference, and uninstall
10. state-safe installed-runtime validation covering localhost security, second-instance behavior, workload attach/stop, safe incident capture, restart persistence, and verified restoration of pre-validation user state

The release workflow remains version-aware, and actual merge, version tag, release publication, repository migration, and public visibility are separate approval-gated operations.

Unsigned builds can trigger Windows SmartScreen reputation warnings. CrashScope should describe that accurately rather than instructing users to disable Windows security features.

## Development

### Requirements

- Windows
- .NET 10 SDK
- Node.js 24+

### Build the dashboard

The committed lockfile is authoritative:

```powershell
cd .\src\CrashScope.Dashboard
npm.cmd ci --no-audit --no-fund
npm.cmd run build
```

Using `npm.cmd` avoids PowerShell execution-policy interception by `npm.ps1` on systems where scripts are disabled.

### Build .NET

```powershell
cd ..\..
dotnet build CrashScope.sln -c Release
```

### Test

```powershell
dotnet test CrashScope.sln -c Release --no-build
```

CrashScope 1.1 currently contains **239 automated .NET tests** across Core, Infrastructure, and Agent projects.

### Run

```powershell
cd "$env:USERPROFILE\source\repos\CrashScope"
dotnet run --project .\src\CrashScope.Agent\CrashScope.Agent.csproj -c Release
```

CrashScope opens its local dashboard automatically after startup. For automation or terminal-only use:

```powershell
dotnet run --project .\src\CrashScope.Agent\CrashScope.Agent.csproj -c Release -- --no-browser
```

Dashboard/API origin:

```text
http://localhost:5077
```

## Repository layout

```text
src/
  CrashScope.Core              domain contracts and models
  CrashScope.Infrastructure    Windows, hardware and SQLite adapters
  CrashScope.Agent             runtime, API, sampling and incident pipeline
  CrashScope.Dashboard         React/TypeScript/Vite frontend

tests/
  CrashScope.Core.Tests
  CrashScope.Infrastructure.Tests
  CrashScope.Agent.Tests

scripts/
  Validate-PortableCandidate.ps1
  Audit-PublicHistory.ps1
  Test-PublicSnapshotPrivacy.ps1

research/
  TelemetrySpike
  WindowsEventSpike

docs/
  adr/
  architecture.md
  beta-validation-checklist.md
  hardware-validation.md
  local-release-validation.md
  performance-budget.md
  publication-runbook.md
  release-notes-v0.1.0.md
  release-plan.md
  research-findings.md
  threat-model.md
```

## Privacy and security

CrashScope is local-first by design:

- listens on loopback only (`localhost`, `127.0.0.1`, `::1`)
- browser-originated requests are restricted to CrashScope's own loopback origin
- API/dashboard responses use `Cache-Control: no-store`
- browser responses include CSP, frame denial, `nosniff`, referrer, permissions, COOP, and same-origin resource policies
- native local tooling without a browser `Origin` header remains supported
- no mandatory account
- no cloud backend
- no analytics
- no automatic diagnostic upload
- no external network dependency is required for normal runtime use
- persistent data lives under `%LOCALAPPDATA%\CrashScope`
- raw crash dumps and machine-specific diagnostic exports are excluded from source control
- normal operation is designed for least privilege rather than Administrator access

CI additionally rejects tracked dump/database/build-output patterns before a change can merge.

Loopback is a security boundary against LAN exposure, not against already-running local processes under the user's account. See [SECURITY.md](SECURITY.md) and the [threat model](docs/threat-model.md) for the documented boundaries.

## Diagnostic philosophy

CrashScope separates **observation** from **causation**.

A process disappearing is not automatically called a crash. Kernel-Power 41 is evidence of an abnormal transition, not proof of why it happened. A watchdog event can support a driver/GPU-instability hypothesis without proving the component-level root cause.

This distinction is deliberate and central to the project.

## Roadmap

CrashScope 1.1 local release validation is complete. The exact release runtime is frozen at the fully validated `e51943f6b7aecaf98223ef30cbec10ef94c1eba1` boundary.

Near-term milestones:

- complete the coordinated v1.0.0 / v1.1.0 GitHub and portfolio publication sweep
- validate NVIDIA and newer Intel GPU/CPU paths on additional real machines
- add richer incident/session comparison views
- evaluate GapTrace as a future optional process-isolated evidence provider after 1.1
- optional code signing

AI-generated root-cause speculation is intentionally **not** an MVP dependency; structured local evidence comes first.
## Contributing

Contributions should preserve CrashScope's evidence-first, local-first, least-privilege, and low-overhead design. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

CrashScope is distributed under the [MIT License](LICENSE). See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for major bundled/open-source dependencies.
