import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  attachCandidate,
  captureDiagnosticMarker,
  createLiveSocket,
  getActiveSession,
  getIncidents,
  getLatestTelemetry,
  getSessions,
  getStatus,
  getWorkloads,
  setAutoAssist,
  stopActiveSession,
} from './api';
import ConfigTraceControl from './ConfigTraceControl';
import EverydayStatusPanel from './EverydayStatusPanel';
import IncidentPanel from './IncidentPanel';
import type {
  AgentStatus,
  IncidentReport,
  LiveMessage,
  MetricReading,
  TelemetryFrame,
  WorkloadCandidate,
  WorkloadSession,
} from './types';

type StreamState = 'connecting' | 'live' | 'offline';
type HistoryPoint = { cpu: number | null; gpu: number | null; memory: number | null };

const MAX_HISTORY = 60;

function readingValue(reading?: MetricReading): number | null {
  return reading?.isAvailable && reading.value !== null ? reading.value : null;
}

function formatNumber(value: number | null | undefined, digits = 0): string {
  return value === null || value === undefined || !Number.isFinite(value) ? '—' : value.toFixed(digits);
}

function formatMiB(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—';
  return value >= 1024 ? `${(value / 1024).toFixed(1)} GiB` : `${value.toFixed(0)} MiB`;
}

function formatDate(value: string | null | undefined): string {
  if (!value) return '—';
  return new Intl.DateTimeFormat(undefined, {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit',
  }).format(new Date(value));
}

function duration(session: WorkloadSession): string {
  const end = session.endedAtUtc ? new Date(session.endedAtUtc).getTime() : Date.now();
  const total = Math.max(0, Math.round((end - new Date(session.startedAtUtc).getTime()) / 1000));
  const minutes = Math.floor(total / 60);
  const seconds = total % 60;
  return minutes ? `${minutes}m ${seconds}s` : `${seconds}s`;
}

function Sparkline({ values }: { values: Array<number | null> }) {
  const points = useMemo(() => {
    const usable = values.map((value) => value ?? 0);
    if (usable.length < 2) return '';
    const max = Math.max(100, ...usable);
    return usable.map((value, index) => {
      const x = (index / (usable.length - 1)) * 100;
      const y = 34 - (value / max) * 30;
      return `${x.toFixed(2)},${y.toFixed(2)}`;
    }).join(' ');
  }, [values]);

  return (
    <svg className="sparkline" viewBox="0 0 100 36" preserveAspectRatio="none" aria-hidden="true">
      <polyline points={points} fill="none" vectorEffect="non-scaling-stroke" />
    </svg>
  );
}

function MetricCard({ label, value, suffix, detail, history }: {
  label: string;
  value: number | null;
  suffix: string;
  detail: string;
  history?: Array<number | null>;
}) {
  return (
    <article className="metric-card">
      <div className="metric-card__topline">
        <span>{label}</span>
        <span className={value === null ? 'metric-state metric-state--muted' : 'metric-state'}>
          {value === null ? 'Unavailable' : 'Live'}
        </span>
      </div>
      <div className="metric-card__value">
        {value === null ? 'N/A' : formatNumber(value, value < 10 ? 1 : 0)}<span>{value === null ? '' : suffix}</span>
      </div>
      <p>{detail}</p>
      {history ? <Sparkline values={history} /> : <div className="sparkline-placeholder" />}
    </article>
  );
}

function Badge({ children, tone = 'neutral' }: {
  children: React.ReactNode;
  tone?: 'good' | 'warn' | 'neutral';
}) {
  return <span className={`badge badge--${tone}`}>{children}</span>;
}

