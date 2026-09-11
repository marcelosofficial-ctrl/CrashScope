# One-command portable release validation

The final primary-machine gate for a CrashScope beta can be automated with `scripts/Validate-PortableCandidate.ps1`.

The script is intended for a **normal, non-Administrator Windows PowerShell** session. It validates the exact CI-produced ZIP rather than rebuilding the app locally.

## What it verifies automatically

1. Candidate ZIP SHA-256 matches the expected value.
2. Port 5077 is free before startup.
3. Package extracts successfully and contains:
   - `CrashScope.exe`
   - bundled dashboard (`wwwroot/index.html`)
   - `LICENSE.txt`
   - `THIRD-PARTY-NOTICES.md`
   - `BUILD-INFO.txt`
4. Portable `CrashScope.exe` starts outside the source repository.
5. API reaches `status = running`.
6. TCP listeners are loopback-only (`127.0.0.1` / `::1`).
7. Launching CrashScope a second time reuses the healthy existing instance rather than stealing the listener.
8. A temporary harmless PowerShell workload is discovered and attached with PID + start-time identity.
9. Sampling changes to `Active`, then returns to `Background` after stop.
10. A safe `UserDiagnosticMarker` completes without fabricating a crash.
11. The marker survives a CrashScope restart through SQLite persistence.
12. A 30-second CPU / working-set / private-memory smoke measurement is recorded.
13. WebSocket delivery-miss and stale-frame diagnostics are recorded.
14. A machine-readable JSON validation report is written to `%TEMP%`.

The script cleans up its temporary workload automatically. By default it also stops the candidate at the end. Pass `-LeaveRunning` to leave the validated candidate open for the two remaining visual checks.

## Run

First stop any development or portable CrashScope instance already using port 5077.

From a **normal Windows PowerShell (not Administrator)**:

```powershell
cd "$env:USERPROFILE\source\repos\CrashScope"
```

Then run the validator with the filename and SHA-256 published for the current candidate:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Validate-PortableCandidate.ps1" `
  -CandidateZip "$env:USERPROFILE\Downloads\CrashScope-candidate-<sha>-win-x64.zip" `
  -ExpectedSha256 "<64-character SHA-256>" `
  -LeaveRunning
```

`-ExecutionPolicy Bypass` applies only to that child PowerShell process; it does **not** change the user's system execution-policy setting.

A successful run ends with:

```text
PASS: portable release candidate validation completed.
```

and prints JSON containing the checksum, build provenance, incident/session counts, listener addresses, stream diagnostics, and performance measurements.

## Two visual checks that remain manual

Automation cannot judge these UX behaviors reliably:

1. **Incident focus cue:** while the incident detail is below the viewport, click an incident. The selected detail panel should visibly scroll/focus into view and briefly highlight. Reduced-motion preferences should avoid smooth animation.
2. **Second-instance browser behavior:** the validator launches a second instance and verifies process/listener behavior automatically. Confirm that the existing CrashScope dashboard was opened/reused in the normal browser rather than showing a port error.

## Pass criteria for v0.1.0

- script reports `passed: true`
- `samplingMode` ends at `Background`
- no non-loopback listener exists
- `streamDeliveryMisses` remains zero
- CPU remains comfortably below the 0.5% Agent budget on the primary machine
- working set stays within the documented release budget or any deviation is investigated
- both visual checks above are acceptable

After these pass, the candidate is eligible for the version tag and GitHub prerelease workflow.
