import { useEffect, useState } from 'react';
import { getStartupStatus, setStartupEnabled } from './api';
import type { StartupRegistrationStatus } from './types';

export default function StartupControl() {
  const [status, setStatus] = useState<StartupRegistrationStatus | null>(null);
  const [changing, setChanging] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let disposed = false;
    void getStartupStatus()
      .then((next) => { if (!disposed) setStatus(next); })
      .catch((caught) => { if (!disposed) setError(caught instanceof Error ? caught.message : 'Could not read startup setting.'); });
    return () => { disposed = true; };
  }, []);

  const toggle = async () => {
    if (!status?.supported || changing) return;
    setChanging(true);
    setError(null);
    try {
      setStatus(await setStartupEnabled(!status.enabled));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not update startup setting.');
    } finally {
      setChanging(false);
    }
  };

  return (
    <div className="startup-control">
      <div>
        <span>Start with Windows</span>
        <small>{status?.detail ?? 'Optional per-user startup. CrashScope starts quietly without opening the browser.'}</small>
      </div>
      <button
        type="button"
        className={`toggle-button${status?.enabled ? ' toggle-button--on' : ''}`}
        aria-pressed={status?.enabled ?? false}
        disabled={!status?.supported || changing}
        onClick={() => void toggle()}
      >
        {changing ? 'Saving…' : status?.enabled ? 'On' : 'Off'}
      </button>
      {error && <small className="startup-control__error">{error}</small>}
    </div>
  );
}
