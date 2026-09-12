# Hardware validation matrix

CrashScope is architected to be vendor-neutral, but architecture support and real-machine validation are deliberately tracked separately.

## Validation states

- **Validated** — exercised end-to-end on real hardware with CrashScope telemetry/runtime behavior checked.
- **Partial** — real-machine behavior was exercised, but metric coverage is incomplete or not broad enough for a full support claim.
- **Architecture-ready** — provider/core design supports the category, but no real-machine claim is made yet.
- **Not tested** — no current evidence of support.

## Current matrix

| Component | Hardware / platform | State | Notes |
| --- | --- | --- | --- |
| OS | Windows 11 | Validated | Primary development and runtime target. |
| OS | Windows 10 | Validated | Secondary real-machine installation/runtime validation passed on the Intel test laptop. |
| CPU | AMD Ryzen 5 7500F | Partial | Total + logical utilization validated. Temperature/power/clock values are unavailable through the current least-privilege sensor path and remain explicit unavailable metrics. |
| GPU | AMD Radeon RX 9070 XT | Validated | Utilization, temperatures, hotspot, power, clocks, VRAM and fan data validated end-to-end. |
| CPU | Intel Core i5-3210M | Partial | Secondary real-machine CrashScope validation passed on Windows 10. Comprehensive Intel sensor coverage is not claimed from this one system. |
| GPU | Intel HD Graphics 4000 | Partial | Secondary real-machine CrashScope validation passed on Windows 10. This does not imply full Intel Arc / modern Intel telemetry coverage. |
| GPU | NVIDIA GeForce | Architecture-ready | Real-machine provider validation remains required before claiming public support. |
| GPU | Intel Arc / newer Intel GPU | Architecture-ready | Broader modern Intel GPU telemetry validation remains required. |
| CPU | Other Intel Core | Architecture-ready | Broader Intel CPU telemetry validation remains required. |
| Memory | Windows system memory | Validated | Used/available/load telemetry validated on the primary system. |
| Windows diagnostics | Application Error / Hang | Validated | Historical real-machine evidence found and parser/runtime paths tested. |
| Windows diagnostics | WER / LiveKernel / watchdog | Validated | Real WER metadata, watchdog paths, LiveKernelEvent 141/193 history and source-time handling validated. |
| Windows diagnostics | Kernel-Power 41 | Validated | Treated as evidence of an abnormal transition, not proof of root cause. |
| Windows diagnostics | WHEA | Parser/runtime validated | Deterministic tests cover classification behavior; no WHEA event was present in the original real-machine sample. |

## Adding a hardware validation result

Record:

- exact CPU/GPU model
- Windows version
- GPU driver version
- normal or Administrator execution
- which metrics are available, unavailable, or suspicious
- CrashScope version/commit
- whether workload monitoring, session persistence, live dashboard, and safe diagnostic marker capture were exercised
- measured Agent CPU/working-set impact if practical

Do not change a row to **Validated** solely because a library or vendor API theoretically supports the device.

## Public support wording

Public materials may state that CrashScope has primary AMD validation plus genuine secondary Windows 10 validation on an Intel Core i5-3210M / Intel HD Graphics 4000 machine. They must still distinguish that compatibility evidence from comprehensive modern Intel telemetry coverage. NVIDIA real-hardware validation remains outstanding and must not be claimed until it is actually completed.
