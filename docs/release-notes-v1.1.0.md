# CrashScope 1.1.0 release notes

CrashScope 1.1.0 extends the frozen 1.0 line with a generic external-evidence provider architecture and ConfigTrace integration while preserving the local-first, privacy-first, evidence-first, and low-overhead design.

Public publication is still intentionally deferred until the coordinated release sweep. The exact final `v1.1.0` tag target is the locally frozen 1.1 release commit produced after validation; no tag or GitHub Release is created by the local release-engineering batches.

## What changed from 1.0

### Generic external evidence providers

CrashScope now has a generic `EvidenceEvent` provider boundary for process-isolated evidence sources. Provider evidence is mapped into the existing incident model as bounded chronological evidence rather than as a separate causal system.

The central rule remains:

> A nearby event is correlation evidence unless stronger evidence supports a causal claim.

For example, CrashScope can say that a renderer setting changed seconds before an incident. It does not claim that the setting change caused the incident.

### ConfigTrace 1.0.1 integration

CrashScope 1.1 bundles ConfigTrace as its first external sidecar provider:

- ConfigTrace is **OFF by default**;
- the user explicitly selects one existing absolute configuration root;
- ConfigTrace starts only while a workload is actively monitored;
- the sidecar stops with the workload;
- journals live under CrashScope-owned local application data;
- nearby semantic configuration changes are imported as `Context` evidence;
- ConfigTrace failure is isolated and must not take CrashScope down;
- portable and installed layouts use the same bundled `providers\ConfigTrace\configtrace.exe` path.

Bundled ConfigTrace provenance:

- version: **1.0.1**
- source commit: `b629c970dfc14fca5df1e0ef2b0d1d07d0d8c56c`
- Windows EXE SHA-256: `fe1c470a58402e82e97ee529c6a6b02822430da70e65ffc5fc5a71359ad4e521`
- license: MIT
- bundled license path: `providers\ConfigTrace\LICENSE.txt`

ConfigTrace 1.0.1 includes a privacy fix discovered during CrashScope integration: sensitive-key recognition now handles camelCase/PascalCase names such as `apiToken`, `accessToken`, `refreshToken`, `clientSecret`, `sessionId`, and `APIKey`. Real JSONL watcher validation proved that sensitive values remain redacted while change fingerprints continue to work.

### Settings and dashboard

CrashScope settings schema v3 adds:

- `configTraceEnabled`
- `configTraceRootPath`

Existing 1.0-era settings migrate with ConfigTrace disabled and no configured root.

The dashboard adds a ConfigTrace control that:

- saves one absolute configuration root;
- prevents enabling the provider without a valid saved root;
- explains that ConfigTrace only runs during monitored workloads;
- keeps the provider disabled by default.

### Manual diagnostic markers

User-requested safe diagnostic markers now use the same bounded provider-evidence mapping as live incidents. ConfigTrace evidence attached to a marker remains `Context`; the marker classification stays `UserDiagnosticMarker`.

## Validation status

The 1.1 integration checkpoint passed:

- **239 .NET tests passed, 0 failed**
- ConfigTrace Rust formatting/tests/build validation
- ConfigTrace 1.0.1 real watch-journal privacy smoke
- portable sidecar packaging/hash/provenance checks
- real ConfigTrace OFF/ON lifecycle validation
- real semantic config-change journal capture
- secret absent from the raw ConfigTrace journal
- secret absent from CrashScope incident JSON
- exactly one ConfigTrace evidence item observed in the end-to-end marker validation
- that evidence item mapped as `Context`
- bundled sidecar started with the monitored workload and stopped with it
- real user CrashScope state restored after validation

A short idle integration measurement during the pre-release A11 gate recorded approximately:

- ConfigTrace OFF: Agent average CPU **0.29%**, Agent peak working set **97.63 MB**
- ConfigTrace ON: Agent average CPU **0.1937%**, Agent peak working set **99.74 MB**
- ConfigTrace sidecar average CPU **0%** in that sample, peak working set **4.95 MB**

Those are targeted integration measurements, not the final 1.1 performance seal. Final release validation separately rechecks performance and cross-machine behavior.

## Packaging

The self-contained 1.1 portable and installer packages include:

```text
CrashScope.exe
wwwroot/
providers/
  ConfigTrace/
    configtrace.exe
    LICENSE.txt
LICENSE.txt
THIRD-PARTY-NOTICES.md
BUILD-INFO.txt
```

The installer recursively installs the same validated portable payload. It remains per-user, non-admin for normal operation, does not create a Windows service, does not force Start with Windows, and preserves CrashScope user data.

## Not included in 1.1

GapTrace/network evidence is intentionally not part of CrashScope 1.1. The generic provider boundary is designed to support future evidence sources without making them a requirement for this release.

Code signing is also not implemented in 1.1.0; unsigned-build SmartScreen reputation warnings may occur.

## Remaining local release gates before publication

Before the coordinated publication sweep, CrashScope 1.1 still requires the final release-candidate gates:

- true installed **1.0.0 -> 1.1.0** upgrade/state-preservation validation;
- final 1.1 performance and cleanliness seal;
- repeat cross-machine/second-PC validation for the exact 1.1 artifact;
- final UTF-8, security/privacy, and loopback verification;
- exact artifact/hash handoff to the coordinated release process.

No push, tag, GitHub Release, repository-visibility change, or GitHub Actions run is implied by this document.