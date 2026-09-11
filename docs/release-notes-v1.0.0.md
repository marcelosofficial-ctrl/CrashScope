# CrashScope 1.0.0 release notes

CrashScope 1.0.0 is the frozen first stable local release line. Public publication is intentionally coordinated with CrashScope 1.1.0 and the surrounding portfolio work; this file records the validated 1.0 boundary without claiming that publication has already occurred.

## Frozen source

- Source commit: `10c5769364068619f026e609a3c221d2665ede57`
- Version: `1.0.0`
- Portable ZIP SHA-256: `3a0a61969830e824b5c8d3d828b730a0e878b81679df6b8625897840c39560bb`
- Installer SHA-256: `8574e45f1ce649cfd78748bc42044ef5e9b79a7fa738fc862bb657d51dd52abc`
- Automated .NET tests: **185 passed, 0 failed**

The historical `v1.0.0` tag must point to that exact source commit. The frozen commit is not amended or rewritten by 1.1 development.

## Highlights

CrashScope 1.0.0 provides a local-first Windows crash-diagnostics workflow for games, GPU workloads, AI tools, and unstable PCs:

- low-overhead CPU, RAM, GPU, VRAM, temperature, clock, power, and fan telemetry when exposed by the hardware provider;
- a central 0.5 Hz background / 1 Hz active sampler and approximately 120 seconds of rolling telemetry;
- Windows Event Log, WER, watchdog/LiveKernel, display-driver, application-fault/hang, Kernel-Power, and WHEA evidence;
- evidence-first incident reports with Trigger, Corroborating, and Context roles;
- SQLite incident/session persistence and process identity protection against PID reuse;
- a React/TypeScript dashboard served only on loopback;
- safe user diagnostic markers;
- privacy-safe support-bundle export;
- per-user Start with Windows support controlled by the user;
- a self-contained Windows x64 portable package and per-user Inno Setup installer;
- normal operation without Administrator privileges, a Windows service, an account, analytics, or automatic cloud upload.

CrashScope deliberately separates observation from causation. A process disappearing is not automatically labeled a crash, Kernel-Power 41 is not presented as root-cause proof, and correlated telemetry or Windows evidence is not overstated.

## Final 1.0 validation

The frozen 1.0 build passed the complete local release-validation sequence, including installer lifecycle, state restoration, localhost security, second-instance behavior, workload attach/stop, persistence, safe incident capture, and UTF-8/UI validation.

Final main-PC performance on the Ryzen 5 7500F / Radeon RX 9070 XT / Windows 11 reference system:

- average Agent CPU: **0.2327%**
- average working set: **91.36 MB**
- peak working set: **94.41 MB**
- private memory: **34.28 MB**
- stale WebSocket frames: **0**
- stream delivery misses: **0**

A genuine second-PC validation also passed on Windows 10 Home with an Intel Core i5-3210M, Intel HD Graphics 4000, and approximately 8 GB of RAM. That machine had neither the .NET SDK/runtime command nor Node.js installed, confirming the self-contained package path. The final second-PC visual check also confirmed clean UTF-8 rendering and clean unavailable-metric fallbacks.

## Packaging and security notes

- Installer AppId remains stable across upgrades: `{08FC71E2-02C3-4A5A-B5EA-F9302EADE31A}`.
- Default installer location: `%LOCALAPPDATA%\Programs\CrashScope`.
- User data under `%LOCALAPPDATA%\CrashScope` is preserved by installer repair/uninstall semantics.
- CrashScope listens only on loopback and enforces browser-origin restrictions for its local UI/API.
- The installer is unsigned; Windows SmartScreen reputation warnings may occur.
- Code signing is not implemented in 1.0.0.