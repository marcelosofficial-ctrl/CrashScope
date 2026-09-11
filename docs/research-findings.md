# Phase 0 Research Findings

## Hardware telemetry

LibreHardwareMonitor successfully exposed useful RX 9070 XT telemetry including utilization, temperatures, hotspot, VRAM, clocks, package power, voltage, and fan state.

Ryzen 5 7500F utilization worked, while CPU temperature, package power, and several clock readings were invalid or unavailable.

This established a core rule:

> A discovered sensor is not automatically a valid sensor.

## Privileges

Administrator mode did not materially improve the useful telemetry set, so CrashScope remains unelevated by default.

## Windows diagnostics

Useful evidence was confirmed from Application Error, Application Hang, Windows Error Reporting, LiveKernelEvent, Kernel-Power, and WHEA when present.

Windows Error Reporting can expose watchdog dump paths even when direct enumeration of the protected LiveKernelReports directory is denied.

## Deduplication

Windows can emit the same underlying LiveKernelEvent more than once. CrashScope must separate event observation time from underlying artifact identity and avoid creating duplicate incidents.