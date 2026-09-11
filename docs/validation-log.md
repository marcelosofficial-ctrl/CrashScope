# CrashScope validation log

This file records durable validation evidence for CrashScope. Update it whenever a meaningful CI or real-machine milestone is reached.

## 2026-09-10 — FINAL combined next-beta normal-user gate + human UX closure

- Validated product head: `9afb5418db9293a7fc9be0cc68be018ee4d89ff0`
- PR: #59 (`main` <- `integration/next-beta`)
- Exact CI: run #393 / workflow run id `34391659724`
- Exact artifact SHA-256: `4e37376e7a26e59b82dcc30a38638d65fb425c9c4a653189895cca05c4b4d493`
- Environment: normal non-Administrator Windows PowerShell on the primary reference machine
- Automated result: **PASS**
- Human UX result: **PASS**
- Issue #72: **CLOSED / completed**

Core automated evidence:

- exact CI artifact provenance/checksum verified
- CrashScope 0.1.0 healthy startup verified
- loopback-only listeners verified on `::1:5077` and `127.0.0.1:5077`
- browser Origin/security-header policy verified
- second-instance process safety verified
- workload attach and Active -> Background transition verified
- safe marker incident captured: `953beea4-7a4b-4826-a995-fcc9429e4dc3`
- SQLite incident persistence across restart verified
- dashboard WebSocket reconnected with 1 subscriber
- stream frames published: 16
- stream dropped stale frames: 0
- stream delivery misses: 0
- persistence schema: 3

30-second Agent performance:

- average CPU: **0.2657%**
- average working set: **93 MB**
- peak working set: **95.83 MB**
- average private memory: **35.08 MB**

This is within the current <=0.5% average CPU and around <=100 MB working-set product budget.

Integrated beta checks:

- settings schema 2 verified
- default Auto Assist enabled verified
- default retention 30 days verified
- Auto Assist/retention persistence verified using temporary validation state
- invalid retention rejected
- HKCU Start with Windows exact command verified
- startup disable cleanup verified
- native Explorer tray registration verified
- support bundle preview/export contained only curated entries: README, manifest, incident, session, runtime
- support bundle persistence schema 3 / settings schema 2 metadata verified
- support-bundle identity leak scan passed
- automatic upload remained false
- retention range/persistence/startup maintenance checks passed
- deterministic retention deletion semantics remain covered by CI tests
- no synthetic mutation of the user's real database was performed

Native WER:

- on-demand native WER comparison exceeded the 30-second validation budget
- recorded as `timed-out`, non-blocking research evidence
- zero-reports remains non-failing
- live triggering was not changed
- promotion eligible: false
- native WER remains read-only/on-demand and requires separate evidence before any promotion decision

User-state safety:

- hardened outer safety vault was active
- pre-run live CrashScope state was absent
- pre-run CrashScope HKCU Run value was absent
- validator-created state was cleaned/quarantined according to the hardened path
- outer restore completed and was independently verified
- post-run `%LOCALAPPDATA%\CrashScope` remained absent, matching pre-run state
- post-run CrashScope startup registration remained absent, matching pre-run state
- no unresolved durable preservation vault remained
- zero CrashScope processes remained
- port 5077 was not listening after cleanup

Human UX closure:

1. Tray right-click compact menu: **PASS** — user manually confirmed it is readable and usable.
2. Incident selection/capture detail scroll/focus behavior: **PASS** — user manually confirmed expected behavior.
3. Same-session screenshot showed a clean current dashboard render with Agent online, Protected locally, Auto Assist enabled, and live CPU/GPU/RAM telemetry.

Subsequent documentation-only bookkeeping head `f2b1105df775363bb167e3267b4ae8ab1b3a0db4` also passed CI #399 / workflow run `34393722281`. Documentation-only commits after the validated product head do not require rerunning the expensive product gate.

The #72 validation gate is complete. Do not rerun the combined validator unless product code changes invalidate the validated product head.

## 2026-09-10 — validation-state safety hardening proof

- Preservation hardening integrated at `046bf6a1ea977529a52e4d01554b41bd3e2dc050`
- Synthetic adversarial safety proof integrated at `754f27b02c27da783e35c91b2ed683d1036efca9`
- Local primary-machine execution of `scripts/Test-ValidationStateSafety.ps1`: PASS
- Covered:
  - verified preserve / quarantine / restore
  - interrupted-run fail-closed behavior
  - legacy WorkingRoot backup refusal
  - tampered-backup refusal before touching current state
  - originally-empty state restoring back to empty
  - startup snapshot comparison

## 2026-09-09 — second combined next-beta real-machine attempt

- Integration head: `cde2b77422f759566835ce5e853218284ad2bc1a`
- CI: #339
- Result: PARTIAL PASS
- Exact artifact SHA-256 verified
- Healthy portable startup verified
- Loopback-only listener verified
- Browser Origin/security headers verified
- Second-instance safety verified
- Workload attach verified
- Active -> Background transition verified
- Safe marker incident: `e73cfe97-b699-4850-a5cc-b056ff7f33e0`
- SQLite restart persistence verified
- Dashboard reconnect verified with 1 WebSocket subscriber
- 30-second Agent performance:
  - average CPU: 0.2699%
  - average working set: 90.68 MB
  - peak working set: 93.62 MB
  - average private memory: 34.23 MB
- Limitation: final combined evidence was not preserved, motivating the durable transcript/report work completed later.

## 2026-09-09 — first combined next-beta real-machine attempt

- Result: VALIDATOR TOOL FAILURE, not product failure
- Failure: Windows PowerShell 5.1 rejected empty `Start-Process -ArgumentList`
- Product runtime was not reached
- Compatibility fixes followed.

## Historical dashboard-open performance milestone

- average Agent CPU: 0.0682%
- average working set: 91.09 MB
- peak working set: 92.41 MB
- average private memory: 35.69 MB
- stream subscribers: 1
- stream dropped stale frames: 0
- stream delivery misses: 0

## Validation retention policy

For future real-machine runs, preserve:

1. exact integration SHA
2. exact CI run id
3. exact artifact SHA-256
4. final JSON report
5. durable console transcript
6. CPU/memory measurements
7. stream diagnostics
8. native WER timing/counts/store accessibility
9. support-bundle privacy result
10. user-state restoration result
11. remaining human UX checks

Durable transcript:

`%TEMP%\CrashScope-next-beta-validation-transcript.txt`

Final consolidated JSON:

`%TEMP%\CrashScope-next-beta-validation\next-beta-validation.json`
