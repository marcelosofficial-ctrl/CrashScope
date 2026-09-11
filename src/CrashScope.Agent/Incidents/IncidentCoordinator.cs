using System.Diagnostics;
using CrashScope.Agent.Buffering;
using CrashScope.Agent.Sampling;
using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Incidents;

internal sealed record IncidentTrigger
{
    public Guid IncidentId { get; }
    public string EvidenceKey { get; }
    public string Summary { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public DateTimeOffset? SourceOccurredAtUtc { get; }
    public long MonotonicTimestampTicks { get; }

    public IncidentTrigger(
        Guid incidentId,
        string evidenceKey,
        string summary,
        DateTimeOffset observedAtUtc,
        DateTimeOffset? sourceOccurredAtUtc,
        long monotonicTimestampTicks)
    {
        if (incidentId == Guid.Empty)
        {
            throw new ArgumentException(
                "Incident ID cannot be empty.",
                nameof(incidentId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        if (observedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Incident observation time must be UTC.",
                nameof(observedAtUtc));
        }

        if (sourceOccurredAtUtc.HasValue &&
            sourceOccurredAtUtc.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Incident source occurrence time must be UTC when known.",
                nameof(sourceOccurredAtUtc));
        }

        if (monotonicTimestampTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monotonicTimestampTicks));
        }

        IncidentId = incidentId;
        EvidenceKey = evidenceKey;
        Summary = summary;
        ObservedAtUtc = observedAtUtc;
        SourceOccurredAtUtc = sourceOccurredAtUtc;
        MonotonicTimestampTicks = monotonicTimestampTicks;
    }
}

internal sealed record IncidentCapture
{
    public IncidentTrigger Trigger { get; }
    public IReadOnlyList<TelemetryFrame> TelemetryFrames { get; }

    public IncidentCapture(
        IncidentTrigger trigger,
        IReadOnlyList<TelemetryFrame> telemetryFrames)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(telemetryFrames);

        Trigger = trigger;
        TelemetryFrames = telemetryFrames.ToArray();
    }
}

internal sealed class IncidentCoordinator : ITelemetryFrameSink
{
    public static TimeSpan DefaultPreTriggerWindow { get; } =
        TimeSpan.FromSeconds(60);
    public static TimeSpan DefaultPostTriggerWindow { get; } =
        TimeSpan.FromSeconds(30);

    private readonly object _sync = new();
    private readonly TelemetryRingBuffer _buffer;
    private readonly TimeSpan _preTriggerWindow;
    private readonly TimeSpan _postTriggerWindow;
    private readonly List<ActiveCapture> _activeCaptures = new();

    public IncidentCoordinator(
        TelemetryRingBuffer buffer,
        TimeSpan? preTriggerWindow = null,
        TimeSpan? postTriggerWindow = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        _buffer = buffer;
        _preTriggerWindow = preTriggerWindow ?? DefaultPreTriggerWindow;
        _postTriggerWindow = postTriggerWindow ?? DefaultPostTriggerWindow;

        if (_preTriggerWindow < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(preTriggerWindow));
        }

        if (_postTriggerWindow < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(postTriggerWindow));
        }
    }

    public Task<IncidentCapture> BeginCapture(IncidentTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        var snapshot = _buffer.Snapshot();
        var state = new ActiveCapture(trigger);

        foreach (var frame in snapshot)
        {
            if (IsWithinCaptureWindow(frame, trigger))
            {
                state.Frames.Add(frame);
            }
        }

        if (_postTriggerWindow == TimeSpan.Zero ||
            snapshot.Any(frame => HasReachedPostBoundary(frame, trigger)))
        {
            return Task.FromResult(
                new IncidentCapture(trigger, state.Frames));
        }

        lock (_sync)
        {
            _activeCaptures.Add(state);
        }

        return state.Completion.Task;
    }

    public async ValueTask WriteAsync(
        TelemetryFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();

        await _buffer.WriteAsync(frame, cancellationToken)
            .ConfigureAwait(false);

        List<ActiveCapture>? completed = null;

        lock (_sync)
        {
            for (var index = _activeCaptures.Count - 1; index >= 0; index--)
            {
                var state = _activeCaptures[index];

                if (frame.MonotonicTimestampTicks >=
                    state.Trigger.MonotonicTimestampTicks)
                {
                    state.Frames.Add(frame);
                }

                if (!HasReachedPostBoundary(frame, state.Trigger))
                {
                    continue;
                }

                completed ??= new List<ActiveCapture>();
                completed.Add(state);
                _activeCaptures.RemoveAt(index);
            }
        }

        if (completed is null)
        {
            return;
        }

        foreach (var state in completed)
        {
            state.Completion.TrySetResult(
                new IncidentCapture(state.Trigger, state.Frames));
        }
    }

    private bool IsWithinCaptureWindow(
        TelemetryFrame frame,
        IncidentTrigger trigger)
    {
        if (frame.MonotonicTimestampTicks <= trigger.MonotonicTimestampTicks)
        {
            return Stopwatch.GetElapsedTime(
                    frame.MonotonicTimestampTicks,
                    trigger.MonotonicTimestampTicks) <=
                _preTriggerWindow;
        }

        return Stopwatch.GetElapsedTime(
                trigger.MonotonicTimestampTicks,
                frame.MonotonicTimestampTicks) <=
            _postTriggerWindow;
    }

    private bool HasReachedPostBoundary(
        TelemetryFrame frame,
        IncidentTrigger trigger)
    {
        if (frame.MonotonicTimestampTicks < trigger.MonotonicTimestampTicks)
        {
            return false;
        }

        return Stopwatch.GetElapsedTime(
                trigger.MonotonicTimestampTicks,
                frame.MonotonicTimestampTicks) >=
            _postTriggerWindow;
    }

    private sealed class ActiveCapture
    {
        public IncidentTrigger Trigger { get; }
        public List<TelemetryFrame> Frames { get; } = new();
        public TaskCompletionSource<IncidentCapture> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ActiveCapture(IncidentTrigger trigger)
        {
            Trigger = trigger;
        }
    }
}
