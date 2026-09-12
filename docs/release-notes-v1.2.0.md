# CrashScope 1.2.0 release notes

CrashScope 1.2.0 adds a native Windows desktop application shell while preserving the local-first, evidence-first, least-privilege, and low-overhead Agent architecture.

Public publication remains intentionally deferred until the explicit publication checkpoint. No push, tag, GitHub Release, or GitHub Actions run is implied by this local release seal.

## Native desktop shell

- WPF hosts the existing React dashboard through WebView2.
- The Agent remains the lightweight background/runtime process.
- The Desktop process exists only while the native UI is open.
- Closing the Desktop window unloads the WebView2 UI while leaving the Agent running.
- Duplicate Desktop launches signal and activate the existing window rather than creating another shell.
- Normal launch, tray Open CrashScope, and existing-instance behavior prefer the Desktop shell and retain browser fallback.
- Navigation remains restricted to CrashScope's loopback dashboard origin.

## Windows integration

- CrashScope now uses a recognizable multi-resolution application icon.
- The Desktop window uses native dark caption integration.
- Portable packages contain the Desktop payload under `desktop\\`.
- Installer shortcuts continue to target `CrashScope.exe` so the Agent lifecycle remains authoritative, while the Desktop executable supplies the shortcut icon.
- The installer remains per-user and non-admin for normal use.

## State-safety hardening

CrashScope's installed-validation safety helper now carries the original product-data root in its durable context. Vault completion re-verifies the restored path/length/SHA-256 manifest before deleting the preservation vault. Repeated completion is accepted only after the restored state has been independently verified.

The installed-validator safety test includes a synthetic runtime proof covering protect, isolated validation state, verified restore, first completion, repeated completion, and final manifest equality.

## Validation

- **266/266 .NET tests passed**: Core 30, Infrastructure 37, Agent 179, Desktop 20.
- Production preview packaging, portable smoke, installer build, and installer contract passed.
- Real installed 1.1.0 -> 1.2 production-preview upgrade passed.
- Start Menu and Desktop shortcuts passed target/icon/working-directory validation.
- Native Desktop launch, true single-instance behavior, dark title bar, embedded dashboard, tray icon, and close-window-keeps-Agent behavior passed.
- Upgrade and uninstall preserved CrashScope user data.
- Upgrade/uninstall preserved unrelated startup state and removed only the exact CrashScope-owned startup value.
- Recovery consensus verified that the restored user-data tree contained no 1.2G marker, every file predated 1.2G, and the tree exactly matched the separately preserved R6 snapshot.

## Hardware-validation scope

The strongest real-hardware validation remains the Ryzen 5 7500F / Radeon RX 9070 XT Windows 11 development system plus the existing Windows 10 / Intel Core i5-3210M / Intel HD Graphics 4000 second-PC validation. NVIDIA real-hardware validation remains outstanding and is not claimed.

## Signing

The local 1.2.0 installer is unsigned. Windows SmartScreen reputation warnings may occur. Self-signing would not create public reputation trust, and CrashScope does not instruct users to disable SmartScreen or other Windows security controls.

## Final local artifact seal

- Binary release/tag target: `PENDING_RELEASE_COMMIT`
- Portable ZIP SHA-256: `PENDING_PORTABLE_SHA256`
- Installer SHA-256: `PENDING_INSTALLER_SHA256`
- Published Agent SHA-256: `PENDING_AGENT_SHA256`
- Published Desktop SHA-256: `PENDING_DESKTOP_SHA256`
- Automated .NET tests: **266/266 PASS**

The binary/tag target is intentionally the clean runtime commit used to build these artifacts. A later documentation-only seal commit may record the resulting hashes without changing the binary release boundary.
