# Combined next-beta validation

CrashScope's post-v0.1 work is integrated through `integration/next-beta` and PR #59 while the validated v0.1 candidate on `main` remains frozen.

The combined beta gate is intentionally one normal-user Windows run instead of repeated feature-by-feature checks.

## Run

From a normal, non-Administrator Windows PowerShell window in the CrashScope repository:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Validate-NextBeta-WindowsPowerShell.ps1
```

Prerequisites:

- Exit any currently running CrashScope instance first.
- GitHub CLI (`gh`) must be installed and authenticated.
- Do not run the PowerShell window as Administrator. Normal-user behavior is part of the gate.

The Windows PowerShell entrypoint applies the narrow compatibility adjustment needed by Windows PowerShell 5.1 to a temporary copy of the portable validator, parses that copy before execution, then runs the same combined gate.

The script resolves PR #59's current `integration/next-beta` head, requires successful CI for that exact SHA, downloads the exact CI-tested `CrashScope-ci-win-x64` artifact, and verifies its packaged SHA-256 before running anything.

## Durable transcript

The Windows PowerShell entrypoint starts a transcript before the nested validator runs and preserves it outside the validator cleanup root:

`%TEMP%\CrashScope-next-beta-validation-transcript.txt`

This is intentionally separate from `%TEMP%\CrashScope-next-beta-validation` because the combined validator deletes and recreates that working directory while isolating user state.

The transcript is useful even when the validator fails late, the console is closed, or a chat/session ends before the final output is copied. It records the exact last stage reached and all console-visible evidence up to that point.

## User-state isolation

Before the candidate starts, the validator temporarily isolates:

- `%LOCALAPPDATA%\CrashScope`
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CrashScope`

The isolated candidate therefore starts with clean defaults and cannot mix test incidents/settings with the user's real CrashScope history.

The validator restores the original local-data directory and exact current-user startup registry value from a `finally` block, including after a failed check. Test candidate processes are stopped before restoration.

If restoration reports a warning, preserve both the validation working directory and durable transcript and investigate before launching CrashScope again.

## Automated evidence

The gate reuses `Validate-PortableCandidate.ps1` and adds integrated next-beta checks.

Core portable gate:

- exact package checksum and provenance
- normal portable startup
- loopback-only listener ownership
- localhost browser Origin and security headers
- safe second-instance behavior
- workload discovery and PID + process-start-time attach
- Active -> Background sampling transition
- safe diagnostic marker capture
- SQLite incident persistence across restart
- dashboard WebSocket reconnect observation
- 30-second Agent CPU / memory measurement
- stale-frame and delivery-miss diagnostics

Next-beta product gate:

- settings schema/defaults and retention range validation
- persistent Auto Assist and retention values across a real Agent restart
- exact per-user Start-with-Windows command (`"CrashScope.exe" --no-browser`) under HKCU, followed by clean disable
- native tray message-window presence plus Explorer notification-icon registration through `Shell_NotifyIconGetRect`
- support-bundle preview and explicit local ZIP creation for the safe marker
- support ZIP entry allow-list checks and rejection of DB/dump/ETL/raw-WER/log payloads
- support runtime metadata for persistence schema, settings schema and retention policy
- defense-in-depth scan for the current user profile, username and machine name without printing exported content
- one explicit read-only native WER report-store comparison, recording elapsed time, per-store accessibility/counts, native/existing report counts and ReportId overlap

A WER store being unavailable or containing zero matching historical reports is recorded as evidence, not treated as an automatic product failure. Native WER is still not part of live incident triggering.

Retention deletion semantics remain covered by deterministic repository tests. The real-machine gate validates range handling, persistence and successful startup maintenance without injecting synthetic rows into the user's database.

## Output

The final consolidated report is written under:

`%TEMP%\CrashScope-next-beta-validation\next-beta-validation.json`

The durable transcript is written under:

`%TEMP%\CrashScope-next-beta-validation-transcript.txt`

The JSON report includes the exact PR head, CI run, artifact SHA-256, core portable results, settings/startup/tray/support-bundle results, native-WER measurements, and remaining human checks.

After a successful real-machine run, record the exact results in `docs/validation-log.md` and refresh `docs/project-state.md` so the repository remains the durable handoff across future chat/session boundaries.

## Remaining visual checks

Only behavior that is not reliable to prove through localhost APIs or Win32 registration calls remains manual:

1. Right-click the CrashScope tray icon and confirm the compact menu is readable and usable.
2. Select or capture an incident in the dashboard and confirm Incident detail visibly scrolls/focuses into view.

Do not deliberately crash an application to complete these checks.
