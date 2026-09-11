import type {
  AgentStatus,
  CrashScopeSettings,
  IncidentReport,
  StartupRegistrationStatus,
  SupportBundlePreview,
  TelemetryFrame,
  WorkloadCandidate,
  WorkloadSession,
} from './types';

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      Accept: 'application/json',
      ...init?.headers,
    },
  });

  if (!response.ok) {
    let detail = `${response.status} ${response.statusText}`;
    try {
      const payload = (await response.json()) as { error?: string };
      if (payload.error) detail = payload.error;
    } catch {
      // Preserve the HTTP status when the response body is not JSON.
    }
    throw new Error(detail);
  }

  return (await response.json()) as T;
}

export function getStatus(): Promise<AgentStatus> { return request<AgentStatus>('/api/status'); }
export function getSettings(): Promise<CrashScopeSettings> { return request<CrashScopeSettings>('/api/settings'); }
export async function setAutoAssist(enabled: boolean): Promise<CrashScopeSettings> {
  const payload = await request<{ settings: CrashScopeSettings }>(`/api/settings/auto-assist/${enabled}`, { method: 'PUT' });
  return payload.settings;
}
export function setRetentionDays(days: number): Promise<CrashScopeSettings> {
  return request<CrashScopeSettings>(`/api/settings/retention/${days}`, { method: 'PUT' });
}
export function getStartupStatus(): Promise<StartupRegistrationStatus> {
  return request<StartupRegistrationStatus>('/api/settings/startup');
}
export function setStartupEnabled(enabled: boolean): Promise<StartupRegistrationStatus> {
  return request<StartupRegistrationStatus>(`/api/settings/startup/${enabled}`, { method: 'PUT' });
}

export async function getLatestTelemetry(): Promise<TelemetryFrame | null> {
  const response = await fetch('/api/telemetry/latest', { headers: { Accept: 'application/json' } });
  if (response.status === 204) return null;
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
  return (await response.json()) as TelemetryFrame;
}

export function getWorkloads(maximum = 20): Promise<WorkloadCandidate[]> { return request<WorkloadCandidate[]>(`/api/workloads?maximum=${maximum}`); }
export function getSessions(): Promise<WorkloadSession[]> { return request<WorkloadSession[]>('/api/sessions'); }
export async function getActiveSession(): Promise<WorkloadSession | null> {
  const response = await fetch('/api/sessions/active', { headers: { Accept: 'application/json' } });
  if (response.status === 204) return null;
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
  return (await response.json()) as WorkloadSession;
}
export function getIncidents(): Promise<IncidentReport[]> { return request<IncidentReport[]>('/api/incidents'); }
export function getIncident(incidentId: string): Promise<IncidentReport> { return request<IncidentReport>(`/api/incidents/${encodeURIComponent(incidentId)}`); }
export function captureDiagnosticMarker(): Promise<IncidentReport> { return request<IncidentReport>('/api/incidents/capture-marker', { method: 'POST' }); }
export function getSupportBundlePreview(incidentId: string): Promise<SupportBundlePreview> {
  return request<SupportBundlePreview>(`/api/incidents/${encodeURIComponent(incidentId)}/support-bundle/preview`);
}
export async function downloadSupportBundle(incidentId: string): Promise<Blob> {
  const response = await fetch(`/api/incidents/${encodeURIComponent(incidentId)}/support-bundle`, { method: 'POST', headers: { Accept: 'application/zip' } });
  if (!response.ok) {
    let detail = `${response.status} ${response.statusText}`;
    try { const payload = (await response.json()) as { error?: string }; if (payload.error) detail = payload.error; } catch { }
    throw new Error(detail);
  }
  return response.blob();
}
export function attachCandidate(candidate: WorkloadCandidate): Promise<WorkloadSession> {
  const start = encodeURIComponent(candidate.processStartTimeUtc);
  return request<WorkloadSession>(`/api/sessions/attach-candidate/${candidate.processId}?processStartTimeUtc=${start}`, { method: 'POST' });
}
export function stopActiveSession(): Promise<WorkloadSession> { return request<WorkloadSession>('/api/sessions/stop', { method: 'POST' }); }
export function createLiveSocket(): WebSocket {
  const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
  return new WebSocket(`${protocol}//${window.location.host}/api/live`);
}
