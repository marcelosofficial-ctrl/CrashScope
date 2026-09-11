# Changelog

All notable changes to CrashScope are documented here.

CrashScope follows semantic versioning for public releases. Early `0.x` releases may still contain breaking changes while the public beta matures.

## [Unreleased]

### Added

- Version-aware local release-candidate builder with clean-tree, provenance, portable smoke, ZIP, and SHA-256 gates.
- Per-user Windows installer built with Inno Setup 7 for x64-compatible Windows systems.
- Version-aware local installer builder with product-source and installer-source provenance plus SHA-256 output.
- Installer regression contract covering least privilege, install location, optional shortcuts, user-data preservation, startup non-interference, and absence of Windows-service installation.
- Automated installer lifecycle validation covering fresh installation, exact payload verification, same-version repair, Start Menu behavior, per-user HKCU registration, user-data preservation, and clean uninstall.
- State-safe installed-candidate validation covering installed-runtime health, loopback-only binding, browser security, second-instance protection, workload attach/stop, safe incident capture, restart persistence, uninstall, and independently verified restoration of pre-validation CrashScope state.

### Changed

- Release engineering can now produce and validate both portable and installed Windows candidates locally without consuming private GitHub Actions minutes.
- Stable-release preparation now treats installer and portable distribution as parallel supported Windows delivery formats.
- Release documentation distinguishes completed installer engineering from remaining code-signing, broader hardware-validation, exact-candidate validation, and publication work.
- Local stable-release source metadata is deliberately stamped `1.0.0`; this does not create a Git tag or publish a release.
- Installer uninstall cleanup removes only the exact app-owned `Run\CrashScope` startup command and preserves different or unrelated startup entries.

## [0.1.0] - 2026-09-10

### Added

- Local-first Windows Agent built on .NET 10 and ASP.NET Core.
- React/TypeScript/Vite dashboard served by the Agent on localhost.
- Everyday Mode with conservative Auto Assist enabled by default.
- Central low-overhead telemetry sampler with 0.5 Hz background and 1 Hz active modes.
- Rolling 120-second telemetry buffer and incident capture window of approximately 60 seconds before / 30 seconds after a trigger.
- CPU, RAM, GPU, VRAM, temperature, hotspot, clock, power, and fan telemetry when exposed by the current hardware provider.
- Explicit unavailable/null telemetry instead of fake zeroes when a sensor path is missing.
- Realtime Windows Event Log ingestion with sparse reconciliation fallback.
- Windows diagnostic evidence ingestion for application faults/hangs, Windows Error Reporting, LiveKernel/watchdog evidence, display-driver events, Kernel-Power, and WHEA.
- Diagnostic evidence deduplication and source-time handling for watchdog reports.
- Structured evidence-first incident reports with cautious classification and guidance organized as Observed ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Meaning ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ What this does not prove ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Next checks.
- SQLite persistence for incident reports, sessions, and environment snapshots.
- Workload discovery, PID + process-start-time identity tracking, active session monitoring, and automatic sampling-mode switching.
- Event-driven active-process exit monitoring and stale-observation race protection.
- Session environment snapshots containing OS, CPU, GPU, driver, RAM, runtime, and CrashScope version information.
- Live WebSocket telemetry with bounded drop-stale delivery so UI clients cannot backpressure the hardware sampler.
- Safe user diagnostic markers for end-to-end incident validation without deliberately crashing a process or writing fake Windows errors.
- Incident drill-down UI including telemetry summary, process context, evidence roles, and scroll/focus behavior.
- Second-instance detection that reuses an already-running healthy CrashScope instance.
- Automatic dashboard opening for normal portable launches and `--no-browser` automation/startup mode.
- Native Win32 tray shell with dashboard, Auto Assist, Start with Windows, and Exit controls.
- Event-driven tray incident notifications and `TaskbarCreated` recovery without a polling loop.
- Versioned persistent settings with atomic writes, migration support, Auto Assist control, and retention configuration.
- Per-user HKCU Start with Windows support using the exact portable executable command plus `--no-browser`.
- Bounded 30-day default retention, configurable from 1ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Å“365 days, with startup-only pruning and active/linked-history protection.
- Privacy-safe support-bundle preview/export with explicit local download only and no automatic upload.
- Support bundles exclude raw SQLite databases, dump bytes, ETL/raw WER files, arbitrary logs, and raw settings files.
- Localhost browser-Origin validation for HTTP/WebSocket traffic in addition to loopback-only binding.
- Browser hardening headers including CSP, frame denial, `nosniff`, referrer/permissions, COOP and same-origin resource policies.
- `Cache-Control: no-store` / `Pragma: no-cache` for local UI and diagnostic responses.
- Self-contained `win-x64` portable publishing that does not require end users to install .NET or Node.
- Committed npm dependency lockfile and deterministic `npm ci` dashboard builds.
- Project license, third-party notices, and `BUILD-INFO.txt` provenance in release packages.
- CI repository-hygiene gate for dumps/databases/generated artifacts.
- Hardened validator user-state preservation with durable external vaults, SHA-256 manifests, fail-closed orphan/legacy detection, quarantine semantics, and independently verified restoration.
- Synthetic adversarial validation-state safety proof and Windows PowerShell wrapper-layout guard in CI.
- One-command normal-user combined next-beta validation with durable transcript, nested output, and consolidated JSON evidence.
- Repository-backed project-state, validation-log, and authoritative handoff documentation for continuity across development sessions.
- Tag-driven GitHub prerelease workflow for portable ZIP + SHA-256 checksum generation.

