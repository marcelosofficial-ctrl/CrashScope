namespace CrashScope.Core.Diagnostics;

public enum DiagnosticArtifactKind
{
    WindowsErrorReport,
    LiveKernelDump,
    Other
}

public sealed record DiagnosticArtifact
{
    public string Source { get; }
    public DiagnosticArtifactKind Kind { get; }
    public string ArtifactPath { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public DateTimeOffset? SourceOccurredAtUtc { get; }
    public string? ReportId { get; }
    public string? EventType { get; }
    public string? RelatedPath { get; }
    public string? Signature { get; }
    public IReadOnlyDictionary<string, string> Properties { get; }

    public DiagnosticArtifact(
        string source,
        DiagnosticArtifactKind kind,
        string artifactPath,
        DateTimeOffset observedAtUtc,
        DateTimeOffset? sourceOccurredAtUtc,
        string? reportId,
        string? eventType,
        string? relatedPath,
        string? signature,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        if (observedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Diagnostic artifact observation time must be UTC.",
                nameof(observedAtUtc));
        }

        if (sourceOccurredAtUtc.HasValue &&
            sourceOccurredAtUtc.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Diagnostic artifact source occurrence time must be UTC when known.",
                nameof(sourceOccurredAtUtc));
        }

        Source = source;
        Kind = kind;
        ArtifactPath = artifactPath;
        ObservedAtUtc = observedAtUtc;
        SourceOccurredAtUtc = sourceOccurredAtUtc;
        ReportId = NormalizeOptional(reportId);
        EventType = NormalizeOptional(eventType);
        RelatedPath = NormalizeOptional(relatedPath);
        Signature = NormalizeOptional(signature);
        Properties = properties is null
            ? new Dictionary<string, string>()
            : properties.ToDictionary(
                x => x.Key,
                x => x.Value,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public interface IDiagnosticArtifactSource
{
    string Name { get; }

    ValueTask<IReadOnlyList<DiagnosticArtifact>> ReadSinceAsync(
        DateTimeOffset sinceUtc,
        int maximumArtifacts = 256,
        CancellationToken cancellationToken = default);
}
