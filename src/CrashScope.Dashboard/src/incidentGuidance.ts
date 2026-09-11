import type { IncidentReport } from './types';

export type IncidentGuidance = {
  meaning: string;
  doesNotProve: string;
  nextChecks: string[];
  context: string[];
};

function telemetryContext(report: IncidentReport): string[] {
  const notes: string[] = [];
  const { telemetry } = report;

  if (telemetry.peakGpuHotspotCelsius !== null && telemetry.peakGpuHotspotCelsius >= 95) {
    notes.push(`GPU hotspot reached ${telemetry.peakGpuHotspotCelsius.toFixed(0)}°C in the captured window. Compare this with a healthy run; temperature alone does not establish the cause.`);
  }
  if (telemetry.peakSystemMemoryLoadPercent !== null && telemetry.peakSystemMemoryLoadPercent >= 90) {
    notes.push(`System memory load reached ${telemetry.peakSystemMemoryLoadPercent.toFixed(0)}%. Memory pressure may be relevant context, but it is not proof of why the incident occurred.`);
  }
  if (telemetry.peakGpuUtilizationPercent !== null && telemetry.peakGpuUtilizationPercent >= 95) {
    notes.push(`GPU utilization reached ${telemetry.peakGpuUtilizationPercent.toFixed(0)}%. High utilization is normal in many games and render workloads, so CrashScope treats it as context rather than a fault.`);
  }

  return notes;
}

export function getIncidentGuidance(report: IncidentReport): IncidentGuidance {
  const context = telemetryContext(report);

  switch (report.classification) {
    case 'KernelOrDriverWatchdog':
      return {
        meaning: 'Windows recorded low-level graphics, driver, or watchdog evidence near this incident. This is more specific than an ordinary application exit and is worth testing as a graphics-stack stability problem.',
        doesNotProve: 'It does not prove that the GPU hardware is defective, that VRAM caused the failure, or that the driver is the sole cause.',
        nextChecks: [
          'If the issue began after a GPU driver change, compare with the previous known-good driver or perform a clean vendor-supported driver reinstall.',
          'Temporarily return GPU overclock, undervolt, power-limit, and memory tuning to stock, then retry the same workload.',
          'Reproduce the same workload with CrashScope running and compare whether the same watchdog/display evidence appears again.',
          'If only one game fails, test another demanding GPU workload before treating the problem as system-wide.',
        ],
        context,
      };

    case 'HardwareError':
      return {
        meaning: 'Windows recorded hardware-error evidence near the incident. Repeated errors from the same source are more useful than a single isolated event.',
        doesNotProve: 'A hardware-error record does not automatically identify a failed component unless the Windows evidence itself names one clearly.',
        nextChecks: [
          'Return CPU, RAM, GPU, and memory-controller tuning to known-stable settings before reproducing the issue.',
          'Check whether later incidents repeat the same Windows hardware-error source and identifiers.',
          'Compare temperatures and workload conditions with a healthy session before replacing hardware.',
          'If the same hardware-error evidence repeats at stock settings, investigate the named subsystem with a focused stability test.',
        ],
        context,
      };

    case 'UnexpectedShutdown':
      return {
        meaning: 'Windows recorded an abnormal shutdown/power transition. This confirms that Windows did not complete a normal shutdown sequence.',
        doesNotProve: 'Kernel-Power evidence by itself does not prove a bad PSU, overheating, a GPU fault, or a power outage.',
        nextChecks: [
          'Note whether the PC powered off, rebooted, froze, or required a manual reset; that distinction matters.',
          'Compare temperatures and power-related telemetry immediately before the event with a normal workload session.',
          'Temporarily remove unstable overclocks/undervolts and reproduce the same workload at stock settings.',
          'Check whether nearby watchdog, WHEA, application-fault, or display-driver evidence gives the shutdown more context.',
        ],
        context,
      };

    case 'ApplicationFailure':
      return {
        meaning: 'The strongest nearby evidence currently points to the application/process rather than a lower-level Windows hardware or watchdog event.',
        doesNotProve: 'An application-level failure does not prove the PC hardware is healthy; it only means CrashScope did not correlate stronger low-level evidence in this window.',
        nextChecks: [
          'Retry the same application once with the same settings and see whether the failure is reproducible.',
          'If the application is a game, verify its files and temporarily disable recently added mods, injectors, or overlays.',
          'Compare recent application, game, runtime, and GPU-driver changes with the last known-good session.',
          'If other demanding applications also fail, treat the problem as broader than this one application and compare their CrashScope incidents.',
        ],
        context,
      };

    case 'Mixed':
      return {
        meaning: 'CrashScope found more than one category of evidence in the same correlation window. The events may describe one failure chain, but their order and relationship matter.',
        doesNotProve: 'Multiple nearby events do not prove that the first-looking event caused the others.',
        nextChecks: [
          'Read the evidence timeline in time order and look for the earliest specific event rather than the final shutdown symptom.',
          'Reproduce the same workload and check whether the same combination and ordering appears again.',
          'Return relevant CPU/GPU/RAM tuning to stock before the next comparison run.',
          'Prioritize repeated watchdog or WHEA evidence over generic shutdown records when deciding what subsystem to test next.',
        ],
        context,
      };

    case 'UserDiagnosticMarker':
      return {
        meaning: 'This is a user-requested safe marker used to prove that CrashScope can preserve telemetry and diagnostic context end-to-end.',
        doesNotProve: 'The marker is not evidence of a crash, hardware fault, driver problem, or system instability.',
        nextChecks: ['No troubleshooting is required. If the marker contains telemetry and evidence as expected, the diagnostic pipeline is working.'],
        context: [],
      };

    default:
      return {
        meaning: 'CrashScope captured the trigger, but the nearby evidence is not yet specific enough for a stronger classification.',
        doesNotProve: 'A lack of specific evidence does not prove that nothing failed; some failures leave little or delayed Windows evidence.',
        nextChecks: [
          'Keep CrashScope running and reproduce the same problem so a second incident can be compared.',
          'Record what you saw on screen: crash to desktop, freeze, black screen, reboot, or full power-off.',
          'Avoid changing several drivers/settings at once; one controlled change makes the next incident more informative.',
        ],
        context,
      };
  }
}
