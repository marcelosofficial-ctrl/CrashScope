import type { AgentStatus, WorkloadSession } from './types';
import RetentionControl from './RetentionControl';
import StartupControl from './StartupControl';

type EverydayStatusPanelProps = {
  status: AgentStatus | null;
  activeSession: WorkloadSession | null;
  changingAutoAssist: boolean;
  onToggleAutoAssist: () => void;
};

function relativePause(value: string | null): string {
  if (!value) return '';
  const remaining = Math.max(0, new Date(value).getTime() - Date.now());
  const minutes = Math.ceil(remaining / 60000);
  return minutes <= 1 ? 'for about a minute' : `for about ${minutes} minutes`;
}

export default function EverydayStatusPanel({
  status,
  activeSession,
  changingAutoAssist,
  onToggleAutoAssist,
}: EverydayStatusPanelProps) {
  const state = status?.automaticMonitoringState ?? 'Ready';
  const autoAssistEnabled = status?.automaticMonitoringEnabled !== false;
  const isMonitoring = Boolean(activeSession);
  const isConfirming = !isMonitoring && state === 'Confirming';
  const isPaused = !isMonitoring && state === 'PausedByUser';
  const isDisabled = !isMonitoring && !autoAssistEnabled;

  let title = 'CrashScope is ready';
  let description = 'Use your PC normally. CrashScope watches quietly in the background and can recognize strong game, benchmark, rendering, and local-AI workloads automatically.';
  let kicker = 'EVERYDAY MODE';
  let tone = 'ready';

  if (isMonitoring) {
    kicker = 'MONITORING NOW';
    title = `Watching ${activeSession!.processName}`;
    description = autoAssistEnabled
      ? 'Active sampling is enabled and CrashScope is preserving telemetry around any Windows diagnostic evidence that appears.'
      : 'This active session keeps running even though Auto Assist is off. Disabling automation never interrupts a workload already being monitored.';
    tone = 'monitoring';
  } else if (isDisabled) {
    kicker = 'AUTO ASSIST OFF';
    title = 'Automatic workload detection is off';
    description = 'CrashScope still keeps background telemetry and Windows diagnostic evidence available, but it will not attach to foreground games or workloads automatically.';
    tone = 'paused';
  } else if (isConfirming) {
    kicker = 'AUTO ASSIST';
    title = `Recognizing ${status?.automaticCandidateProcessName ?? 'foreground workload'}`;
    description = 'CrashScope waits for the same process identity twice before attaching automatically, reducing false positives from launchers and short-lived helpers.';
    tone = 'confirming';
  } else if (isPaused) {
    kicker = 'AUTO ASSIST PAUSED';
    title = 'Manual stop respected';
    description = `Automatic re-attachment is paused ${relativePause(status?.automaticSuppressedUntilUtc ?? null)} so CrashScope does not fight your choice.`;
    tone = 'paused';
  }

  return (
    <section className={`everyday-status everyday-status--${tone}`} aria-label="CrashScope everyday monitoring status">
      <div className="everyday-status__signal" aria-hidden="true"><span /></div>
      <div className="everyday-status__copy">
        <p className="eyebrow">{kicker}</p>
        <h2>{title}</h2>
        <p>{description}</p>
        <p className="everyday-status__quiet-note">
          <strong>You can close this tab.</strong> The CrashScope Agent keeps monitoring without the browser, which saves the browser's CPU/GPU/RAM overhead while you play.
        </p>
      </div>
      <div className="everyday-status__facts" aria-label="Automatic monitoring details">
        <div className="everyday-status__fact-control">
          <span>Auto Assist</span>
          <button
            type="button"
            className={`toggle-button${autoAssistEnabled ? ' toggle-button--on' : ''}`}
            aria-pressed={autoAssistEnabled}
            disabled={!status || changingAutoAssist}
            onClick={onToggleAutoAssist}
          >
            {changingAutoAssist ? 'Saving…' : autoAssistEnabled ? 'On' : 'Off'}
          </button>
        </div>
        <div>
          <span>Foreground check</span>
          <strong>{autoAssistEnabled && status ? `${status.automaticObservationIntervalSeconds}s` : 'Paused'}</strong>
        </div>
        <div>
          <span>Hardware reads</span>
          <strong>No extra loop</strong>
        </div>
        <StartupControl />
        <RetentionControl />
      </div>
    </section>
  );
}
