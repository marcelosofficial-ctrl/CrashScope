# CrashScope Everyday Mode research

This document records the product and engineering direction for turning CrashScope into a quiet Windows companion that can remain useful every day without becoming another heavy monitoring overlay.

## Product target

A user should be able to:

1. download CrashScope,
2. launch `CrashScope.exe`,
3. understand immediately that monitoring is ready,
4. close the browser if they want maximum gaming headroom,
5. use the PC normally,
6. have strong game/benchmark/local-AI workloads recognized automatically,
7. return after a crash, hang, driver reset, hardware error or suspicious shutdown,
8. see an evidence-first explanation of what was observed,
9. follow safe next checks, and
10. optionally download a sanitized support bundle for manual sharing.

The product should not require users to understand Event Viewer, WER, LiveKernelReports, PID reuse, telemetry logging or Windows diagnostic timing.

## What has already moved from research into the product

Several original research directions are now implemented on `integration/next-beta`.

### Event-driven Windows diagnostics

CrashScope now uses real-time Event Log subscriptions for relevant providers on the normal supported path.

A bounded in-memory ring stores recent relevant records, the incident pipeline can wake immediately on new evidence, and sparse reconciliation remains as a safety net for sleep/resume, missed delivery or restricted environments.

The design intentionally avoids a broad always-on ETW session and does not add a hardware polling loop.

### Event-driven active-process exit

An attached workload no longer requires a normal one-second PID polling loop. CrashScope waits asynchronously on the selected process and falls back to sparse probing only when the process handle cannot support the preferred path.

PID plus process-start-time identity remains the source of truth, and stale observations are prevented from completing replacement sessions.

### Conservative Auto Assist

Automatic monitoring is intentionally not “magic.” It uses the existing classifier and requires a strongly recommended workload plus stable PID/start-time identity before attachment.

Current policy includes:

- foreground-process observation rather than repeated full process-table enumeration,
- Recommended workload classification,
- conservative score threshold,
- rejection of generic applications and helper/system processes,
- two-observation confirmation,
- manual session priority,
- manual-stop cooldown,
- no additional hardware reads.

### Persistent Auto Assist control

Auto Assist can now be disabled and re-enabled at runtime and the preference persists in a versioned local settings file.

Disabling automatic monitoring prevents future auto-attaches but does not interrupt an already active monitored workload.

### Privacy-safe support bundle flow

Incident Detail can preview and download a curated support ZIP without automatic upload or server-side archive persistence.

The bundle excludes the database, crash dumps, ETL, raw WER folders, arbitrary logs and arbitrary environment variables by default.

Defense-in-depth redaction includes user/profile/machine identifiers, email, IPv4/IPv6, MAC addresses and obvious credential-style values. The UI still tells users to review the preview because arbitrary free-form text cannot honestly be guaranteed perfectly anonymous.

### Safe per-user startup foundation

The tested startup foundation uses the current user's Windows Run key only and launches CrashScope with `--no-browser`.

It requires no Administrator rights, Windows service or machine-wide startup configuration. The user-facing toggle should only appear after its full behavior is composed and validated.

## Power and overhead rules

These remain product invariants:

1. One hardware read per central sampling tick.
2. Background sampling remains 0.5 Hz until measurements justify a change.
3. Active workload sampling remains 1 Hz.
4. No dashboard-triggered sensor polling.
5. No continuous full process enumeration for automatic mode.
6. Closing the browser must leave the Agent fully functional.
7. No OSD or in-process game injection in the default product.
8. No always-on PresentMon/ETW frame capture without measured justification.
9. Prefer event subscriptions to short recurring diagnostic polls.
10. Any new provider must be measured with the dashboard open and closed.
11. Repeatable performance regressions are release blockers.
12. Missing sensors remain unavailable, never fake zero.

The reference-machine goal remains at most 0.5% average Agent CPU with a stretch target of 0.25%, roughly 100 MB or less working set, no sustained GPU use and effectively zero intentional VRAM.

## UX hierarchy

The default experience should answer these questions in order:

1. **Is CrashScope ready or monitoring me now?**
2. **Did something important happen?**
3. **What should I do next?**
4. What are the live vitals?
5. What are the advanced internals?

Dense sensor data is intentionally secondary to actionability.

