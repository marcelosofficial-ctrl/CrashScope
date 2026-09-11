using CrashScope.Agent.Processes;
using CrashScope.Agent.Sampling;
using CrashScope.Agent.Settings;
using CrashScope.Core.Sessions;
using CrashScope.Infrastructure.Persistence;

namespace CrashScope.Agent.Sessions;

internal sealed record AutomaticWorkloadMonitorStatus(
    bool Enabled,
    string State,
    string? CandidateProcessName,
    int? CandidateScore,
    int ConfirmationCount,
    DateTimeOffset? SuppressedUntilUtc,
    string? LastAutoAttachReason,
    double ObservationIntervalSeconds);

internal static class AutomaticWorkloadPolicy
{
    internal const int MinimumAutomaticScore = 90;

    public static bool IsEligible(WorkloadCandidate? candidate) =>
        candidate is not null &&
        candidate.Recommendation == WorkloadRecommendation.Recommended &&
        candidate.RecommendationScore >= MinimumAutomaticScore &&
        candidate.Kind != WorkloadKind.GeneralApplication &&
        candidate.Kind != WorkloadKind.HelperOrSystem;
}

internal sealed class AutomaticCandidateGate
{
    internal const int RequiredConfirmations = 2;

    public WorkloadCandidate? Candidate { get; private set; }
    public int ConfirmationCount { get; private set; }

    public bool Observe(WorkloadCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (Candidate is not null &&
            Candidate.ProcessId == candidate.ProcessId &&
            Candidate.ProcessStartTimeUtc == candidate.ProcessStartTimeUtc)
        {
            Candidate = candidate;
            ConfirmationCount++;
        }
        else
        {
            Candidate = candidate;
            ConfirmationCount = 1;
        }

        return ConfirmationCount >= RequiredConfirmations;
    }

    public void Reset()
    {
        Candidate = null;
        ConfirmationCount = 0;
    }
}

internal sealed class AutomaticWorkloadMonitor
{
    internal static TimeSpan DefaultObservationInterval { get; } = TimeSpan.FromSeconds(5);
    internal static TimeSpan DefaultManualStopCooldown { get; } = TimeSpan.FromMinutes(10);

    private readonly IForegroundProcessObserver _foreground;
    private readonly WorkloadDiscoveryService _discovery;
    private readonly WorkloadSessionManager _sessions;
    private readonly ISessionEnvironmentSnapshotProvider _environmentProvider;
    private readonly SqliteSessionEnvironmentStore _environments;
    private readonly ISamplingClock _clock;
    private readonly CrashScopeSettingsState? _settings;
    private readonly TimeSpan _observationInterval;
    private readonly object _sync = new();
    private readonly AutomaticCandidateGate _candidateGate = new();

    private DateTimeOffset? _suppressedUntilUtc;
    private string? _lastAutoAttachReason;
    private string _state = "Ready";

    public AutomaticWorkloadMonitor(
        IForegroundProcessObserver foreground,
        WorkloadDiscoveryService discovery,
        WorkloadSessionManager sessions,
        ISessionEnvironmentSnapshotProvider environmentProvider,
        SqliteSessionEnvironmentStore environments,
        ISamplingClock clock,
        TimeSpan? observationInterval = null,
        CrashScopeSettingsState? settings = null)
    {
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(environmentProvider);
        ArgumentNullException.ThrowIfNull(environments);
        ArgumentNullException.ThrowIfNull(clock);

        _foreground = foreground;
        _discovery = discovery;
        _sessions = sessions;
        _environmentProvider = environmentProvider;
        _environments = environments;
        _clock = clock;
        _settings = settings;
        _observationInterval = observationInterval ?? DefaultObservationInterval;

        if (_observationInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(observationInterval));
        }
    }

    public AutomaticWorkloadMonitorStatus StatusSnapshot()
    {
        var enabled = IsEnabled();
        lock (_sync)
        {
            return new AutomaticWorkloadMonitorStatus(
                enabled,
                enabled ? _state : "Disabled",
                enabled ? _candidateGate.Candidate?.ProcessName : null,
                enabled ? _candidateGate.Candidate?.RecommendationScore : null,
                enabled ? _candidateGate.ConfirmationCount : 0,
                enabled ? _suppressedUntilUtc : null,
                _lastAutoAttachReason,
                _observationInterval.TotalSeconds);
        }
    }

    public void SuppressAfterManualStop(TimeSpan? duration = null)
    {
        var cooldown = duration ?? DefaultManualStopCooldown;
        if (cooldown <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        lock (_sync)
        {
            _suppressedUntilUtc = _clock.GetUtcNow().ToUniversalTime() + cooldown;
            _candidateGate.Reset();
            _state = "PausedByUser";
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ObserveOnceAsync(cancellationToken).ConfigureAwait(false);
            await _clock.DelayAsync(_observationInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    internal async ValueTask ObserveOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsEnabled())
        {
            ResetPending("Disabled");
            return;
        }

        if (_sessions.ActiveSnapshot() is not null)
        {
            ResetPending("Monitoring");
            return;
        }

        var now = _clock.GetUtcNow().ToUniversalTime();
        lock (_sync)
        {
            if (_suppressedUntilUtc.HasValue && now < _suppressedUntilUtc.Value)
            {
                _state = "PausedByUser";
                _candidateGate.Reset();
                return;
            }

            if (_suppressedUntilUtc.HasValue)
            {
                _suppressedUntilUtc = null;
                _state = "Ready";
            }
        }

        var processId = _foreground.GetForegroundProcessId();
        if (!processId.HasValue)
        {
            ResetPending("Ready");
            return;
        }

        var candidate = _discovery.DiscoverProcess(processId.Value);
        if (!AutomaticWorkloadPolicy.IsEligible(candidate))
        {
            ResetPending("Ready");
            return;
        }

        bool confirmed;
        lock (_sync)
        {
            confirmed = _candidateGate.Observe(candidate!);
            _state = "Confirming";
        }

        if (!confirmed)
        {
            return;
        }

        var attach = await _sessions.AttachCandidateAsync(
            candidate!.ProcessId,
            candidate.ProcessStartTimeUtc,
            cancellationToken).ConfigureAwait(false);

        if (!attach.IsAttached)
        {
            ResetPending("Ready");
            return;
        }

        try
        {
            var environment = await _environmentProvider
                .CaptureAsync(cancellationToken)
                .ConfigureAwait(false);
            await _environments
                .SaveAsync(attach.Session!.SessionId, environment, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Environment enrichment must never prevent automatic monitoring.
        }

        lock (_sync)
        {
            _lastAutoAttachReason = candidate.RecommendationReasons.FirstOrDefault()
                ?? $"Recommended workload score {candidate.RecommendationScore}.";
            _candidateGate.Reset();
            _state = "Monitoring";
        }
    }

    private bool IsEnabled() =>
        _settings?.Snapshot().AutoAssistEnabled ?? true;

    private void ResetPending(string state)
    {
        lock (_sync)
        {
            _candidateGate.Reset();
            _state = state;
        }
    }
}