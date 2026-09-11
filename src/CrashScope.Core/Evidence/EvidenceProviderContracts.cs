using System.Collections.ObjectModel;

namespace CrashScope.Core.Evidence;

public sealed record EvidenceProviderSessionContext
{
    public Guid SessionId { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public int? ProcessId { get; }

    public string? ExecutablePath { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    public EvidenceProviderSessionContext(
        Guid sessionId,
        DateTimeOffset startedAtUtc,
        int? processId = null,
        string? executablePath = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Evidence provider session ID cannot be empty.",
                nameof(sessionId));
        }

        if (startedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Evidence provider session start time must be UTC.",
                nameof(startedAtUtc));
        }

        if (processId is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processId),
                processId,
                "Process ID must be positive when supplied.");
        }

        var metadataCopy = new Dictionary<string, string>(StringComparer.Ordinal);
        if (metadata is not null)
        {
            foreach (var pair in metadata)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    throw new ArgumentException(
                        "Evidence provider metadata keys cannot be blank.",
                        nameof(metadata));
                }

                if (pair.Value is null)
                {
                    throw new ArgumentException(
                        "Evidence provider metadata values cannot be null.",
                        nameof(metadata));
                }

                metadataCopy.Add(pair.Key, pair.Value);
            }
        }

        SessionId = sessionId;
        StartedAtUtc = startedAtUtc;
        ProcessId = processId;
        ExecutablePath = string.IsNullOrWhiteSpace(executablePath)
            ? null
            : executablePath.Trim();
        Metadata = new ReadOnlyDictionary<string, string>(metadataCopy);
    }
}

public interface IEvidenceProvider
{
    string ProviderName { get; }

    bool IsEnabled { get; }

    ValueTask<IEvidenceProviderSession> StartAsync(
        EvidenceProviderSessionContext context,
        CancellationToken cancellationToken = default);
}

public interface IEvidenceProviderSession : IAsyncDisposable
{
    string ProviderName { get; }

    ValueTask<IReadOnlyList<EvidenceEvent>> ReadWindowAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}