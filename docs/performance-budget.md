# Performance Budget

Performance is a product requirement. CrashScope must not materially interfere with the workload it is measuring.

Initial engineering targets:

- background sampling: 0.5 Hz
- active session sampling: 1 Hz
- average Agent CPU: <= 0.5%
- stretch CPU target: <= 0.25%
- working set: <= 100 MB
- sustained GPU render/compute usage: effectively zero
- intentional VRAM allocation: effectively zero
- no busy loops
- no required external network traffic

Short LibreHardwareMonitor research runs on the development machine measured roughly 7 ms average refresh time, 0.07% to 0.11% process CPU, and 41 to 42 MB working set.

## Real Agent baselines

The following are development-machine measurements, not universal release-wide guarantees. They are used to detect regressions as features are added.

### Current sampler baseline

A direct real-machine check of the current Agent was run on the primary development system with:

- Ryzen 5 7500F
- 12 logical processors
- CrashScope Agent listening on port 5077
- background sampling at 0.5 Hz
- active sampling at 1 Hz
- 20-second measurement windows after a 3-second mode-settle delay
- CPU measured from `TotalProcessorTime`, normalized by wall time and logical-processor count
- working-set and private-memory samples collected once per second
- no incidents recorded during the measurement

| Mode | Average CPU | Average working set | Peak working set | Average private memory | Peak private memory |
| --- | ---: | ---: | ---: | ---: | ---: |
| Background | **0.0577%** | **76.12 MB** | **77.74 MB** | **29.98 MB** | **31.70 MB** |
| Active | **0.0578%** | **75.31 MB** | **75.87 MB** | **29.21 MB** | **30.51 MB** |

Interpretation:

- both measured modes were roughly one ninth of the 0.5% CPU budget
- both were comfortably below the stricter 0.25% stretch CPU target
- average and peak working set remained below the 100 MB budget
- active 1 Hz sampling did not produce a measurable CPU increase in this short run
- this is a point-in-time development-machine baseline, not a universal hardware guarantee
- longer-duration and workload-attached measurements remain useful before treating small differences as meaningful

### Historical SQLite checkpoint

An earlier measurement after incident-only SQLite persistence was added recorded:

- background average CPU: 0.0641%
- active average CPU: 0.1413%
- background average working set: 74.50 MB
- active average working set: 74.99 MB
- peak working set: 76.31 MB background, 76.04 MB active
- average private memory: 29.00 MB background, 28.84 MB active
- test suite at that checkpoint: 88 passed, 0 failed

Interpretation of that historical checkpoint:

- background CPU remained effectively unchanged and was far below the 0.5% budget
- the single active-mode measurement increased but remained well below the 0.5% budget and was never treated as proof that SQLite caused the increase
- memory remained below the 100 MB budget
- SQLite does not perform periodic writes between incidents; continuous telemetry remains memory-only
- the newer sampler measurement shows the earlier 0.1413% active-mode result was not a stable characteristic of active sampling

### Dashboard-open milestone

A separate validation with the live dashboard connected recorded approximately:

- average Agent CPU: 0.0682%
- average working set: 91.09 MB
- peak working set: 92.41 MB
- average private memory: 35.69 MB
- 1 live WebSocket subscriber
- 0 stale-frame drops
- 0 delivery misses

The dashboard consumes the existing telemetry stream and does not create another hardware polling loop.

### Final integrated next-beta gate

The final normal-user Windows combined next-beta gate on the primary reference machine validated exact product head `9afb5418db9293a7fc9be0cc68be018ee4d89ff0` and recorded a 30-second Agent performance sample:

- average CPU: **0.2657%**
- average working set: **93 MB**
- peak working set: **95.83 MB**
- average private memory: **35.08 MB**
- stream frames published: 16
- stale-frame drops: 0
- stream delivery misses: 0

This sample covered the integrated product path rather than an isolated sampler loop and still remained within the <=0.5% average CPU and around <=100 MB working-set budgets. It is above the 0.25% stretch CPU target by a small margin, which is acceptable for the current release budget but remains useful regression evidence for future optimization.

These measurements are development/reference-machine baselines, not universal hardware guarantees. Repeat them after major persistence, diagnostics, telemetry, dashboard, or release changes rather than inferring performance from synthetic benchmarks alone.
