# Hardware validation matrix

CrashScope is architected to be vendor-neutral, but architecture support and real-machine validation are deliberately tracked separately.

## Validation states

- **Validated** — exercised end-to-end on real hardware with CrashScope telemetry/runtime behavior checked.
- **Partial** — some telemetry paths validated but known metrics remain unavailable or unverified.
- **Architecture-ready** — provider/core design supports the category, but no real-machine claim is made yet.
- **Not tested** — no current evidence of support.

## Current matrix

| Component | Hardware / platform | State | Notes |
| --- | --- | --- | --- |
| OS | Windows 11 | Validated | Primary development and runtime target. |
| CPU | AMD Ryzen 5 7500F | Partial | Total + logical utilization validated. Temperature/power/clock values are unavailable through the current least-privilege sensor path and remain explicit unavailable metrics. |
| GPU | AMD Radeon RX 9070 XT | Validated | Utilization, temperatures, hotspot, power, clocks, VRAM and fan data validated end-to-end. |
| GPU | NVIDIA GeForce | Architecture-ready | Core contracts are vendor-neutral; real-machine provider validation remains required before claiming public support. |
| GPU | Intel Arc / Intel GPU | Architecture-ready | Core contracts are vendor-neutral; real-machine validation remains required. |
| CPU | Intel Core | Architecture-ready | Core/session/environment design is vendor-neutral; real-machine validation remains required. |
| Memory | Windows system memory | Validated | Used/available/load telemetry validated on the primary system. |
| Windows diagnostics | Application Error / Hang | Validated | Historical real-machine evidence found and parser/runtime paths tested. |
| Windows diagnostics | WER / LiveKernel / watchdog | Validated | Real WER metadata, watchdog paths, LiveKernelEvent 141/193 history and source-time handling validated. |
| Windows diagnostics | Kernel-Power 41 | Validated | Treated as evidence of an abnormal transition, not proof of root cause. |
| Windows diagnostics | WHEA | Parser/runtime validated | No WHEA event was present in the original 30-day real-machine sample; deterministic tests cover classification behavior. |

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

Until NVIDIA/Intel real-hardware validation is complete, public materials should say CrashScope is **designed for vendor-neutral telemetry** and that AMD Radeon RX 9070 XT / Ryzen 5 7500F is the primary validated development system. Avoid claiming comprehensive NVIDIA/Intel support prematurely.
