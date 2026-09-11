namespace CrashScope.Core.Incidents;

public enum IncidentClassification
{
    Unclassified,
    ApplicationFailure,
    KernelOrDriverWatchdog,
    HardwareError,
    UnexpectedShutdown,
    Mixed,
    UserDiagnosticMarker
}

public enum IncidentEvidenceRole
{
    Trigger,
    Corroborating,
    Context
}

public sealed record IncidentEvidenceItem
{
    public DateTimeOffset OccurredAtUtc { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public IncidentEvidenceRole Role { get; }
    public string Source { get; }
    public string Kind { get; }
    public string Summary { get; }
    public string? EvidenceKey { get; }

    public IncidentEvidenceItem(
        DateTimeOffset occurredAtUtc,
        DateTimeOffset observedAtUtc,
        IncidentEvidenceRole role,
        string source,
        string kind,
        string summary,
        string? evidenceKey = null)
    {
        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Incident evidence occurrence time must be UTC.", nameof(occurredAtUtc));
        }

        if (observedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Incident evidence observation time must be UTC.", nameof(observedAtUtc));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        OccurredAtUtc = occurredAtUtc;
        ObservedAtUtc = observedAtUtc;
        Role = role;
        Source = source;
        Kind = kind;
        Summary = summary;
        EvidenceKey = string.IsNullOrWhiteSpace(evidenceKey) ? null : evidenceKey.Trim();
    }
}

public sealed record IncidentProcessContext
{
    public int ProcessId { get; }
    public DateTimeOffset StartTimeUtc { get; }
    public string Name { get; }
    public string ObservationState { get; }
    public DateTimeOffset ObservedAtUtc { get; }

    public IncidentProcessContext(
        int processId,
        DateTimeOffset startTimeUtc,
        string name,
        string observationState,
        DateTimeOffset observedAtUtc)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        if (startTimeUtc.Offset != TimeSpan.Zero || observedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Process incident times must be UTC.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(observationState);

        ProcessId = processId;
        StartTimeUtc = startTimeUtc;
        Name = name;
        ObservationState = observationState;
        ObservedAtUtc = observedAtUtc;
    }
}

public sealed record IncidentTelemetrySummary(
    int FrameCount,
    DateTimeOffset? WindowStartUtc,
    DateTimeOffset? WindowEndUtc,
    double? PeakCpuUtilizationPercent,
    double? PeakGpuUtilizationPercent,
    double? PeakGpuHotspotCelsius,
    double? PeakGpuMemoryUsedMiB,
    double? PeakSystemMemoryLoadPercent);

public sealed record IncidentReport
{
    public Guid IncidentId { get; }
    public IncidentClassification Classification { get; }
    public string Title { get; }
    public string Summary { get; }
    public string Assessment { get; }
    public DateTimeOffset IncidentTimeUtc { get; }
    public IncidentProcessContext? Process { get; }
    public IncidentTelemetrySummary Telemetry { get; }
    public IReadOnlyList<IncidentEvidenceItem> Evidence { get; }

    public IncidentReport(
        Guid incidentId,
        IncidentClassification classification,
        string title,
        string summary,
        string assessment,
        DateTimeOffset incidentTimeUtc,
        IncidentProcessContext? process,
        IncidentTelemetrySummary telemetry,
        IReadOnlyList<IncidentEvidenceItem> evidence)
    {
        if (incidentId == Guid.Empty)
        {
            throw new ArgumentException("Incident ID cannot be empty.", nameof(incidentId));
        }

        if (incidentTimeUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Incident time must be UTC.", nameof(incidentTimeUtc));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(assessment);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(evidence);

        IncidentId = incidentId;
        Classification = classification;
        Title = title;
        Summary = summary;
        Assessment = assessment;
        IncidentTimeUtc = incidentTimeUtc;
        Process = process;
        Telemetry = telemetry;
        Evidence = evidence.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.ObservedAtUtc).ToArray();
    }
}
