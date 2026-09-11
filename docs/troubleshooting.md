# CrashScope troubleshooting

This guide covers common beta problems without requiring Administrator privileges, unsafe hardware testing, LAN exposure, or destructive system changes.

## CrashScope.exe flashes and closes

### Another CrashScope instance may already be running

Open:

```text
http://localhost:5077
```

If the CrashScope dashboard appears, a healthy instance is already running. Current builds detect this and normally open/reuse the existing dashboard on a second launch.

### Another application may be using port 5077

In a normal PowerShell window:

```powershell
Get-NetTCPConnection -LocalPort 5077 -State Listen -ErrorAction SilentlyContinue |
    Select-Object LocalAddress,LocalPort,OwningProcess
```

If a listener exists, identify it:

```powershell
$listener = Get-NetTCPConnection -LocalPort 5077 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($listener) { Get-Process -Id $listener.OwningProcess | Select-Object Id,ProcessName,Path }
```

Do not terminate an unfamiliar process just to free the port. Close the owning application normally or report the conflict.

## Windows SmartScreen appears

Unsigned beta packages can trigger SmartScreen reputation warnings even when the ZIP matches the checksum published by the project.

Before running:

1. Download the ZIP and checksum from the same Release or validated CI artifact.
2. Verify the ZIP with `Get-FileHash`.
3. Confirm the hash matches exactly.

A checksum match confirms the downloaded bytes match the published package. It does not replace antivirus or general security judgment.

## The browser did not open

CrashScope's Agent may still be healthy. Open:

```text
http://localhost:5077
```

You can also check from a normal PowerShell window:

```powershell
Invoke-RestMethod -Uri 'http://127.0.0.1:5077/api/status' | ConvertTo-Json -Depth 6
```

A healthy Agent should report a running status.

## The page shows JSON instead of the dashboard

A packaged build should contain:

```text
wwwroot\index.html
```

Make sure the complete ZIP was extracted and that `CrashScope.exe` remains with the rest of the portable files.

## Auto Assist says Disabled

Auto Assist can be turned off from the dashboard. That preference persists in:

```text
%LOCALAPPDATA%\CrashScope\settings.json
```

Re-enable Auto Assist in the dashboard. Restarting CrashScope should not be required.

Disabling Auto Assist prevents future automatic attaches but deliberately does not terminate an already active session.

## Auto Assist does not monitor my game

Automatic attachment is intentionally conservative.

CrashScope requires a strongly classified target and observes the same PID plus process-start-time identity twice before automatic attach. Generic applications, helper processes and weak/ambiguous candidates are rejected by design.

Try:

1. Keep the game in the foreground for several seconds.
2. Check the Auto Assist state/reason in the dashboard.
3. If it still does not qualify, use the manual workload picker.

A manual session remains the supported fallback and override.

## Auto Assist stopped reattaching after I pressed Stop

Expected. Manual Stop starts a cooldown so automation does not immediately override the user's decision.

The current design uses roughly a ten-minute reattach cooldown.

## CPU temperature, CPU power, or clocks show unavailable

This can be normal on some systems through CrashScope's current least-privilege provider path.

CrashScope intentionally represents missing or invalid telemetry as unavailable rather than turning it into fake `0 °C`, `0 W`, or `0 MHz` evidence.

When reporting sensor coverage, include:

- CPU model
- motherboard
- Windows version
- CrashScope version
- exact unavailable metrics

Do not run CrashScope as Administrator solely to force missing sensors unless a specific hardware-validation experiment requests it.

## GPU telemetry is missing or incomplete

The strongest current real-machine validation is AMD Radeon RX 9070 XT. CrashScope's data model is multi-GPU/vendor-neutral, but real NVIDIA and Intel coverage is still expanding.

Include GPU model and driver version in the report. Do not interpret an unavailable sensor as a zero value.

## RAM numbers look wrong

Compare CrashScope with Windows Task Manager before assuming a fault. If values are materially inconsistent, report:

- installed physical memory
- CrashScope memory percentage
- used/available values shown
- CrashScope version
- a sanitized screenshot if useful

Do not attach a full memory dump.

## A workload does not appear in the manual picker

Choose **Refresh workloads** while the target is running.

CrashScope filters or deprioritizes many helper/system processes. The target must also still have the same PID plus start-time identity when you attach it.

If the process exited or restarted after discovery, refresh and select the new process instance.

## Monitoring refuses to attach

A safe attach can fail when:

- the process already exited
- Windows recycled the PID
- the process restarted after discovery
- another CrashScope session is already active

Refresh the workload list and try the current instance.

## Monitoring stays Active after a process exits

The normal supported process-exit path is event-driven rather than a one-second PID polling loop. Restricted or unsupported process handles can fall back to sparse reconciliation.

If a session remains active unexpectedly, record the active session/process identity and `/api/status` output rather than editing the database manually.

## Safe diagnostic marker takes a while

Expected. CrashScope intentionally captures post-trigger telemetry, so completion takes roughly 30 seconds.

