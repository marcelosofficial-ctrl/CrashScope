using CrashScope.Agent.Evidence;
using CrashScope.Agent.Processes;
using CrashScope.Agent.Sampling;
using CrashScope.Agent.Sessions;
using CrashScope.Core.Evidence;
using CrashScope.Core.Sessions;
using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Tests;

public sealed class RecentProviderEvidenceRetentionTests
{
    [Fact]
    public async Task ProcessExitRetainsProviderEvidenceForPostExitIncidentWindow()
    {
        var retained = Event(Utc(8, 4), "retained");
        var session = new RecordingSession(new[] { retained });
        var manager = Manager(
            new SequenceProbe(
                Running(4242, Utc(8, 0), "game"),
                Exited(4242)),
            new FactoryProvider(session));

        await manager.InitializeAsync();
        Assert.True((await manager.AttachAsync(4242)).IsAttached);

        var observation = await manager.ObserveActiveProcessAsync();

        Assert.NotNull(observation);
        Assert.Equal(ProcessObservationState.Exited, observation!.State);
        Assert.Null(manager.ActiveSnapshot());
        Assert.Equal(1, session.DisposeCount);

        var events = await manager.ReadActiveEvidenceWindowAsync(
            Utc(8, 3),
            Utc(8, 6));

        var item = Assert.Single(events);
        Assert.Equal("retained", item.Summary);
    }

    [Fact]
    public async Task SnapshotReadFailureNeverBlocksProcessExitCompletion()
    {
        var session = new RecordingSession(
            Array.Empty<EvidenceEvent>(),
            throwOnRead: true);
        var mode = new SamplingModeController();
        var manager = Manager(
            new SequenceProbe(
                Running(4242, Utc(8, 0), "game"),
                Exited(4242)),
            new FactoryProvider(session),
            mode);

        await manager.InitializeAsync();
        Assert.True((await manager.AttachAsync(4242)).IsAttached);

        var observation = await manager.ObserveActiveProcessAsync();

        Assert.NotNull(observation);
        Assert.Equal(ProcessObservationState.Exited, observation!.State);
        Assert.Null(manager.ActiveSnapshot());
        Assert.Equal(SamplingMode.Background, mode.Current);
        Assert.Equal(1, session.DisposeCount);

        var events = await manager.ReadActiveEvidenceWindowAsync(
            Utc(8, 0),
            Utc(8, 6));

        Assert.Empty(events);
    }

    [Fact]
    public async Task PostExitEvidenceCacheIsBoundedTo256Events()
    {
        var evidence = Enumerable.Range(0, 300)
            .Select(index =>
                Event(
                    Utc(8, 0).AddSeconds(index),
                    $"event-{index:D3}"))
            .ToArray();

        var manager = Manager(
            new SequenceProbe(
                Running(4242, Utc(8, 0), "game"),
                Exited(4242)),
            new FactoryProvider(new RecordingSession(evidence)));

        await manager.InitializeAsync();
        Assert.True((await manager.AttachAsync(4242)).IsAttached);
        await manager.ObserveActiveProcessAsync();

        var retained = await manager.ReadActiveEvidenceWindowAsync(
            Utc(8, 0),
            Utc(8, 6));

        Assert.Equal(256, retained.Count);
        Assert.Equal("event-044", retained[0].Summary);
        Assert.Equal("event-299", retained[^1].Summary);
    }

    [Fact]
    public async Task StartingReplacementWorkloadClearsPreviousExitEvidence()
    {
        var previous = new RecordingSession(
            new[] { Event(Utc(8, 4), "previous") });
        var replacement = new RecordingSession(Array.Empty<EvidenceEvent>());

        var provider = new FactoryProvider(previous, replacement);
        var manager = Manager(
            new SequenceProbe(
                Running(4242, Utc(8, 0), "old-game"),
                Exited(4242),
                Running(5252, Utc(8, 10), "new-game")),
            provider);

        await manager.InitializeAsync();
        Assert.True((await manager.AttachAsync(4242)).IsAttached);
        await manager.ObserveActiveProcessAsync();

        Assert.Single(await manager.ReadActiveEvidenceWindowAsync(
            Utc(8, 3),
            Utc(8, 6)));

        Assert.True((await manager.AttachAsync(5252)).IsAttached);
        await manager.StopAsync();

        var stale = await manager.ReadActiveEvidenceWindowAsync(
            Utc(8, 3),
            Utc(8, 6));

        Assert.Empty(stale);
        Assert.Equal(2, provider.StartCount);
    }

