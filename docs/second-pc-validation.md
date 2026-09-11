# CrashScope second-PC beta validation

This is the evidence procedure for issue #35. It is intentionally designed so a genuinely separate Windows machine can validate the portable beta without installing the .NET SDK, .NET runtime, Node, or development tooling.

## What this gate proves

The second-PC gate checks that the exact portable ZIP works outside the primary Ryzen 5 7500F / Radeon RX 9070 XT development system while preserving CrashScope's product invariants:

- normal-user launch
- loopback-only networking
- self-contained runtime
- workload discovery / attach / stop
- active and background sampling transitions
- safe diagnostic-marker incident capture
- SQLite persistence across restart
- dashboard reconnect
- browser-origin/security policy
- low-overhead execution evidence
- honest unavailable/null sensor behavior
- original local CrashScope state restored after validation

It does **not** turn architecture intent into vendor validation. Record the actual CPU/GPU/driver and only strengthen the hardware matrix when the machine provides real evidence.

## Before running

Use a normal non-Administrator PowerShell window.

CrashScope must be closed and localhost port 5077 must be free. The wrapper refuses to proceed otherwise.

Keep these together on the second PC:

- the exact CrashScope portable ZIP being evaluated
- `scripts/Validate-SecondPcBeta.ps1`
- `scripts/Validate-PortableCandidate.ps1`
- `scripts/ValidationStateSafety.ps1`

The recommended release flow will provide the exact expected SHA-256 separately from the ZIP.

## One-command automated gate

From the CrashScope repository/scripts environment on the second PC:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Validate-SecondPcBeta.ps1 `
  -CandidateZip "C:\path\to\CrashScope-v0.1.0-win-x64.zip" `
  -ExpectedSha256 "<64-character expected SHA-256>"
```

Do not substitute a development build for the intended release/candidate ZIP.

## User-state safety

`Validate-SecondPcBeta.ps1` reuses CrashScope's hardened validation-state safety helper.

Before exercising the candidate it:

1. refuses to start if a pending validation backup already exists;
2. captures the current HKCU CrashScope startup registration;
3. records a SHA-256 manifest of existing `%LOCALAPPDATA%\CrashScope` data;
4. copies that state into a durable external validation vault;
5. verifies the copied manifest before removing the working copy;
6. runs the portable validation against isolated validation state;
7. stops the validation CrashScope process;
8. quarantines validation-created state;
9. restores the original data from the durable vault;
10. independently re-hashes the restored data and verifies startup registration;
11. deletes the durable vault only after restoration is verified.

If restoration cannot be verified, the wrapper fails closed and intentionally retains the durable vault for recovery.

The second-PC wrapper deliberately does **not** expose `Validate-PortableCandidate.ps1 -LeaveRunning`, because leaving the test Agent alive would conflict with immediate verified restoration of the original product state.

## Evidence produced

By default a timestamped folder is created beneath `%TEMP%` and contains:

- `machine-evidence.json`
  - Windows caption/version/build/architecture
  - machine manufacturer/model
  - physical RAM
  - CPU model/manufacturer/core/logical-processor counts
  - GPU model/vendor/driver/video-processor information
  - PowerShell version
  - whether `dotnet` and `node` commands happen to exist
  - confirmation that the validation shell was non-elevated

- `portable-validation-report.json`
  - exact portable-validator functional/performance evidence

- `second-pc-summary.txt`
  - compact PASS/FAIL summary
  - candidate path
  - expected and independently calculated SHA-256
  - Windows/CPU/GPU/RAM identity
  - development-runtime presence observation
  - restoration result

The presence of `dotnet` or `node` on the machine is recorded only as environmental context. CrashScope must not depend on them for the self-contained portable build.

## Remaining human checks

After the automated state-safe run passes, separately confirm the presentation/UX observations that automation cannot establish reliably:

- tray icon/menu is visible, readable, and usable
- dashboard renders normally at the machine's display scale
- available hardware metrics look plausible
- unavailable metrics are shown as unavailable rather than fake zero
- no obvious unexpected GPU load is created by CrashScope
- note SmartScreen or antivirus reputation friction separately from product failures

Do not weaken or bypass the state-safety wrapper just to keep the automated validation instance open for visual inspection. If a human inspection run is needed, launch the restored/normal app separately afterward.

## Result classification

**PASS**

The automated wrapper passes, original local state restoration is verified, and the remaining human checks show no product/invariant problem.

**PARTIAL**

Core portable behavior works, but some hardware telemetry is legitimately unavailable or a clearly environmental/non-product limitation prevents full evidence. Document it honestly.

**FAIL**

A product/release defect or invariant violation is observed, including failure to launch normally, non-loopback exposure, broken persistence, unsafe state handling, or a false support claim.

## After the run

Attach or summarize only privacy-safe evidence in issue #35. Do not publish usernames, local personal paths, email addresses, raw dumps, WER archives, credentials, or unrelated machine diagnostics.

Only after genuine second-machine evidence exists should `docs/hardware-validation.md` be updated to strengthen support claims. Installer/signing work in issue #36 remains downstream of this gate.
