# CrashScope user guide

CrashScope is a local-first Windows diagnostics application for correlating low-overhead hardware telemetry with Windows crash, watchdog, application-failure and hardware-error evidence.

CrashScope can be distributed as either a **per-user Windows installer** or a **portable ZIP**. Both formats contain the self-contained Windows application, so end users do not need a separate .NET runtime, Node.js, web server, account, cloud backend, analytics service, or mandatory internet connection.

CrashScope is designed to answer a careful question: **what did the system actually observe around the failure?** It does not claim that correlation proves root cause.

## 1. Choose installer or portable

### Installer

For most users, the installer is the simplest option once a release is published.

Run:

```text
CrashScope-Setup-<version>.exe
```

The installer is per-user and normally requires no Administrator elevation.

Its default installation directory is:

```text
%LOCALAPPDATA%\Programs\CrashScope
```

It creates a Start Menu shortcut. A desktop shortcut is optional and is not selected by default.

Installing CrashScope does **not** automatically enable `Start with Windows`. That remains an explicit opt-in setting controlled by CrashScope itself.

The installer does not install a Windows service or machine-wide startup entry.

### Portable

For portable use, download the Windows x64 ZIP and extract the complete archive into a normal user-writable directory.

Keep `CrashScope.exe`, `wwwroot`, license files, runtime files, and build information together.

Do not copy only `CrashScope.exe` into another folder.

## 2. Verify the download

Use the SHA-256 checksum published with the exact installer or ZIP you downloaded.

For an installer:

```powershell
Get-FileHash .\CrashScope-Setup-*.exe -Algorithm SHA256
```

For a portable ZIP:

```powershell
Get-FileHash .\CrashScope-v*-win-x64.zip -Algorithm SHA256
```

Compare the calculated hash with the published checksum.

If the hashes differ, do not run that artifact.

Unsigned CrashScope builds can produce a Windows SmartScreen reputation warning. Do not disable Windows security features to suppress a warning. Verify that the artifact came from the intended CrashScope release and that its SHA-256 checksum matches before deciding whether to run it.

## 3. Start, stop, and uninstall CrashScope

For an installed copy, open **CrashScope** from the Start Menu.

For a portable copy, double-click:

```text
CrashScope.exe
```

Normal startup opens the local dashboard in your default browser after the Agent is ready.

CrashScope listens only on the current PC at:

```text
http://localhost:5077
```

If the browser does not open automatically, open that address manually.

For startup or automation where no browser should open:

```powershell
.\CrashScope.exe --no-browser
```

Normal operation does not require Administrator privileges.

If a healthy CrashScope instance is already running, launching `CrashScope.exe` again reuses the existing instance and opens its dashboard instead of creating a second hardware monitor.

Use **Exit CrashScope** from the notification-area menu when you want to stop the Agent completely.

To uninstall an installed copy, use the normal Windows installed-app removal flow. CrashScope's installer intentionally preserves:

```text
%LOCALAPPDATA%\CrashScope
```

This allows settings and diagnostic history to survive a reinstall or upgrade.

If you intentionally want to erase that data as well, first exit CrashScope, uninstall it, and then remove the CrashScope data directory manually.

A portable copy has no installer registration. After exiting CrashScope, the extracted application directory can be deleted independently of the user-data directory.

## 4. Everyday Mode and Auto Assist

The default experience is designed so you can launch CrashScope and then use your PC normally.

Auto Assist watches only the current foreground process at a low frequency and uses CrashScope's existing workload classifier. It does **not** continuously enumerate every process and does **not** create another hardware telemetry loop.

Automatic monitoring is deliberately conservative. CrashScope only auto-attaches when a workload is strongly classified as an appropriate game, benchmark, local-AI workload or similar target, and the same PID plus process-start-time identity is observed twice.

The dashboard can show states such as:

- Ready
- Confirming
- Monitoring
- Paused by user
- Disabled

You can turn Auto Assist off from the dashboard. The setting persists under:

```text
%LOCALAPPDATA%\CrashScope\settings.json
```

Turning Auto Assist off prevents future automatic attaches. It does not interrupt a workload that is already being monitored. Re-enabling takes effect without restarting CrashScope.

A manual session always wins over automatic selection. If you manually stop a session, CrashScope applies a cooldown before Auto Assist can attach again.

## 5. Close the browser while gaming

The Agent is the monitoring product. The browser is only a viewer.

You can close the CrashScope browser tab while playing a game or running a demanding workload. Monitoring, rolling telemetry, Windows evidence collection, session tracking and incident capture continue in the Agent.

On interactive Windows sessions, the notification-area icon provides a small control surface for reopening the dashboard without leaving a browser renderer open. Closing the browser is the lowest-overhead way to leave CrashScope running during a game because there is no browser renderer consuming CPU, GPU or memory.

## 6. Live telemetry

CrashScope currently samples:

- Background: 0.5 Hz, one sample every two seconds
- Active workload session: 1 Hz

The same central telemetry frame is reused by the rolling incident buffer, session aggregation and dashboard stream. Opening the dashboard does not trigger extra hardware reads.

Depending on hardware and driver support, telemetry can include:

- CPU utilization
- CPU temperature, clocks and package power when available
- system-memory load and capacity
- GPU utilization
- GPU core temperature and hotspot
- GPU memory temperature when available
- VRAM used and total
- GPU clocks
- package power
- fan data

