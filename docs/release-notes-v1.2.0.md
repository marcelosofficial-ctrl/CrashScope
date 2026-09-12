# CrashScope 1.2.0 release notes

CrashScope 1.2.0 adds a native Windows desktop application shell while preserving the local-first, evidence-first, least-privilege, and low-overhead Agent architecture.

CrashScope 1.2.0 is publicly released at [GitHub Releases](https://github.com/marcelosofficial-ctrl/CrashScope/releases/tag/v1.2.0). The published installer, portable ZIP, and checksum file are the exact artifacts sealed during local release validation.

## Public publication provenance

- Public runtime/tag commit: `8ce9c25dc40f6481bf7b782d3dae67deeb3e6cef`
- Public documentation/main commit at publication: `097790d6c700fc4fe3e32e080bc0200b08a5bc1f`
- Exact shared runtime Git tree: `b23d4e4e8f79598362d8d9cc9d1a405e989ee3c6`
- Public `v1.2.0` remains on the runtime boundary.
- Publication used direct validated artifact upload; no public GitHub Actions runner jobs were required.

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
- Portable packages contain the Desktop payload under `desktop\`.
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

## Final release artifact seal

- Binary release/tag target: `3a6a3ffd9ad40943e7e5fc8be4d8faf0fc7b9912`
- Portable ZIP SHA-256: `5cd5821800b2e5f2c4ace319a6921267414465129c704b8e50c83b1a1a932b04`
- Installer SHA-256: `a37c012293a1c5e5aa94c823f1898a85ef0bc896b5b3cf03d870e8191050a12e`
- Published Agent SHA-256: `db0deb3234def23a2b4b95e34b841904a189b8625b7ef3f9a5ed6969ee522244`
- Published Desktop SHA-256: `c19401015f81ea66d53e9b25e096b3bff3ce7bda5aec7ba740da8ec4fad5974e`
- Automated .NET tests: **266/266 PASS**

The binary release boundary remains the validated private runtime commit above. Public `v1.2.0` points to a privacy-safe public commit with the exact same runtime Git tree; later documentation-only commits do not move the tag or replace the published binaries.
