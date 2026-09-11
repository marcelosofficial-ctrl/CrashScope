using System.Collections.ObjectModel;

namespace CrashScope.Core.Evidence;

public enum EvidenceSeverity
{
    Information = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

public sealed record EvidenceEvent
{
    public DateTimeOffset TimestampUtc { get; }

    public DateTimeOffset ObservedAtUtc { get; }

    public string Source { get; }

    public string Kind { get; }

    public EvidenceSeverity Severity { get; }

    public string Summary { get; }

    public IReadOnlyDictionary<string, string> Details { get; }

    public EvidenceEvent(
        DateTimeOffset timestampUtc,
        DateTimeOffset observedAtUtc,
        string source,
        string kind,
        EvidenceSeverity severity,
        string summary,
        IReadOnlyDictionary<string, string>? details = null)
    {
        if (timestampUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Evidence event timestamps must be UTC.",
                nameof(timestampUtc));
        }

        if (observedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Evidence event observation times must be UTC.",
                nameof(observedAtUtc));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException(
                "Evidence event source is required.",
                nameof(source));
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException(
                "Evidence event kind is required.",
                nameof(kind));
        }

        if (!Enum.IsDefined(typeof(EvidenceSeverity), severity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(severity),
                severity,
                "Evidence event severity is not defined.");
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException(
                "Evidence event summary is required.",
                nameof(summary));
        }

        var detailCopy = new Dictionary<string, string>(StringComparer.Ordinal);
        if (details is not null)
        {
            foreach (var pair in details)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    throw new ArgumentException(
                        "Evidence event detail keys cannot be blank.",
                        nameof(details));
                }

                if (pair.Value is null)
                {
                    throw new ArgumentException(
                        "Evidence event detail values cannot be null.",
                        nameof(details));
                }

                detailCopy.Add(pair.Key, pair.Value);
            }
        }

        TimestampUtc = timestampUtc;
        ObservedAtUtc = observedAtUtc;
        Source = source.Trim();
        Kind = kind.Trim();
        Severity = severity;
        Summary = summary.Trim();
        Details = new ReadOnlyDictionary<string, string>(detailCopy);
    }
}