Everyday copy should make it clear that the browser tab can be closed and that the Agent continues monitoring without it.

## Incident intelligence direction

Incident detail currently uses:

1. Observed
2. Meaning
3. What this does not prove
4. Next checks

The next useful intelligence improvements should remain deterministic and evidence-first.

Promising directions include:

- compare an incident against recent healthy sessions for the same workload,
- identify recent driver/environment changes,
- distinguish repeated evidence patterns from one-off events,
- improve no-dump/no-event crash handling,
- preserve explicit uncertainty rather than inventing a root cause.

AI-generated causal explanations should not replace the deterministic evidence model.

## Tray direction

A future lightweight notification-area control surface remains desirable because the browser should not need to stay open while gaming.

Preferred architecture is a minimal Win32 `Shell_NotifyIcon`-style presence rather than Electron or a heavyweight desktop UI kept alive only for a tray icon.

Desired tray states:

- CrashScope — Ready
- CrashScope — Monitoring <workload>
- CrashScope — Incident captured

Desired actions:

- Open CrashScope
- Pause/Resume Auto Assist
- Start with Windows
- Exit

Notifications should be reserved for meaningful incidents or explicit user actions, not routine telemetry.

## Retention direction

Persistent history must not grow without bound.

Retention should be deterministic and testable, preserve important incidents, and expose user controls only after the actual cleanup behavior exists.

Potential policy dimensions include:

- incident age,
- unassociated routine session age,
- maximum retained low-value sessions,
- preserving sessions associated with incidents,
- safe cleanup at startup or sparse maintenance intervals rather than a busy background loop.

## Native WER store research

A future evidence improvement is read-only metadata access to the native Windows Error Reporting store.

Research should begin with metadata-only enumeration using documented Windows APIs where practical. Goals:

- normal-user access,
- no dump bytes automatically,
- preserve Report ID, time, signature and artifact references,
- integrate with the existing dedup/time model,
- measure overhead before enabling anything continuously.

This should complement, not bypass, the existing Event Log and WER evidence semantics.

## Provider roadmap

LibreHardwareMonitor remains the broad fallback provider.

CrashScope-owned interfaces should stay above vendor adapters so a better provider can be used where validated without contaminating Core with vendor-specific types.

### AMD

ADLX is promising for usage, VRAM, clocks, fan, hotspot/edge temperature and power. It should be adopted only after consumer coverage and overhead are measured on real hardware.

### NVIDIA

NVML/NVAPI research should focus on real GeForce coverage for utilization, temperature, memory, power and relevant process information. Supported fields must be labeled from actual validation rather than assumed from data-center documentation.

### Intel

Dedicated provider research remains later work and must follow the same capability-based approach.

### PresentMon

PresentMon is relevant for optional on-demand frame-time and stutter diagnostics. It should not become part of default always-on monitoring until its cost is measured and the user value clearly justifies it.

## Release sequencing

The preferred sequence remains:

1. keep frozen `main` unchanged until the v0.1 release decision,
2. continue integrating tested post-v0.1 work through `integration/next-beta`,
3. finish tray/startup composition and retention,
4. research native WER metadata access,
5. produce one combined next-beta CI artifact,
6. perform one consolidated real-machine validation,
7. then decide whether to tag/release frozen v0.1 and how to expose the next beta publicly.

The consolidated real-machine gate should exercise:

- RX 9070 XT telemetry,
- Auto Assist on/off persistence,
- safe diagnostic marker,
- support-bundle preview/download/privacy,
- event-driven Windows diagnostics,
- event-driven process exit,
- browser-open and browser-closed performance,
- stream health,
- loopback-only security,
- second-instance behavior,
- startup behavior once fully exposed,
- final visual polish.

## Success criteria

A strong CrashScope beta should be meaningfully better than telling a user to open Event Viewer plus a separate hardware logger:

- launch once and understand the product quickly,
- no manual setup required for recognized workloads,
- no normal Administrator requirement,
- very low background overhead,
- browser can be closed during gaming,
- incidents preserve useful telemetry plus Windows evidence,
- reports distinguish observation from hypothesis,
- next checks are safe and reversible,
- support bundles are small and privacy-conscious by default,
- additional hardware support is based on real validation rather than marketing claims.
