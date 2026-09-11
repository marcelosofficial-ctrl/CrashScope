import { useEffect, useMemo, useState } from 'react';
import { getSettings, setConfigTraceEnabled, setConfigTraceRoot } from './api';
import type { CrashScopeSettings } from './types';

export default function ConfigTraceControl() {
  const [settings, setSettings] = useState<CrashScopeSettings | null>(null);
  const [draftRoot, setDraftRoot] = useState('');
  const [savingRoot, setSavingRoot] = useState(false);
  const [changingEnabled, setChangingEnabled] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let disposed = false;

    void getSettings()
      .then((next) => {
        if (disposed) return;
        setSettings(next);
        setDraftRoot(next.configTraceRootPath ?? '');
      })
      .catch((caught) => {
        if (!disposed) {
          setError(caught instanceof Error ? caught.message : 'Could not read ConfigTrace settings.');
        }
      });

    return () => { disposed = true; };
  }, []);

  const normalizedDraft = draftRoot.trim();
  const savedRoot = settings?.configTraceRootPath ?? '';
  const rootChanged = normalizedDraft !== savedRoot;
  const configured = Boolean(savedRoot);
  const busy = savingRoot || changingEnabled;

  const stateLabel = useMemo(() => {
    if (!settings) return 'Loading';
    if (settings.configTraceEnabled) return 'On';
    if (configured) return 'Ready';
    return 'Off';
  }, [configured, settings]);

  const saveRoot = async () => {
    if (!settings || savingRoot || !rootChanged) return;

    setSavingRoot(true);
    setError(null);

    try {
      const updated = await setConfigTraceRoot(normalizedDraft || null);
      setSettings(updated);
      setDraftRoot(updated.configTraceRootPath ?? '');
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not update ConfigTrace root.');
    } finally {
      setSavingRoot(false);
    }
  };

  const toggle = async () => {
    if (!settings || changingEnabled || rootChanged || (!settings.configTraceEnabled && !configured)) return;

    setChangingEnabled(true);
    setError(null);

    try {
      setSettings(await setConfigTraceEnabled(!settings.configTraceEnabled));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not update ConfigTrace.');
    } finally {
      setChangingEnabled(false);
    }
  };

  return (
    <section className="panel configtrace-panel" id="configtrace" aria-labelledby="configtrace-heading">
      <div className="panel-heading panel-heading--row configtrace-panel__heading">
        <div>
          <p className="eyebrow">CONFIGURATION EVIDENCE</p>
          <h2 id="configtrace-heading">ConfigTrace</h2>
          <p>
            Opt in to watch one game or application configuration folder while CrashScope is actively monitoring a workload.
          </p>
        </div>
        <span className={`configtrace-state${settings?.configTraceEnabled ? ' configtrace-state--on' : ''}`}>
          {stateLabel}
        </span>
      </div>

      <div className="configtrace-root">
        <label htmlFor="configtrace-root">Configuration folder</label>
        <div className="configtrace-root__row">
          <input
            id="configtrace-root"
            type="text"
            value={draftRoot}
            disabled={!settings || busy}
            spellCheck={false}
            autoComplete="off"
            placeholder="C:\path\to\game\config"
            onChange={(event) => setDraftRoot(event.target.value)}
          />
          <button
            type="button"
            className="button button--ghost"
            disabled={!settings || busy || !rootChanged}
            onClick={() => void saveRoot()}
          >
            {savingRoot ? 'Saving…' : normalizedDraft ? 'Save path' : 'Clear'}
          </button>
        </div>
        <small>Paste an existing absolute folder path. CrashScope's own local-data directory cannot be watched.</small>
      </div>

      <div className="configtrace-enable">
        <div>
          <strong>Include configuration changes</strong>
          <span>
            ConfigTrace starts only for a monitored workload and stops with that workload. It is not a permanent background watcher.
          </span>
        </div>
        <button
          type="button"
          className={`toggle-button${settings?.configTraceEnabled ? ' toggle-button--on' : ''}`}
          aria-pressed={settings?.configTraceEnabled ?? false}
          disabled={!settings || busy || rootChanged || (!settings.configTraceEnabled && !configured)}
          onClick={() => void toggle()}
        >
          {changingEnabled ? 'Saving…' : settings?.configTraceEnabled ? 'On' : 'Off'}
        </button>
      </div>

      <div className="configtrace-context-note">
        <strong>Evidence, not blame.</strong>
        <span>
          Nearby configuration changes are shown as Context evidence. They can help explain what changed around an incident, but do not prove that a setting caused it. Sensitive structured values are redacted by ConfigTrace.
        </span>
      </div>

      {rootChanged && settings && (
        <small className="configtrace-panel__hint">Save the folder path before changing the ConfigTrace On/Off state.</small>
      )}
      {error && <small className="configtrace-panel__error" role="alert">{error}</small>}
    </section>
  );
}