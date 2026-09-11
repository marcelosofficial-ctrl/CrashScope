import { useEffect, useState } from 'react';
import { getSettings, setRetentionDays } from './api';

const RETENTION_OPTIONS = [7, 30, 90, 180, 365];

export default function RetentionControl() {
  const [days, setDays] = useState<number | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let disposed = false;
    void getSettings()
      .then((settings) => { if (!disposed) setDays(settings.retentionDays); })
      .catch((caught) => { if (!disposed) setError(caught instanceof Error ? caught.message : 'Could not read retention setting.'); });
    return () => { disposed = true; };
  }, []);

  const change = async (nextDays: number) => {
    if (saving || nextDays === days) return;
    setSaving(true);
    setError(null);
    try {
      const settings = await setRetentionDays(nextDays);
      setDays(settings.retentionDays);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not update retention setting.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="retention-control">
      <div>
        <span>Keep history</span>
        <small>Expired completed sessions and unlinked incidents are pruned once when CrashScope starts.</small>
      </div>
      <select
        aria-label="CrashScope history retention"
        value={days ?? 30}
        disabled={days === null || saving}
        onChange={(event) => void change(Number(event.target.value))}
      >
        {RETENTION_OPTIONS.map((option) => (
          <option key={option} value={option}>{option} days</option>
        ))}
      </select>
      {error && <small className="retention-control__error">{error}</small>}
    </div>
  );
}
