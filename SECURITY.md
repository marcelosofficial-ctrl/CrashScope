# Security Policy

CrashScope is a local-first Windows diagnostics application. Security reports are welcome, especially for issues involving local API exposure, unsafe file handling, privilege escalation, persistence corruption, browser-origin bypasses, or unintended network communication.

## Supported versions

During the public beta, only the latest published `0.x` release is supported for security fixes.

## Reporting a vulnerability

Please do **not** publish exploit details in a public GitHub issue before a fix is available.

Use GitHub's private vulnerability reporting feature for this repository when available. If private reporting is not available, open a minimal issue that states a security problem exists without including sensitive reproduction details, and request a private contact path.

Useful information includes:

- CrashScope version (available from `/api/status` in hardened builds)
- Windows version
- whether CrashScope was run normally or elevated
- exact affected endpoint/file/workflow
- minimal reproduction steps
- expected vs observed behavior
- browser Origin involved, if applicable
- whether another local user/process is required
- whether network access beyond loopback is involved

Do not attach personal crash dumps, WER archives, private paths, authentication material, or unrelated diagnostic data to a public issue.

## Security design boundaries

CrashScope is designed to:

- bind its dashboard/API to loopback only
- reject browser-originated HTTP/WebSocket requests from unrelated/non-loopback origins
- accept native local tooling without requiring a browser Origin header
- send restrictive CSP/frame/content-type/referrer/permissions/cross-origin response headers
- mark local diagnostic/UI responses `no-store` to avoid persistent browser caching
- avoid mandatory accounts, cloud backends, analytics, and automatic telemetry upload
- operate without Administrator privileges for normal use
- keep persistent application data under `%LOCALAPPDATA%\CrashScope`
- treat crash dumps and machine-specific diagnostic artifacts as local evidence rather than automatically uploading them
- avoid executing arbitrary user-supplied code as part of normal incident analysis
- publish portable packages with a SHA-256 checksum, license/third-party notices, and build provenance

These are product goals and controls, not a guarantee that vulnerabilities cannot exist.

## Localhost trust model

Loopback binding protects CrashScope from direct LAN/public exposure, but browser-served localhost applications also need protection from unrelated websites trying to access local endpoints. CrashScope therefore validates browser `Origin` before API or WebSocket handling.

CrashScope does **not** claim to isolate its API from arbitrary native processes already executing under the same Windows user. Such a process generally has comparable access to the user's sockets, files, processes, and `%LOCALAPPDATA%`. Requests without a browser Origin intentionally remain available to native local tools such as PowerShell.

See [`docs/threat-model.md`](docs/threat-model.md) for the fuller boundary and rationale.

## Out of scope for early beta

The following may still be useful bug reports but are not automatically security vulnerabilities:

- Windows SmartScreen warnings on unsigned beta binaries
- hardware sensors that are unavailable or inaccurate because the underlying provider/driver does not expose them
- denial of service that requires the same local user to deliberately terminate CrashScope
- unsupported behavior caused by manually modifying the SQLite database while CrashScope is running
- a malicious native process already running with the same user's permissions calling the localhost API
