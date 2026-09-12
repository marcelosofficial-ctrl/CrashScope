# Changelog

All notable changes to CrashScope are documented here.

CrashScope follows semantic versioning for public releases. Early `0.x` releases may still contain breaking changes while the public beta matures.

## [Unreleased]

### Planned

- Additional NVIDIA and newer Intel GPU/CPU real-hardware validation.
- Richer incident/session comparison views.
- Evaluation of GapTrace as a future optional external evidence provider.
- Optional code signing.

## [1.2.0] - 2026-09-12

### Added

- Native WPF + WebView2 desktop shell that renders the existing localhost dashboard inside a normal Windows application window.
- True Desktop single-instance activation: duplicate UI launches activate the existing window instead of creating another shell.
- Multi-resolution CrashScope application icon and native dark-window caption integration.
- Desktop project and Desktop test project in the solution.

### Changed

- Normal launches, tray Open CrashScope, and second-launch behavior now prefer the native desktop shell while retaining browser fallback.
- The Agent remains the low-overhead always-on component; closing the Desktop shell unloads WebView2 while the Agent continues running.
- Portable packaging now includes the Desktop shell under `desktop\\` and installer shortcuts retain the Agent launch target while using the Desktop icon.
- Production packaging carries numeric 1.2.0.0 file/version metadata and closes both Agent and Desktop processes during installer upgrades.
- Validation-state completion now re-verifies restored product data before deleting its durable vault and safely tolerates repeated completion only after that verification.

### Validation

- **266/266 .NET tests passed**: Core 30, Infrastructure 37, Agent 179, Desktop 20.
- Native installed-shell validation passed: shortcut launch, dark title bar, embedded dashboard, tray icon, true single-instance behavior, and closing the Desktop window while the Agent remains healthy.
- Exact production preview upgrade from 1.1.0 passed with user data and unrelated startup state preserved.
- Production uninstall preserved user data and unrelated startup state while removing the exact owned startup value.
- Recovery consensus proved the restored live user-data tree predates the 1.2G validation run and exactly matches the separately verified R6 safety snapshot.
- Final 1.2.0 portable and installer artifact hashes are recorded in the 1.2.0 release notes after the frozen runtime commit is built.

### Known release note

- The local installer remains unsigned; Windows SmartScreen reputation warnings may occur. CrashScope does not instruct users to disable Windows security features.

## [1.1.0] - 2026-09-12

### Added

- Generic process-isolated external evidence-provider architecture.
- ConfigTrace 1.0.1 as the first bundled external evidence provider.
- Settings schema v3 fields for an opt-in ConfigTrace root and enabled state.
- Dashboard controls for saving one absolute ConfigTrace root and enabling/disabling the provider.
- Workload-scoped ConfigTrace sidecar lifecycle with the provider OFF by default.
- Bounded provider evidence correlation into incidents as Context evidence.
- Provider Context evidence in user-requested safe diagnostic markers.
- Portable and installer packaging for `providers\ConfigTrace\configtrace.exe` and its MIT license.
- ConfigTrace source/version/hash provenance in packaged `BUILD-INFO.txt`.

### Changed

- Manual diagnostic markers now use the same provider-evidence mapping rules as live incidents.
- Release packages pin ConfigTrace 1.0.1 commit `b629c970dfc14fca5df1e0ef2b0d1d07d0d8c56c` and EXE SHA-256 `fe1c470a58402e82e97ee529c6a6b02822430da70e65ffc5fc5a71359ad4e521`.
- Automated .NET test count increased to **239**.

### Security

- ConfigTrace 1.0.1 fixes sensitive-key recognition for camelCase/PascalCase names such as `apiToken`, `accessToken`, `clientSecret`, and `sessionId`.
- End-to-end validation confirms plaintext test secrets are absent from both ConfigTrace journals and CrashScope marker JSON.

### Validation

- 239/239 .NET tests passed.
- Real runtime validation observed a semantic configuration change, one ConfigTrace Context evidence item, and correct sidecar start/stop behavior.
- ConfigTrace remains optional and disabled by default.
- Correlation wording remains explicitly non-causal.
- True installed **1.0.0 -> 1.1.0** upgrade/state-preservation validation passed, including schema v2 -> v3 migration, persisted-incident survival, startup preservation, uninstall cleanup, and exact restoration of the original user state.
- Final ConfigTrace-OFF reference measurement: **0.2365%** average Agent CPU, **91.71 MB** average / **94.58 MB** peak working set, 0 stale frames, and 0 stream-delivery misses.
- Final ConfigTrace-ON active-workload measurement: CrashScope **0.1966%** average CPU / **97.31 MB** peak working set; ConfigTrace **0%** measured average CPU / **4.99 MB** peak working set; 0 stale frames, 0 delivery misses, and no plaintext test secret in the provider journal.
- Genuine non-elevated second-PC validation passed on Windows 10 / Intel Core i5-3210M / Intel HD Graphics 4000, followed by a manual visual PASS.

## [1.0.0] - 2026-09-11

### Added

- Stable self-contained Windows x64 portable package and per-user Inno Setup installer.
- Installer lifecycle and installed-runtime validation with durable user-state preservation.
- Final dashboard/sidebar UX polish and UTF-8 rendering repair.
- Genuine second-PC validation on Windows 10 / Intel Core i5-3210M / Intel HD Graphics 4000.

### Validation

- Frozen source commit: `10c5769364068619f026e609a3c221d2665ede57`.
- 185/185 .NET tests passed.
- Final reference-system Agent CPU: **0.2327%** average.
- Final working set: **91.36 MB average / 94.41 MB peak**.
- Portable SHA-256: `3a0a61969830e824b5c8d3d828b730a0e878b81679df6b8625897840c39560bb`.
- Installer SHA-256: `8574e45f1ce649cfd78748bc42044ef5e9b79a7fa738fc862bb657d51dd52abc`.
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