An unavailable sensor is represented as unavailable, not as a fake zero.

## 7. Manual workload monitoring

Manual selection remains available as an override and fallback.

In the workload section:

1. Refresh the workload list.
2. Find the target game/application.
3. Choose Monitor.
4. Confirm an Active Session appears.
5. Reproduce or continue the workload normally.
6. Choose Stop when finished.

CrashScope identifies a workload using **PID + process start time**, never PID alone. A recycled PID therefore cannot silently become the wrong monitored process.

Normal process exit is recorded separately from diagnostic evidence. A disappearing process is not automatically classified as a crash.

## 8. Incident reports

CrashScope keeps a rolling telemetry buffer and preserves approximately:

- 60 seconds before an incident
- 30 seconds after an incident

Supported incident classifications include:

- Unclassified
- ApplicationFailure
- KernelOrDriverWatchdog
- HardwareError
- UnexpectedShutdown
- Mixed
- UserDiagnosticMarker

Incident detail is organized around:

1. **Observed**
2. **Meaning**
3. **What this does not prove**
4. **Next checks**

Guidance is deterministic and evidence-driven. For example, a display-driver reset plus high GPU utilization can be reported together, but high utilization alone is not presented as proof that the GPU is defective.

Technical evidence remains available with Trigger, Corroborating and Context roles.

## 9. Safe diagnostic marker

**Capture diagnostic marker** tests the complete incident pipeline without deliberately crashing an application, driver or PC.

The result is classified:

```text
UserDiagnosticMarker
```

It explicitly states that the marker is not evidence of a crash, hardware error or driver failure.

Because CrashScope preserves post-trigger telemetry, completion takes roughly 30 seconds.

## 10. Persistent data and retention

CrashScope stores its local application data under:

```text
%LOCALAPPDATA%\CrashScope
```

SQLite persists structured incidents, workload sessions, session/incident associations and environment snapshots. Settings are stored separately in `settings.json`.

History retention defaults to 30 days and can be changed from Everyday Mode using simple 7, 30, 90, 180 or 365-day presets. Cleanup runs once when the Agent starts rather than on a recurring background timer.

Retention never deletes an active/unclosed session. An old incident is also retained while a retained session still references it. This keeps cleanup bounded without breaking the evidence relationships that make old sessions interpretable.

Deleting the portable program directory does not automatically remove the data under `%LOCALAPPDATA%\CrashScope`.

## 11. Privacy-safe support bundles

Incident Detail includes a support-bundle flow intended to replace manually collecting Event Viewer, WER and assorted diagnostic files.

Choose **Share safely** / **Create support bundle** to preview what will be included before downloading anything.

A support bundle contains only curated text/JSON such as:

- README.txt
- manifest.json
- incident.json
- related session.json when available
- runtime.json

CrashScope does **not** include the SQLite database, crash dumps, ETL traces, raw WER folders, arbitrary logs or arbitrary environment variables by default.

Exported text applies defense-in-depth redaction for items including the current user-profile path, username, machine name, email addresses, IPv4/IPv6 addresses, MAC addresses and obvious credential-style values such as bearer tokens or named `api_key`, `access_token`, `token`, `password` and `secret` assignments.

Redaction is intentionally described as defense-in-depth, not a mathematical guarantee for arbitrary free-form text. Preview the bundle before sharing it.

CrashScope creates the ZIP in memory for the browser download. It does not automatically upload the bundle and does not keep a permanent server-side archive.

## 12. Notification-area controls and Start with Windows

On an interactive Windows desktop, CrashScope uses a small native notification-area control surface rather than keeping a second desktop UI framework alive.

Left-clicking the icon opens the localhost dashboard. The context menu provides:

- current Ready / Monitoring status
- Open dashboard
- Auto Assist on/off
- Start with Windows on/off
- Exit CrashScope

Newly captured real incidents can produce a Windows notification. The safe `UserDiagnosticMarker` does not generate an incident notification, and CrashScope asks Windows to respect quiet-time behavior rather than forcing a notification through it.

`Start with Windows` is opt-in. When enabled, CrashScope uses only the current user's Windows Run key and launches with:

```text
--no-browser
```

This requires no Administrator access and does not install a Windows service, scheduled task or machine-wide startup entry.

If the notification area is unavailable, such as in a headless Windows session, the tray feature can remain unavailable while the Agent continues monitoring normally.

## 13. Privacy and networking

CrashScope is local-first by design:

- loopback-only HTTP/WebSocket listeners
- no account
- no cloud backend
- no analytics
- no automatic uploads
- no LAN listener
- no normal Administrator requirement
- browser requests restricted to CrashScope's trusted localhost origins
- native local tools without an `Origin` header remain supported
- diagnostic/UI responses use restrictive security headers and `Cache-Control: no-store`

CrashScope can still contain locally sensitive diagnostic information. Review screenshots and support bundles before posting them publicly.

## 14. Getting help

See [`troubleshooting.md`](troubleshooting.md) first.

Useful bug reports include:

- CrashScope version
- Windows version
- CPU model
- GPU model
- GPU driver version
- workload involved
- expected behavior
- observed behavior
- unavailable sensors, if relevant
- whether Auto Assist was enabled
- a CrashScope support bundle when appropriate and reviewed

Do not deliberately create unsafe hardware failures just to test CrashScope.
