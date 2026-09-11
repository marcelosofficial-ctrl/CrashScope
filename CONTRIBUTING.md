# Contributing to CrashScope

CrashScope is an evidence-first, low-overhead Windows diagnostics project. Contributions are welcome when they preserve those two priorities.

## Development environment

- Windows 11 recommended
- .NET 10 SDK
- Node.js 24+
- Git

## Build and test

From the repository root:

```powershell
cd .\src\CrashScope.Dashboard
npm.cmd ci --no-audit --no-fund
npm.cmd run build
cd ..\..
dotnet build CrashScope.sln -c Release
dotnet test CrashScope.sln -c Release --no-build
```

Use the committed npm lockfile as the authoritative dependency graph. `npm ci` is preferred for reproducible local and CI builds.

## Engineering principles

Changes should preserve these rules:

1. **Evidence before conclusions.** Do not turn correlation into unsupported root-cause claims.
2. **Performance is a product requirement.** Avoid extra polling loops, busy waits, unbounded queues, or UI-driven hardware reads.
3. **Missing telemetry is explicit.** Do not replace unavailable or invalid readings with fake zeroes.
4. **Least privilege by default.** Do not require Administrator access unless a feature genuinely cannot work otherwise.
5. **Local-first privacy.** Do not add mandatory accounts, analytics, cloud dependencies, or automatic diagnostic uploads.
6. **Stable process identity.** Treat PID + process start time as the identity; PID alone is insufficient.
7. **Vendor-neutral core.** Hardware-provider implementation types must not leak into CrashScope.Core contracts.

## Pull requests

A focused PR should normally include:

- a clear problem statement
- tests for behavior that can be tested deterministically
- no unrelated formatting churn
- documentation changes when public behavior changes
- a note about performance impact for sampling, persistence, diagnostics, or streaming changes
- cautious wording for new incident classifications or assessments

Hardware-specific changes should include the exact hardware/driver/Windows environment used for validation.

## Performance-sensitive changes

If a change touches sampling, Event Log scanning, WebSocket delivery, persistence, or hardware providers, compare behavior against the performance budget in `docs/performance-budget.md`.

Do not add a noisy micro-performance assertion to CI just to enforce machine-specific timing. Prefer deterministic architecture tests plus documented real-machine measurements.

## Diagnostic evidence contributions

When adding a Windows evidence source or parser:

- preserve source occurrence time separately from observation/collection time when possible
- document deduplication keys
- do not deduplicate solely on a broad signature that could collapse unrelated incidents
- include tests for stale/replayed Windows evidence
- distinguish an abnormal transition from evidence explaining why it occurred

## Sensitive test data

Never commit personal crash dumps, WER folders, ETL traces, private machine exports, tokens, credentials, or other personal diagnostic artifacts. Use sanitized fixtures or synthetic minimal test data instead.