Do not click the marker repeatedly during an active capture.

The result should be classified:

```text
UserDiagnosticMarker
```

and explicitly state that it is not evidence of a real crash, hardware error or driver failure.

## An incident appears after restart

Persisted incidents should remain visible across restarts.

A previously stored incident should not be treated as a new crash merely because CrashScope restarted. If old Windows evidence appears to create a brand-new incident on every startup, report the incident/evidence timestamps and identifiers.

## Incident guidance seems too cautious

This is intentional. CrashScope separates:

1. Observed
2. Meaning
3. What this does not prove
4. Next checks

For example, Kernel-Power 41 proves Windows observed an abnormal transition. It does not by itself prove a bad PSU. High GPU utilization can be useful context but is normal during demanding workloads and is not fault evidence by itself.

## Share safely / support bundle does not download

First verify that the incident still opens correctly and that the privacy preview appears.

The support bundle is created in memory only after explicit user action. CrashScope does not automatically upload it or keep a permanent server-side ZIP.

Expected archive scope is deliberately small:

- README.txt
- manifest.json
- incident.json
- optional session.json
- runtime.json

It should not contain the SQLite database, dump files, ETL traces, raw WER directories, arbitrary logs or arbitrary environment variables.

If download fails, report the browser/version, CrashScope version, incident ID and the error shown. Do not manually collect and upload sensitive WER/dump material as a substitute.

## Is the support bundle guaranteed to contain no private text?

No redactor can honestly guarantee arbitrary free-form diagnostic text is risk-free.

CrashScope applies defense-in-depth redaction for the current user profile, username, machine name, email, IPv4/IPv6, MAC addresses and obvious credential-style values. Always inspect the preview and review what you share.

## High CrashScope CPU or memory use

The primary validated dashboard-open baseline is approximately:

- Agent CPU: ~0.068% average
- working set: ~91 MB

The product budget is at most 0.5% average Agent CPU on the Ryzen 5 7500F reference system, with a roughly 100 MB working-set target for the validated dashboard-open scenario.

If usage is materially higher:

1. Let CrashScope run long enough to avoid judging one instantaneous Task Manager spike.
2. Record whether sampling is Background or Active.
3. Record whether the browser dashboard is open or closed.
4. Record whether Auto Assist is Ready, Confirming, Monitoring or Disabled.
5. Record CPU/GPU model and CrashScope version.

Closing the browser should leave full Agent monitoring active and removes browser rendering overhead.

## The dashboard disconnects and reconnects

The browser uses a local WebSocket. Browser throttling must not backpressure hardware sampling; CrashScope uses bounded stale-frame dropping to protect the central sampler.

Useful status fields include:

- streamSubscribers
- streamFramesPublished
- streamDroppedStaleFrames
- streamDeliveryMisses

A stale frame can be deliberately discarded instead of slowing sampling. Repeated delivery misses should be reported.

## Windows diagnostic events seem delayed

CrashScope uses real-time Event Log subscriptions for relevant providers where available, plus sparse reconciliation for missed events, sleep/resume and restricted environments.

Windows Event Log `TimeCreated`, WER observation time, artifact time and the actual incident time are not always identical. CrashScope deliberately preserves these distinctions instead of pretending every timestamp means the same thing.

## Access from another device does not work

That is intentional.

CrashScope binds to loopback only and is not a LAN dashboard. Do not change firewall/listener settings to expose it remotely.

## A remote website cannot call the local API

Also intentional. Browser-originated HTTP/WebSocket requests are restricted to CrashScope's own trusted localhost origins. Native local tools such as PowerShell can use the API without a browser `Origin` header.

## Where is my data?

Persistent CrashScope data is under:

```text
%LOCALAPPDATA%\CrashScope
```

The SQLite database stores sessions/incidents/environment history. `settings.json` stores persistent product settings such as Auto Assist.

Deleting the extracted program folder does not automatically delete this application data.

Do not manually edit the SQLite database while CrashScope is running.

## Start with Windows is not visible yet

The next-beta codebase contains the tested per-user Windows startup foundation, but product controls should only be exposed once the full behavior is wired and validated.

CrashScope's intended startup behavior is current-user only, no Administrator requirement, and `--no-browser` at logon. Do not manually edit the registry just because the foundation exists internally.

## What should I include in a bug report?

Prefer concise, reproducible information:

- CrashScope version
- Windows version
- CPU and GPU model
- GPU driver version
- what workload was involved
- Auto Assist/manual mode
- expected behavior
- observed behavior
- relevant `/api/status` fields
- reviewed CrashScope support bundle when appropriate
- sanitized screenshot if useful

Do **not** publish:

- raw crash dumps
- raw WER archives
- credentials/tokens
- private documents
- unrelated logs
- screenshots exposing personal paths or usernames unless intentionally redacted

For suspected security issues, follow [`../SECURITY.md`](../SECURITY.md) instead of posting exploit details publicly.
