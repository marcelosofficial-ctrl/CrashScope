namespace CrashScope.Core.Diagnostics;

public enum DiagnosticEventKind
{
    ApplicationFault,
    ApplicationHang,
    WindowsErrorReport,
    HardwareError,
    KernelPower,
    DisplayDriver,
    Other
}

public sealed record DiagnosticEvent
{
    public string Source { get; }
    public string LogName { get; }
    public string ProviderName { get; }
    public int EventId { get; }
    public long? RecordId { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public DateTimeOffset? SourceOccurredAtUtc { get; }
    public DiagnosticEventKind Kind { get; }
    public string? Level { get; }
    public string? Message { get; }

    public DiagnosticEvent(
        string source,
        string logName,
        string providerName,
        int eventId,
        long? recordId,
        DateTimeOffset observedAtUtc,
        DateTimeOffset? sourceOccurredAtUtc,
        DiagnosticEventKind kind,
        string? level,
        string? message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(logName);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        if (eventId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(eventId));
        }

        if (recordId is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recordId));
        }

        if (observedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Diagnostic event observation time must be UTC.",
                nameof(observedAtUtc));
        }

        if (sourceOccurredAtUtc.HasValue &&
            sourceOccurredAtUtc.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Diagnostic event source occurrence time must be UTC when known.",
                nameof(sourceOccurredAtUtc));
        }

        Source = source;
        LogName = logName;
        ProviderName = providerName;
        EventId = eventId;
        RecordId = recordId;
        ObservedAtUtc = observedAtUtc;
        SourceOccurredAtUtc = sourceOccurredAtUtc;
        Kind = kind;
        Level = string.IsNullOrWhiteSpace(level) ? null : level;
        Message = string.IsNullOrWhiteSpace(message) ? null : message;
    }
}

public interface IDiagnosticEventSource
{
    string Name { get; }

    ValueTask<IReadOnlyList<DiagnosticEvent>> ReadSinceAsync(
        DateTimeOffset sinceUtc,
        int maximumEvents = 256,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional capability for event sources that can wake the incident pipeline when
/// new matching evidence arrives, while retaining a sparse history reconciliation path.
/// </summary>
public interface IRealtimeDiagnosticEventSource : IDiagnosticEventSource
{
    bool RealtimeAvailable { get; }

    ValueTask WaitForEventAsync(CancellationToken cancellationToken = default);

    ValueTask ReconcileAsync(
        DateTimeOffset sinceUtc,
        int maximumEvents = 512,
        CancellationToken cancellationToken = default);
}
