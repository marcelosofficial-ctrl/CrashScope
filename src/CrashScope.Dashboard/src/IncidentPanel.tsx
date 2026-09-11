import { useEffect, useRef, useState } from 'react';
import { downloadSupportBundle, getSupportBundlePreview } from './api';
import { getIncidentGuidance } from './incidentGuidance';
import type { IncidentReport, SupportBundlePreview } from './types';

function formatDate(value: string | null | undefined): string {
  if (!value) return '—';
  return new Intl.DateTimeFormat(undefined, {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit',
  }).format(new Date(value));
}

function formatNumber(value: number | null | undefined, digits = 0): string {
  return value === null || value === undefined || !Number.isFinite(value) ? '—' : value.toFixed(digits);
}

function formatMiB(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—';
  return value >= 1024 ? `${(value / 1024).toFixed(1)} GiB` : `${value.toFixed(0)} MiB`;
}

export default function IncidentPanel({ incidents, selected, capturing, onCapture, onSelect, onClose }: {
  incidents: IncidentReport[];
  selected: IncidentReport | null;
  capturing: boolean;
  onCapture: () => void;
  onSelect: (incident: IncidentReport) => void;
  onClose: () => void;
}) {
  const detailRef = useRef<HTMLElement | null>(null);
  const [supportPreview, setSupportPreview] = useState<SupportBundlePreview | null>(null);
  const [loadingPreview, setLoadingPreview] = useState(false);
  const [downloadingBundle, setDownloadingBundle] = useState(false);
  const [supportError, setSupportError] = useState<string | null>(null);
  const recent = [...incidents].sort((a, b) => b.incidentTimeUtc.localeCompare(a.incidentTimeUtc)).slice(0, 6);
  const guidance = selected ? getIncidentGuidance(selected) : null;

  useEffect(() => {
    setSupportPreview(null);
    setSupportError(null);
    if (!selected || !detailRef.current) return;
    const detail = detailRef.current;
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const frame = window.requestAnimationFrame(() => {
      detail.scrollIntoView({ behavior: reduceMotion ? 'auto' : 'smooth', block: 'start' });
      detail.focus({ preventScroll: true });
    });
    return () => window.cancelAnimationFrame(frame);
  }, [selected?.incidentId]);

  const handlePreviewSupportBundle = async () => {
    if (!selected || loadingPreview) return;
    setLoadingPreview(true);
    setSupportError(null);
    try {
      setSupportPreview(await getSupportBundlePreview(selected.incidentId));
    } catch (caught) {
      setSupportError(caught instanceof Error ? caught.message : 'Could not preview the support bundle.');
    } finally {
      setLoadingPreview(false);
    }
  };

  const handleDownloadSupportBundle = async () => {
    if (!selected || !supportPreview || downloadingBundle) return;
    setDownloadingBundle(true);
    setSupportError(null);
    let objectUrl: string | null = null;
    try {
      const blob = await downloadSupportBundle(selected.incidentId);
      objectUrl = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = objectUrl;
      anchor.download = supportPreview.fileName;
      anchor.style.display = 'none';
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
    } catch (caught) {
      setSupportError(caught instanceof Error ? caught.message : 'Could not create the support bundle.');
    } finally {
      if (objectUrl) URL.revokeObjectURL(objectUrl);
      setDownloadingBundle(false);
    }
  };

  return <>
    <article className="panel" id="incidents">
      <div className="panel-heading panel-heading--row">
        <div>
          <p className="eyebrow">INCIDENTS</p>
          <h2>Evidence timeline</h2>
          <p>CrashScope keeps evidence separate from assumptions. A safe marker tests the pipeline without causing a crash.</p>
        </div>
        <div className="incident-actions">
          <span className="panel-count">{incidents.length}</span>
          <button className="button button--ghost" disabled={capturing} onClick={onCapture}>
            {capturing ? 'Capturing 30s…' : 'Capture diagnostic marker'}
          </button>
        </div>
      </div>

      <div className="incident-list">
        {recent.length === 0 ? <div className="empty-state empty-state--compact">
          <strong>No incidents captured.</strong>
          <p>CrashScope will preserve correlated Windows evidence when a meaningful trigger occurs.</p>
        </div> : recent.map((incident) => <button
          type="button"
          className={`incident-row incident-row--button${selected?.incidentId === incident.incidentId ? ' incident-row--selected' : ''}`}
          key={incident.incidentId}
          onClick={() => onSelect(incident)}
        >
          <div className={`incident-dot${incident.classification === 'UserDiagnosticMarker' ? ' incident-dot--marker' : ''}`} />
          <div>
            <div className="incident-title-row"><strong>{incident.title}</strong><span className="badge badge--neutral">{incident.classification}</span></div>
            <span>{formatDate(incident.incidentTimeUtc)}</span>
            <p>{incident.assessment}</p>
          </div>
        </button>)}
      </div>
    </article>

    {selected && guidance && <article
      key={selected.incidentId}
      ref={detailRef}
      className="panel incident-detail incident-detail--revealed"
      tabIndex={-1}
      aria-live="polite"
      aria-label={`Incident detail: ${selected.title}`}
    >
      <div className="panel-heading panel-heading--row">
        <div>
          <p className="eyebrow">INCIDENT DETAIL</p>
          <h2>{selected.title}</h2>
          <p>{formatDate(selected.incidentTimeUtc)} · {selected.classification}</p>
        </div>
        <button className="button button--ghost" onClick={onClose}>Close</button>
      </div>

      <div className="guidance-grid">
        <section className="guidance-card guidance-card--observed">
          <span>01 · OBSERVED</span>
          <h3>What CrashScope actually captured</h3>
          <p>{selected.summary}</p>
        </section>
        <section className="guidance-card guidance-card--meaning">
          <span>02 · MEANING</span>
          <h3>What this may indicate</h3>
          <p>{guidance.meaning}</p>
        </section>
        <section className="guidance-card guidance-card--caution">
          <span>03 · LIMIT</span>
          <h3>What this does not prove</h3>
          <p>{guidance.doesNotProve}</p>
        </section>
      </div>

      {guidance.context.length > 0 && <div className="guidance-context">
        <strong>Telemetry context</strong>
        {guidance.context.map((note) => <p key={note}>{note}</p>)}
      </div>}

      <section className="next-checks">
        <div className="next-checks__heading"><span>04 · NEXT CHECKS</span><h3>Useful things to try next</h3></div>
        <ol>{guidance.nextChecks.map((check) => <li key={check}>{check}</li>)}</ol>
      </section>

      <section className="support-bundle" aria-label="Privacy-safe support bundle">
        <div className="support-bundle__heading">
          <div>
            <span>SHARE SAFELY</span>
            <h3>Create a support bundle</h3>
            <p>Prepare a small local ZIP with this incident, its related session when available, and CrashScope health. Nothing is uploaded.</p>
          </div>
          {!supportPreview && <button
            type="button"
            className="button button--ghost"
            disabled={loadingPreview}
            onClick={() => void handlePreviewSupportBundle()}
          >{loadingPreview ? 'Checking privacy…' : 'Preview bundle'}</button>}
        </div>

        {supportError && <div className="support-bundle__error" role="alert">{supportError}</div>}

        {supportPreview && <div className="support-bundle__preview">
          <div className="support-bundle__lists">
            <div>
              <strong>Included</strong>
              <ul>{supportPreview.entries.map((entry) => <li key={entry}>{entry}</li>)}</ul>
            </div>
            <div>
              <strong>Privacy protections</strong>
              <ul>{supportPreview.privacyNotes.map((note) => <li key={note}>{note}</li>)}</ul>
            </div>
          </div>
          <div className="support-bundle__download">
            <div><span>Local file</span><strong>{supportPreview.fileName}</strong></div>
            <button
              type="button"
              className="button button--primary"
              disabled={downloadingBundle}
              onClick={() => void handleDownloadSupportBundle()}
            >{downloadingBundle ? 'Creating ZIP…' : 'Download support bundle'}</button>
          </div>
        </div>}
      </section>

      <div className="incident-detail__stats">
        <div><span>Frames</span><strong>{selected.telemetry.frameCount}</strong></div>
        <div><span>Peak CPU</span><strong>{formatNumber(selected.telemetry.peakCpuUtilizationPercent)}%</strong></div>
        <div><span>Peak GPU</span><strong>{formatNumber(selected.telemetry.peakGpuUtilizationPercent)}%</strong></div>
        <div><span>Peak hotspot</span><strong>{formatNumber(selected.telemetry.peakGpuHotspotCelsius)}°C</strong></div>
        <div><span>Peak VRAM</span><strong>{formatMiB(selected.telemetry.peakGpuMemoryUsedMiB)}</strong></div>
        <div><span>Peak RAM</span><strong>{formatNumber(selected.telemetry.peakSystemMemoryLoadPercent)}%</strong></div>
      </div>

      {selected.process && <div className="incident-detail__process">
        <strong>Process context</strong>
        <span>{selected.process.name} · PID {selected.process.processId} · {selected.process.observationState}</span>
      </div>}

      <details className="incident-evidence-details">
        <summary>Technical evidence · {selected.evidence.length} items</summary>
        <div className="incident-detail__evidence">
          {selected.evidence.map((item, index) => <div className="evidence-item" key={`${item.evidenceKey ?? item.kind}-${index}`}>
            <div className="evidence-item__meta">
              <span className={`evidence-role evidence-role--${item.role.toLowerCase()}`}>{item.role}</span>
              <span>{item.source}</span><span>{item.kind}</span><span>{formatDate(item.occurredAtUtc)}</span>
            </div>
            <p>{item.summary}</p>
          </div>)}
        </div>
      </details>
    </article>}
  </>;
}
