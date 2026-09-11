using CrashScope.Core.Evidence;

namespace CrashScope.Agent.Evidence;

internal enum EvidenceProviderFailureStage
{
    Start = 0,
    Read = 1,
    Stop = 2,
    Dispose = 3
}

internal sealed record EvidenceProviderFailure(
    string ProviderName,
    EvidenceProviderFailureStage Stage,
    string ErrorType,
    string Message);

internal sealed record EvidenceProviderWindowResult(
    IReadOnlyList<EvidenceEvent> Events,
    IReadOnlyList<EvidenceProviderFailure> Failures);

internal sealed record ActiveEvidenceProviderSession(
    string ProviderName,
    IEvidenceProviderSession Session);

internal sealed class EvidenceProviderHost
{
    private readonly IReadOnlyList<IEvidenceProvider> _providers;

    public EvidenceProviderHost(IEnumerable<IEvidenceProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var materialized = providers.ToArray();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in materialized)
        {
            ArgumentNullException.ThrowIfNull(provider);

            if (string.IsNullOrWhiteSpace(provider.ProviderName))
            {
                throw new ArgumentException(
                    "Evidence providers must have a non-blank provider name.",
                    nameof(providers));
            }

            if (!names.Add(provider.ProviderName))
            {
                throw new ArgumentException(
                    $"Duplicate evidence provider name '{provider.ProviderName}'.",
                    nameof(providers));
            }
        }

        _providers = materialized;
    }

    public async ValueTask<EvidenceProviderSessionSet> StartAsync(
        EvidenceProviderSessionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sessions = new List<ActiveEvidenceProviderSession>();
        var failures = new List<EvidenceProviderFailure>();

        try
        {
            foreach (var provider in _providers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!provider.IsEnabled)
                {
                    continue;
                }

                try
                {
                    var session = await provider
                        .StartAsync(context, cancellationToken)
                        .ConfigureAwait(false);

                    if (session is null)
                    {
                        throw new InvalidOperationException(
                            $"Evidence provider '{provider.ProviderName}' returned no session.");
                    }

                    if (!string.Equals(
                            session.ProviderName,
                            provider.ProviderName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        await session.DisposeAsync().ConfigureAwait(false);
                        throw new InvalidOperationException(
                            $"Evidence provider '{provider.ProviderName}' returned a session " +
                            $"named '{session.ProviderName}'.");
                    }

                    sessions.Add(new ActiveEvidenceProviderSession(
                        provider.ProviderName,
                        session));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add(CreateFailure(
                        provider.ProviderName,
                        EvidenceProviderFailureStage.Start,
                        exception));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CleanupStartedSessionsAsync(sessions).ConfigureAwait(false);
            throw;
        }

        return new EvidenceProviderSessionSet(sessions, failures);
    }

    private static EvidenceProviderFailure CreateFailure(
        string providerName,
        EvidenceProviderFailureStage stage,
        Exception exception) =>
        new(
            providerName,
            stage,
            exception.GetType().Name,
            exception.Message);

    private static async ValueTask CleanupStartedSessionsAsync(
        IReadOnlyList<ActiveEvidenceProviderSession> sessions)
    {
        for (var index = sessions.Count - 1; index >= 0; index--)
        {
            var active = sessions[index];

            try
            {
                await active.Session.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Cancellation cleanup is best-effort and must continue to every session.
            }

            try
            {
                await active.Session.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Cancellation cleanup is best-effort and must continue to every session.
            }
        }
    }
}

internal sealed class EvidenceProviderSessionSet : IAsyncDisposable
{
    private readonly IReadOnlyList<ActiveEvidenceProviderSession> _sessions;
    private readonly SemaphoreSlim _stopGate = new(1, 1);
    private IReadOnlyList<EvidenceProviderFailure> _stopFailures =
        Array.Empty<EvidenceProviderFailure>();
    private bool _stopped;

    public EvidenceProviderSessionSet(
        IReadOnlyList<ActiveEvidenceProviderSession> sessions,
        IReadOnlyList<EvidenceProviderFailure> startFailures)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(startFailures);

        _sessions = sessions.ToArray();
        StartFailures = startFailures.ToArray();
    }

    public int ActiveProviderCount => _sessions.Count;

    public IReadOnlyList<EvidenceProviderFailure> StartFailures { get; }

    public async ValueTask<EvidenceProviderWindowResult> ReadWindowAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken = default)
    {
        if (startUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Evidence window start time must be UTC.",
                nameof(startUtc));
        }

        if (endUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Evidence window end time must be UTC.",
                nameof(endUtc));
        }

        if (endUtc < startUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endUtc),
                endUtc,
                "Evidence window end time cannot be earlier than its start time.");
        }

        if (_stopped)
        {
            throw new InvalidOperationException(
                "Evidence cannot be read after provider sessions are stopped.");
        }

        var events = new List<EvidenceEvent>();
        var failures = new List<EvidenceProviderFailure>();

        foreach (var active in _sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var providerEvents = await active.Session
                    .ReadWindowAsync(startUtc, endUtc, cancellationToken)
                    .ConfigureAwait(false);

                if (providerEvents is null)
                {
                    throw new InvalidOperationException(
                        $"Evidence provider '{active.ProviderName}' returned no event collection.");
                }

                events.AddRange(providerEvents.Where(item =>
                    item.TimestampUtc >= startUtc &&
                    item.TimestampUtc <= endUtc));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(new EvidenceProviderFailure(
                    active.ProviderName,
                    EvidenceProviderFailureStage.Read,
                    exception.GetType().Name,
                    exception.Message));
            }
        }

        var ordered = events
            .OrderBy(item => item.TimestampUtc)
            .ThenBy(item => item.ObservedAtUtc)
            .ThenBy(item => item.Source, StringComparer.Ordinal)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ToArray();

        return new EvidenceProviderWindowResult(
            ordered,
            failures.ToArray());
    }

    public async ValueTask<IReadOnlyList<EvidenceProviderFailure>> StopAsync(
        CancellationToken cancellationToken = default)
    {
        await _stopGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_stopped)
            {
                return _stopFailures;
            }

            var failures = new List<EvidenceProviderFailure>();

            for (var index = _sessions.Count - 1; index >= 0; index--)
            {
                var active = _sessions[index];

                try
                {
                    await active.Session
                        .StopAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(new EvidenceProviderFailure(
                        active.ProviderName,
                        EvidenceProviderFailureStage.Stop,
                        exception.GetType().Name,
                        exception.Message));
                }

                try
                {
                    await active.Session.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(new EvidenceProviderFailure(
                        active.ProviderName,
                        EvidenceProviderFailureStage.Dispose,
                        exception.GetType().Name,
                        exception.Message));
                }
            }

            _stopFailures = failures.ToArray();
            _stopped = true;
            return _stopFailures;
        }
        finally
        {
            _stopGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _stopGate.Dispose();
    }
}