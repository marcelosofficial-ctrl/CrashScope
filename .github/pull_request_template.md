## Summary

Describe the problem and the change.

## Why this belongs in CrashScope

Explain how this improves diagnostics, reliability, performance, portability, or maintainability without weakening the local-first/evidence-first design.

## Validation

- [ ] Dashboard build succeeds if frontend code changed.
- [ ] .NET solution builds successfully.
- [ ] Relevant automated tests added/updated.
- [ ] Full test suite passes.
- [ ] No machine-specific diagnostic artifacts or sensitive files were committed.

## Performance impact

For sampling, persistence, Windows diagnostics, hardware providers, or streaming changes, describe expected overhead impact and any measurements performed.

## Diagnostic wording

If this changes incident classification/assessment:

- [ ] observations remain separate from causation
- [ ] no unsupported component-level root-cause claim was introduced
- [ ] stale/replayed Windows evidence behavior was considered

## Hardware validation

If hardware-specific:

- CPU:
- GPU:
- Windows version:
- driver version:
- normal/elevated execution:
- metrics validated/unavailable:

## Screenshots

Include screenshots for meaningful dashboard/UI changes when useful.