### Final validation

The final combined normal-user Windows gate passed on exact validated product head `9afb5418db9293a7fc9be0cc68be018ee4d89ff0` / CI #393.

Verified behavior included:

- exact CI package provenance/checksum
- healthy normal-user startup
- loopback-only listeners
- browser Origin/security headers
- second-instance safety
- workload attach and Active ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Background transition
- safe incident capture and SQLite restart persistence
- dashboard reconnect
- settings/default/range/persistence behavior
- exact HKCU startup behavior
- Explorer tray registration
- privacy-safe support bundle
- retention configuration/startup maintenance
- independently verified user-state restoration
- zero stale stream drops and zero delivery misses

The final human tray-menu and incident-detail UX checks also passed. Issue #72 is closed as completed.

### Automated test coverage

Final reconciled CI #407 passed with:

- Core: 20 tests
- Agent: 128 tests
- Infrastructure: 37 tests
- total: **185 .NET tests passed, 0 failed**
- synthetic validation-state safety proof: PASS
- Windows PowerShell wrapper-layout guard: PASS
- production dashboard build: PASS
- self-contained Windows publish and portable smoke validation: PASS

### Validated performance

On the primary Ryzen 5 7500F / Radeon RX 9070 XT Windows 11 development system:

- direct sampler background Agent CPU: ~0.0577%
- direct sampler active Agent CPU: ~0.0578%
- dashboard-open Agent CPU: ~0.0682%
- final integrated 30-second gate Agent CPU: **0.2657%**
- final integrated average working set: **93 MB**
- final integrated peak working set: **95.83 MB**
- final integrated average private memory: **35.08 MB**
- stale WebSocket frames dropped: **0**
- WebSocket delivery misses: **0**

These are development/reference-machine measurements rather than universal hardware guarantees. The current release budget remains <=0.5% average Agent CPU and around <=100 MB working set on the reference machine.

### Known limitations

- Current real-hardware validation is strongest on AMD Radeon RX 9070 XT / Ryzen 5 7500F / Windows 11.
- Some Ryzen temperature, power, and clock values are not available through the current least-privilege sensor path and are explicitly reported as unavailable rather than zero.
- NVIDIA, Intel GPU, and Intel CPU support remain architecture-ready until validated on real machines.
- Native WER report-store ingestion remains read-only/on-demand research and is not a live incident trigger; the final normal-user comparison exceeded its 30-second research budget.
- Early portable beta builds are unsigned and may trigger Windows SmartScreen reputation warnings.
- No installer yet; v0.1.0 is intentionally portable-first.
- CrashScope reports correlated evidence and does not claim definitive component-level root cause when Windows evidence cannot support it.
