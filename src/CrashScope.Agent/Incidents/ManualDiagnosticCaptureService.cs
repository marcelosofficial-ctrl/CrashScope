using CrashScope.Agent.Sampling;
using CrashScope.Agent.Sessions;
using CrashScope.Core.Evidence;
using CrashScope.Core.Incidents;
using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Incidents;

internal sealed class ManualDiagnosticCaptureService
{
    private readonly IncidentCoordinator _coordinator;
    private readonly IncidentReportBuilder _reportBuilder;
    private readonly IIncidentReportSink _sink;
    private readonly WorkloadSessionManager _sessions;
    private readonly ISamplingClock _clock;
    private readonly Func<
        DateTimeOffset,
        DateTimeOffset,
        CancellationToken,
        ValueTask<IReadOnlyList<EvidenceEvent>>> _providerEvidenceSource;
    private int _captureInProgress;

    public ManualDiagnosticCaptureService(
        IncidentCoordinator coordinator,
        IncidentReportBuilder reportBuilder,
        IIncidentReportSink sink,
        WorkloadSessionManager sessions,
        ISamplingClock clock)
        : this(
            coordinator,
            reportBuilder,
            sink,
            sessions,
            clock,
            sessions.ReadActiveEvidenceWindowAsync)
    {
    }

    internal ManualDiagnosticCaptureService(
        IncidentCoordinator coordinator,
        IncidentReportBuilder reportBuilder,
        IIncidentReportSink sink,
        WorkloadSessionManager sessions,
        ISamplingClock clock,
        Func<
            DateTimeOffset,
            DateTimeOffset,
            CancellationToken,
            ValueTask<IReadOnlyList<EvidenceEvent>>> providerEvidenceSource)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(reportBuilder);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(providerEvidenceSource);

        _coordinator = coordinator;
        _reportBuilder = reportBuilder;
        _sink = sink;
        _sessions = sessions;
        _clock = clock;
        _providerEvidenceSource = providerEvidenceSource;
    }

    public bool IsCaptureInProgress => Volatile.Read(ref _captureInProgress) != 0;

    public async Task<IncidentReport> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _captureInProgress, 1, 0) != 0)
        {
            throw new InvalidOperationException("A manual diagnostic capture is already in progress.");
        }

        try
        {
            var now = _clock.GetUtcNow().ToUniversalTime();
            var incidentId = Guid.NewGuid();
            var trigger = new IncidentTrigger(
                incidentId,
                $"manual:{incidentId:N}",
                "User requested a diagnostic capture marker.",
                now,
                now,
                _clock.GetTimestamp());

            var active = _sessions.ActiveSnapshot();
            var capture = await _coordinator.BeginCapture(trigger)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            var process = active is null
                ? null
                : new IncidentProcessContext(
                    active.ProcessId,
                    active.ProcessStartTimeUtc,
                    active.ProcessName,
                    "MonitoredSession",
                    now);

            IReadOnlyList<EvidenceEvent> providerEvidence = Array.Empty<EvidenceEvent>();
            try
            {
                providerEvidence = await _providerEvidenceSource(
                        now - _reportBuilder.EvidenceWindow,
                        now + _reportBuilder.EvidenceWindow,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Optional provider evidence must never block a user-requested marker.
            }

            var evidence = new List<IncidentEvidenceItem>
            {
                new(
                    now,
                    now,
                    IncidentEvidenceRole.Trigger,
                    "CrashScope.User",
                    "DiagnosticMarker",
                    "User requested a diagnostic capture marker.",
                    trigger.EvidenceKey)
            };

            evidence.AddRange(
                _reportBuilder.BuildProviderEvidenceItems(providerEvidence, now));

            var report = new IncidentReport(
                incidentId,
                IncidentClassification.UserDiagnosticMarker,
                "Diagnostic capture marker",
                "CrashScope preserved telemetry around a marker requested by the user.",
                "This report is a user-requested diagnostic marker, not evidence that a crash, hardware error, or driver failure occurred.",
                now,
                process,
                Summarize(capture.TelemetryFrames),
                evidence);

            await _sink.WriteAsync(report, cancellationToken).ConfigureAwait(false);
            return report;
        }
        finally
        {
            Volatile.Write(ref _captureInProgress, 0);
        }
    }

    private static IncidentTelemetrySummary Summarize(
        IReadOnlyList<TelemetryFrame> frames)
    {
        if (frames.Count == 0)
        {
            return new IncidentTelemetrySummary(0, null, null, null, null, null, null, null);
        }

        return new IncidentTelemetrySummary(
            frames.Count,
            frames.Min(x => x.TimestampUtc),
            frames.Max(x => x.TimestampUtc),
            MaxAvailable(frames.Select(x => x.Cpu.TotalUtilizationPercent)),
            MaxAvailable(frames.SelectMany(x => x.Gpus).Select(x => x.CoreUtilizationPercent)),
            MaxAvailable(frames.SelectMany(x => x.Gpus).Select(x => x.HotspotTemperatureCelsius)),
            MaxAvailable(frames.SelectMany(x => x.Gpus).Select(x => x.DedicatedMemoryUsedMiB)),
            MaxAvailable(frames.Select(x => x.System.PhysicalMemoryLoadPercent)));
    }

    private static double? MaxAvailable(IEnumerable<MetricReading> readings)
    {
        double? maximum = null;
        foreach (var reading in readings)
        {
            if (!reading.IsAvailable || !reading.Value.HasValue)
            {
                continue;
            }

            maximum = !maximum.HasValue
                ? reading.Value.Value
                : Math.Max(maximum.Value, reading.Value.Value);
        }

        return maximum;
    }
}