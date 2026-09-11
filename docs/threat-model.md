# Threat Model

CrashScope may handle sensitive local information such as application names, executable paths, hardware information, Windows diagnostic events, crash metadata, environment snapshots, and optional diagnostic artifacts.

## Security goals

- keep the HTTP/WebSocket surface bound to loopback rather than LAN/public interfaces
- prevent unrelated websites in the user's browser from reading or mutating CrashScope state through localhost requests
- avoid arbitrary file-read, file-write, or command-execution APIs
- operate as a normal user for routine monitoring
- avoid mandatory cloud services, analytics, accounts, or automatic diagnostic upload
- keep release artifacts traceable to tested source/build metadata
- avoid persisting sensitive browser responses in cache

## Current controls

- Kestrel listens through `ListenLocalhost`, producing loopback listeners only
- browser-originated HTTP/WebSocket requests are accepted only from CrashScope's own `http://localhost:5077`, `http://127.0.0.1:5077`, or `http://[::1]:5077` origin
- malformed, HTTPS, wrong-port, non-loopback, and unrelated web origins are rejected
- native local tooling that does not send a browser `Origin` header remains supported
- browser responses use `Cache-Control: no-store` / `Pragma: no-cache`
- CSP, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, referrer, permissions, COOP, and same-origin resource policies are applied
- no default LAN exposure
- no arbitrary file-read API
- no arbitrary command-execution API
- least-privilege normal operation
- no mandatory cloud
- no analytics by default
- no automatic telemetry upload
- crash dumps excluded from source control and normal exports
- CI rejects tracked dump/database/generated-output patterns
- portable releases include checksums, build provenance, project license, and third-party notices

## Why loopback alone is not sufficient

Binding to `127.0.0.1` / `::1` prevents another machine on the LAN from directly connecting to CrashScope, but a browser can make requests to localhost while displaying an unrelated remote website. A hostile page could therefore attempt HTTP POSTs or a WebSocket connection to local software.

CrashScope treats the browser `Origin` header as an additional boundary for browser-originated traffic. Requests from unrelated web origins are rejected before reaching API endpoints or the WebSocket upgrade. This reduces cross-site request and localhost-WebSocket abuse without requiring an account or cloud authentication layer for a single-user local application.

## Explicit trust boundary

CrashScope does **not** attempt to defend its localhost API from arbitrary native processes already executing under the same Windows user. Such a process can generally interact with the user's files, processes, sockets, and `%LOCALAPPDATA%` with comparable authority. Requests without a browser `Origin` remain intentionally available to local tooling such as PowerShell.

Administrator-level compromise, malicious kernel drivers, or another process directly modifying CrashScope's SQLite files are also outside the beta's primary protection boundary.

## Sensitive data considerations

Potentially identifying local data can include:

- process/application names
- executable paths containing a Windows user/profile path
- hardware and driver information
- incident timestamps
- Windows diagnostic summaries

These values remain local by default. Public issue templates and security guidance instruct users not to upload personal crash dumps, WER archives, private paths, credentials, or unrelated diagnostics.

Future export/support-bundle features should redact personal paths where practical and require an explicit user action before any data leaves the machine.

## Release and supply-chain considerations

The v0.1.0 pipeline uses a committed npm lockfile and `npm ci`, .NET package restore, self-contained Windows publishing, a launch/health smoke of the exact published executable, package-content checks, SHA-256 release checksums, and build provenance in `BUILD-INFO.txt`.

The first beta is unsigned, so Windows SmartScreen reputation warnings are expected and are not themselves evidence that the binary is malicious. Code signing is deferred until the portable beta is validated on additional systems.

## Revisit triggers

The threat model must be revisited before any of the following are introduced:

- LAN/remote access
- cloud synchronization or accounts
- automatic uploads
- privileged/service-mode operation
- arbitrary user-selected file ingestion or export bundles
- write-capable external integrations
- installer auto-update mechanisms
- remote command/control functionality