export default function App() {
  const [status, setStatus] = useState<AgentStatus | null>(null);
  const [telemetry, setTelemetry] = useState<TelemetryFrame | null>(null);
  const [workloads, setWorkloads] = useState<WorkloadCandidate[]>([]);
  const [sessions, setSessions] = useState<WorkloadSession[]>([]);
  const [activeSession, setActiveSession] = useState<WorkloadSession | null>(null);
  const [incidents, setIncidents] = useState<IncidentReport[]>([]);
  const [selectedIncident, setSelectedIncident] = useState<IncidentReport | null>(null);
  const [capturingMarker, setCapturingMarker] = useState(false);
  const [changingAutoAssist, setChangingAutoAssist] = useState(false);
  const [streamState, setStreamState] = useState<StreamState>('connecting');
  const [history, setHistory] = useState<HistoryPoint[]>([]);
  const [busyPid, setBusyPid] = useState<number | null>(null);
  const [stopping, setStopping] = useState(false);
  const [loadingWorkloads, setLoadingWorkloads] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const lastActiveId = useRef<string | null>(null);
  const lastIncidentCount = useRef<number | null>(null);
  const lastSessionCount = useRef<number | null>(null);

  const refreshStatus = useCallback(async () => {
    const next = await getStatus();
    setStatus(next);
    lastIncidentCount.current = next.incidentCount;
    lastSessionCount.current = next.sessionCount;
    lastActiveId.current = next.activeSessionId;
  }, []);

  const refreshSessionData = useCallback(async () => {
    const [allSessions, active, allIncidents] = await Promise.all([
      getSessions(), getActiveSession(), getIncidents(),
    ]);
    setSessions(allSessions);
    setActiveSession(active);
    setIncidents(allIncidents);
    setSelectedIncident((current) => current
      ? allIncidents.find((incident) => incident.incidentId === current.incidentId) ?? current
      : null);
  }, []);

  const refreshWorkloads = useCallback(async () => {
    setLoadingWorkloads(true);
    try { setWorkloads(await getWorkloads(20)); }
    finally { setLoadingWorkloads(false); }
  }, []);

  const refreshAll = useCallback(async () => {
    setError(null);
    try {
      const [nextStatus, nextTelemetry, nextSessions, nextActive, nextIncidents, nextWorkloads] = await Promise.all([
        getStatus(), getLatestTelemetry(), getSessions(), getActiveSession(), getIncidents(), getWorkloads(20),
      ]);
      setStatus(nextStatus);
      setTelemetry(nextTelemetry);
      setSessions(nextSessions);
      setActiveSession(nextActive);
      setIncidents(nextIncidents);
      setWorkloads(nextWorkloads);
      lastActiveId.current = nextStatus.activeSessionId;
      lastIncidentCount.current = nextStatus.incidentCount;
      lastSessionCount.current = nextStatus.sessionCount;
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not load CrashScope state.');
    }
  }, []);

  useEffect(() => { void refreshAll(); }, [refreshAll]);

  useEffect(() => {
    let socket: WebSocket | null = null;
    let retry: number | null = null;
    let disposed = false;

    const connect = () => {
      if (disposed) return;
      setStreamState('connecting');
      socket = createLiveSocket();
      socket.onopen = () => setStreamState('live');
      socket.onmessage = (event) => {
        const message = JSON.parse(event.data as string) as LiveMessage;
        if (message.type !== 'telemetry') return;

        setTelemetry(message.telemetry);
        const gpu = message.telemetry.gpus[0];
        setHistory((current) => [...current, {
          cpu: readingValue(message.telemetry.cpu.totalUtilizationPercent),
          gpu: readingValue(gpu?.coreUtilizationPercent),
          memory: readingValue(message.telemetry.system.physicalMemoryLoadPercent),
        }].slice(-MAX_HISTORY));

        setStatus((current) => current ? {
          ...current,
          samplingMode: message.status.samplingMode,
          activeSessionId: message.status.activeSessionId,
          activeProcessName: message.status.activeProcessName,
          latestSequence: message.telemetry.sequence,
          latestTimestampUtc: message.telemetry.timestampUtc,
          incidentCount: message.status.incidentCount ?? current.incidentCount,
          sessionCount: message.status.sessionCount ?? current.sessionCount,
        } : current);

        const activeChanged = lastActiveId.current !== message.status.activeSessionId;
        const incidentChanged = message.status.incidentCount !== undefined
          && lastIncidentCount.current !== message.status.incidentCount;
        const sessionChanged = message.status.sessionCount !== undefined
          && lastSessionCount.current !== message.status.sessionCount;
        lastActiveId.current = message.status.activeSessionId;
        if (message.status.incidentCount !== undefined) lastIncidentCount.current = message.status.incidentCount;
        if (message.status.sessionCount !== undefined) lastSessionCount.current = message.status.sessionCount;
        if (activeChanged || incidentChanged || sessionChanged) {
          void Promise.all([refreshStatus(), refreshSessionData()]);
        }
      };
      socket.onclose = () => {
        if (disposed) return;
        setStreamState('offline');
        retry = window.setTimeout(connect, 2000);
      };
      socket.onerror = () => socket?.close();
    };

    connect();
    return () => {
      disposed = true;
      if (retry !== null) window.clearTimeout(retry);
      socket?.close();
    };
  }, [refreshSessionData, refreshStatus]);

  const handleAttach = async (candidate: WorkloadCandidate) => {
    setBusyPid(candidate.processId);
    setError(null);
    try {
      setActiveSession(await attachCandidate(candidate));
      await Promise.all([refreshStatus(), refreshSessionData(), refreshWorkloads()]);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not attach workload.');
      await refreshWorkloads();
    } finally { setBusyPid(null); }
  };

  const handleStop = async () => {
    setStopping(true);
    setError(null);
    try {
      await stopActiveSession();
      await Promise.all([refreshStatus(), refreshSessionData(), refreshWorkloads()]);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not stop session.');
    } finally { setStopping(false); }
  };

  const handleCaptureMarker = async () => {
    setCapturingMarker(true);
    setError(null);
    try {
      const report = await captureDiagnosticMarker();
      setSelectedIncident(report);
      await Promise.all([refreshStatus(), refreshSessionData()]);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not complete diagnostic marker capture.');
    } finally { setCapturingMarker(false); }
  };

  const handleToggleAutoAssist = async () => {
    if (!status || changingAutoAssist) return;
    setChangingAutoAssist(true);
    setError(null);
    try {
      await setAutoAssist(!status.automaticMonitoringEnabled);
      await refreshStatus();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not update Auto Assist.');
    } finally {
      setChangingAutoAssist(false);
    }
  };

  const gpu = telemetry?.gpus[0];
  const cpuLoad = readingValue(telemetry?.cpu.totalUtilizationPercent);
  const gpuLoad = readingValue(gpu?.coreUtilizationPercent);
  const hotspot = readingValue(gpu?.hotspotTemperatureCelsius);
  const memoryLoad = readingValue(telemetry?.system.physicalMemoryLoadPercent);
  const vramUsed = readingValue(gpu?.dedicatedMemoryUsedMiB);
  const vramTotal = readingValue(gpu?.dedicatedMemoryTotalMiB);
  const vramPercent = vramUsed !== null && vramTotal ? (vramUsed / vramTotal) * 100 : null;
  const recentSessions = [...sessions].sort((a, b) => b.startedAtUtc.localeCompare(a.startedAtUtc)).slice(0, 6);

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <div className="brand-mark" aria-hidden="true">CS</div>
          <div><strong>CrashScope</strong><span>Local diagnostics</span></div>
        </div>
        <nav className="nav-list" aria-label="Dashboard sections">
          <a href="#overview" className="nav-link nav-link--active">Overview</a>
          <a href="#incidents" className="nav-link">Incidents</a>
          <a href="#workloads" className="nav-link">Workloads</a>
          <a href="#configtrace" className="nav-link">ConfigTrace</a>
          <a href="#sessions" className="nav-link">History</a>
        </nav>

        <section className="sidebar-pulse" aria-label="Live system pulse">
          <div className="sidebar-pulse__heading">
            <span>SYSTEM PULSE</span>
            <strong>{status?.samplingMode ?? 'Waiting'}</strong>
          </div>

          <div className="sidebar-pulse__metric">
            <div><span>CPU</span><strong>{cpuLoad === null ? '--' : `${Math.round(cpuLoad)}%`}</strong></div>
            <div className="sidebar-pulse__track" aria-hidden="true">
              <span style={{ width: `${Math.min(100, Math.max(0, cpuLoad ?? 0))}%` }} />
            </div>
          </div>

          <div className="sidebar-pulse__metric">
            <div><span>GPU</span><strong>{gpuLoad === null ? '--' : `${Math.round(gpuLoad)}%`}</strong></div>
            <div className="sidebar-pulse__track" aria-hidden="true">
              <span style={{ width: `${Math.min(100, Math.max(0, gpuLoad ?? 0))}%` }} />
            </div>
          </div>

          <div className="sidebar-pulse__metric">
            <div><span>RAM</span><strong>{memoryLoad === null ? '--' : `${Math.round(memoryLoad)}%`}</strong></div>
            <div className="sidebar-pulse__track" aria-hidden="true">
              <span style={{ width: `${Math.min(100, Math.max(0, memoryLoad ?? 0))}%` }} />
            </div>
          </div>

          <div className="sidebar-pulse__foot">
            <span className={`connection-dot connection-dot--${streamState}`} />
            Local telemetry
          </div>
        </section>

        <section className="sidebar-session" aria-label="Session snapshot">
          <div className="sidebar-session__heading">
            <span>SESSION SNAPSHOT</span>
            <strong>{status?.activeSessionId ? 'ACTIVE' : 'IDLE'}</strong>
          </div>

          <div className="sidebar-session__process">
            <span>Current</span>
            <strong>{status?.activeProcessName ?? 'No active workload'}</strong>
          </div>

          <div className="sidebar-session__grid">
            <div>
              <span>Incidents</span>
              <strong>{status?.incidentCount ?? 0}</strong>
            </div>
            <div>
              <span>Sessions</span>
              <strong>{status?.sessionCount ?? 0}</strong>
            </div>
          </div>

          <div className="sidebar-session__assist">
            <span>Auto Assist</span>
            <strong>{status?.automaticMonitoringState ?? 'Waiting'}</strong>
          </div>
        </section>
        <div className="sidebar-footer">
          <span className={`connection-dot connection-dot--${streamState}`} />
          <div>
            <strong>{streamState === 'live' ? 'Agent online' : streamState === 'connecting' ? 'Connecting' : 'Reconnecting'}</strong>
            <span>{status ? `${status.samplingMode} · v${status.crashScopeVersion}` : 'Waiting for Agent'}</span>
          </div>
        </div>
      </aside>

      <main className="main-content">
        <header className="topbar" id="overview">
          <div>
            <p className="eyebrow">LOCAL CRASH DIAGNOSTICS</p>
            <h1>Your PC, watched quietly.</h1>
            <p className="topbar-copy">Automatic, evidence-first help when games, drivers, or Windows misbehave.</p>
          </div>
          <div className="topbar-actions">
            <div className="status-pill"><span className={`connection-dot connection-dot--${streamState}`} />
              {streamState === 'live' ? 'Protected locally' : 'Agent reconnecting'}
            </div>
            <button className="button button--ghost" onClick={() => void refreshAll()}>Refresh</button>
          </div>
        </header>

        {error && <div className="error-banner" role="alert">
          <strong>CrashScope notice</strong><span>{error}</span>
          <button onClick={() => setError(null)} aria-label="Dismiss notice">×</button>
        </div>}

        <EverydayStatusPanel
          status={status}
          activeSession={activeSession}
          changingAutoAssist={changingAutoAssist}
          onToggleAutoAssist={() => void handleToggleAutoAssist()}
        />

        <div className="section-intro">
          <div><p className="eyebrow">LIVE VITALS</p><h2>Just enough telemetry to see what the system is doing</h2></div>
          <span>One central hardware read · {status?.samplingMode === 'Active' ? '1 Hz active' : '0.5 Hz background'}</span>
        </div>
        <section className="metrics-grid" aria-label="Live telemetry">
          <MetricCard label="CPU load" value={cpuLoad} suffix="%"
            detail={telemetry?.cpu.temperatureCelsius.isAvailable
              ? `${formatNumber(readingValue(telemetry.cpu.temperatureCelsius))}°C package`
              : telemetry?.cpu.temperatureCelsius.detail ?? 'Temperature unavailable on this sensor path'}
            history={history.map((point) => point.cpu)} />
          <MetricCard label={gpu?.name ?? 'GPU load'} value={gpuLoad} suffix="%"
            detail={gpu ? `${formatNumber(readingValue(gpu.packagePowerWatts))} W · ${formatNumber(readingValue(gpu.coreClockMHz))} MHz` : 'No GPU frame yet'}
            history={history.map((point) => point.gpu)} />
          <MetricCard label="GPU hotspot" value={hotspot} suffix="°C"
            detail={gpu ? `Core ${formatNumber(readingValue(gpu.coreTemperatureCelsius))}°C · Memory ${formatNumber(readingValue(gpu.memoryTemperatureCelsius))}°C` : 'No GPU frame yet'} />
          <MetricCard label="VRAM" value={vramPercent} suffix="%"
            detail={vramUsed !== null ? `${formatMiB(vramUsed)} / ${formatMiB(vramTotal)}` : 'Dedicated memory unavailable'} />
          <MetricCard label="System memory" value={memoryLoad} suffix="%"
            detail={telemetry ? `${formatMiB(readingValue(telemetry.system.physicalMemoryUsedMiB))} used` : 'Waiting for telemetry'}
            history={history.map((point) => point.memory)} />
        </section>

        <section className="content-grid content-grid--hero">
          <article className="panel active-panel">
            <div className="panel-heading">
              <div><p className="eyebrow">CURRENT WORKLOAD</p><h2>{activeSession?.processName ?? 'Waiting for a strong workload'}</h2></div>
              {activeSession ? <Badge tone="good">Monitoring</Badge> : <Badge>Auto Assist</Badge>}
            </div>
            {activeSession ? <>
              <div className="active-session-meta"><span>PID {activeSession.processId}</span><span>{duration(activeSession)}</span><span>{activeSession.telemetry.frameCount} frames</span></div>
              <div className="session-stats">
                <div><span>Peak CPU</span><strong>{formatNumber(activeSession.telemetry.peakCpuUtilizationPercent)}%</strong></div>
                <div><span>Peak GPU</span><strong>{formatNumber(activeSession.telemetry.peakGpuUtilizationPercent)}%</strong></div>
                <div><span>Peak hotspot</span><strong>{formatNumber(activeSession.telemetry.peakGpuHotspotCelsius)}°C</strong></div>
                <div><span>Peak VRAM</span><strong>{formatMiB(activeSession.telemetry.peakGpuMemoryUsedMiB)}</strong></div>
              </div>
              {activeSession.environment && <div className="environment-strip">
                <span>{activeSession.environment.cpuName ?? 'CPU unavailable'}</span>
                <span>{activeSession.environment.gpus[0]?.name ?? 'GPU unavailable'}</span>
                <span>{activeSession.environment.gpus[0]?.driverVersion ? `Driver ${activeSession.environment.gpus[0].driverVersion}` : 'Driver unavailable'}</span>
              </div>}
              <button className="button button--danger" disabled={stopping} onClick={() => void handleStop()}>{stopping ? 'Stopping…' : 'Stop monitoring'}</button>
            </> : <div className="empty-state">
              <strong>{status?.automaticMonitoringEnabled === false ? 'Auto Assist is off.' : 'No setup needed for recognized games and heavy workloads.'}</strong>
              <p>{status?.automaticMonitoringEnabled === false
                ? 'Use the Auto Assist control above to resume automatic foreground detection, or pick a workload manually below.'
                : 'Auto Assist checks only the foreground process at low frequency. If a workload is not recognized, the manual picker below remains available.'}</p>
            </div>}
          </article>

          <article className="panel health-panel">
            <div className="panel-heading"><div><p className="eyebrow">EFFICIENCY</p><h2>Agent footprint</h2></div><Badge tone={streamState === 'live' ? 'good' : 'warn'}>{streamState}</Badge></div>
            <dl className="health-list">
              <div><dt>Sampling</dt><dd>{status?.samplingMode ?? '—'}</dd></div>
              <div><dt>Auto Assist</dt><dd>{status?.automaticMonitoringState ?? '—'}</dd></div>
              <div><dt>Foreground check</dt><dd>{status?.automaticMonitoringEnabled === false ? 'Paused' : status ? `${status.automaticObservationIntervalSeconds}s` : '—'}</dd></div>
              <div><dt>Hardware polling</dt><dd>Single loop</dd></div>
              <div><dt>Ring buffer</dt><dd>{status ? `${status.telemetryFramesBuffered} frames` : '—'}</dd></div>
              <div><dt>Last frame</dt><dd>{formatDate(status?.latestTimestampUtc)}</dd></div>
            </dl>
          </article>
        </section>

        <ConfigTraceControl />

        <section id="incidents">
          <div className="section-intro"><div><p className="eyebrow">WHAT HAPPENED?</p><h2>Recent diagnostic evidence</h2></div><span>{incidents.length ? `${incidents.length} captured` : 'Nothing important captured yet'}</span></div>
          <IncidentPanel incidents={incidents} selected={selectedIncident} capturing={capturingMarker}
            onCapture={() => void handleCaptureMarker()} onSelect={setSelectedIncident} onClose={() => setSelectedIncident(null)} />
        </section>

        <section className="panel" id="workloads">
          <div className="panel-heading panel-heading--row">
            <div><p className="eyebrow">MANUAL OVERRIDE</p><h2>Running workloads</h2><p>Auto Assist handles strong foreground matches. This full process discovery runs only when you refresh it.</p></div>
            <button className="button button--ghost" disabled={loadingWorkloads} onClick={() => void refreshWorkloads()}>{loadingWorkloads ? 'Scanning…' : 'Refresh workloads'}</button>
          </div>
          <div className="workload-list">
            {workloads.length === 0 ? <div className="empty-row">No likely workload candidates are running right now.</div> : workloads.map((candidate) => (
              <div className="workload-row" key={`${candidate.processId}-${candidate.processStartTimeUtc}`}>
                <div className="workload-icon" aria-hidden="true">{candidate.processName.slice(0, 2).toUpperCase()}</div>
                <div className="workload-main">
                  <div className="workload-title-row"><strong>{candidate.processName}</strong><Badge tone={candidate.recommendation === 'Recommended' ? 'good' : 'neutral'}>{candidate.kind}</Badge><Badge tone={candidate.recommendation === 'Recommended' ? 'good' : 'neutral'}>{candidate.recommendation}</Badge></div>
                  <span>{candidate.windowTitle ?? candidate.executablePath ?? 'Executable path unavailable'}</span>
                  <small>{candidate.recommendationReasons[0] ?? 'Candidate identified from running process metadata.'}</small>
                </div>
                <div className="workload-meta"><span>PID {candidate.processId}</span><span>{formatMiB(candidate.workingSetMiB)}</span><span>Score {candidate.recommendationScore}</span></div>
                <button className="button button--primary" disabled={Boolean(activeSession) || busyPid !== null} onClick={() => void handleAttach(candidate)}>{busyPid === candidate.processId ? 'Attaching…' : 'Monitor'}</button>
              </div>
            ))}
          </div>
        </section>

        <section className="panel" id="sessions">
          <div className="panel-heading"><div><p className="eyebrow">HISTORY</p><h2>Recent monitored workloads</h2></div><span className="panel-count">{sessions.length}</span></div>
          <div className="table-wrap"><table><thead><tr><th>Workload</th><th>Started</th><th>Duration</th><th>Peak GPU</th><th>End reason</th></tr></thead><tbody>
            {recentSessions.length === 0 ? <tr><td colSpan={5} className="empty-cell">No sessions recorded yet.</td></tr> : recentSessions.map((session) => <tr key={session.sessionId}>
              <td><strong>{session.processName}</strong><span>PID {session.processId}</span></td><td>{formatDate(session.startedAtUtc)}</td><td>{duration(session)}</td><td>{formatNumber(session.telemetry.peakGpuUtilizationPercent)}%</td><td><Badge tone={session.endReason === 'ProcessExited' ? 'neutral' : 'warn'}>{session.endReason ?? 'Active'}</Badge></td>
            </tr>)}
          </tbody></table></div>
        </section>

        <footer className="footer"><span>CrashScope · local-first · evidence before conclusions</span><span>{telemetry ? `Frame ${telemetry.sequence}` : 'Waiting for first frame'}</span></footer>
      </main>
    </div>
  );
}