    private static WorkloadSessionManager Manager(
        IProcessProbe probe,
        IEvidenceProvider provider,
        SamplingModeController? mode = null) =>
        new(
            new MonitoredProcessTracker(probe),
            mode ?? new SamplingModeController(),
            new FakeRepository(),
            new FakeClock(Utc(8, 5)),
            new EvidenceProviderHost(new[] { provider }));

    private static EvidenceEvent Event(
        DateTimeOffset timestampUtc,
        string summary) =>
        new(
            timestampUtc,
            timestampUtc,
            "FakeProvider",
            "FakeKind",
            EvidenceSeverity.Information,
            summary);

    private static ProcessProbeResult Running(
        int pid,
        DateTimeOffset start,
        string name) =>
        new(
            ProcessProbeState.Running,
            pid,
            start,
            name,
            $@"C:\Games\{name}.exe",
            null);

    private static ProcessProbeResult Exited(int pid) =>
        new(
            ProcessProbeState.NotFound,
            pid,
            null,
            null,
            null,
            "Process exited.");

    private static DateTimeOffset Utc(int hour, int minute) =>
        new(2026, 9, 11, hour, minute, 0, TimeSpan.Zero);

    private sealed class FactoryProvider : IEvidenceProvider
    {
        private readonly Queue<IEvidenceProviderSession> _sessions;

        public FactoryProvider(params IEvidenceProviderSession[] sessions)
        {
            _sessions = new Queue<IEvidenceProviderSession>(sessions);
        }

        public string ProviderName => "FakeProvider";

        public bool IsEnabled => true;

        public int StartCount { get; private set; }

        public ValueTask<IEvidenceProviderSession> StartAsync(
            EvidenceProviderSessionContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;

            if (_sessions.Count == 0)
            {
                throw new InvalidOperationException("No fake provider session remains.");
            }

            return ValueTask.FromResult(_sessions.Dequeue());
        }
    }

    private sealed class RecordingSession : IEvidenceProviderSession
    {
        private readonly IReadOnlyList<EvidenceEvent> _events;
        private readonly bool _throwOnRead;

        public RecordingSession(
            IReadOnlyList<EvidenceEvent> events,
            bool throwOnRead = false)
        {
            _events = events;
            _throwOnRead = throwOnRead;
        }

        public string ProviderName => "FakeProvider";

        public int DisposeCount { get; private set; }

        public ValueTask<IReadOnlyList<EvidenceEvent>> ReadWindowAsync(
            DateTimeOffset startUtc,
            DateTimeOffset endUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_throwOnRead)
            {
                throw new InvalidOperationException("snapshot failed");
            }

            return ValueTask.FromResult(_events);
        }

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SequenceProbe : IProcessProbe
    {
        private readonly Queue<ProcessProbeResult> _results;

        public SequenceProbe(params ProcessProbeResult[] results)
        {
            _results = new Queue<ProcessProbeResult>(results);
        }

        public ValueTask<ProcessProbeResult> ProbeAsync(
            int processId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_results.Count == 0)
            {
                throw new InvalidOperationException("No fake process-probe result remains.");
            }

            return ValueTask.FromResult(_results.Dequeue());
        }
    }

    private sealed class FakeRepository : IWorkloadSessionRepository
    {
        private readonly List<WorkloadSession> _sessions = new();

        public ValueTask InitializeAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SaveAsync(
            WorkloadSession session,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var index = _sessions.FindIndex(item => item.SessionId == session.SessionId);
            if (index >= 0)
            {
                _sessions[index] = session;
            }
            else
            {
                _sessions.Add(session);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<WorkloadSession>> LoadAllAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<WorkloadSession>>(_sessions.ToArray());
    }

    private sealed class FakeClock : ISamplingClock
    {
        private readonly DateTimeOffset _utcNow;

        public FakeClock(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public DateTimeOffset GetUtcNow() => _utcNow;

        public long GetTimestamp() => 0;

        public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) =>
            TimeSpan.Zero;

        public ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}