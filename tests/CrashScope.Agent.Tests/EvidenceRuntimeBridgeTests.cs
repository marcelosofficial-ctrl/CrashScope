using CrashScope.Agent.Evidence;
using CrashScope.Agent.Processes;
using CrashScope.Agent.Sampling;
using CrashScope.Agent.Sessions;
using CrashScope.Core.Evidence;
using CrashScope.Core.Sessions;

namespace CrashScope.Agent.Tests;

public sealed class EvidenceRuntimeBridgeTests
{
    [Fact]
    public async Task ReadBeforeAttachReturnsEmptyWithoutStartingProvider()
    {
        var provider = new RecordingProvider(
            new RecordingSession("fake", Array.Empty<EvidenceEvent>()));
        var manager = Manager(provider);

        await manager.InitializeAsync();

        var events = await manager.ReadActiveEvidenceWindowAsync(
            Utc(8, 0),
            Utc(8, 10));

        Assert.Empty(events);
        Assert.Equal(0, provider.StartCount);
    }

    [Fact]
    public async Task ReadDuringActiveSessionUsesExactWindowAndReturnsProviderEvents()
    {
        var first = Event(Utc(8, 4), Utc(8, 4), "first");
        var second = Event(Utc(8, 6), Utc(8, 6), "second");
        var session = new RecordingSession("fake", new[] { second, first });
        var provider = new RecordingProvider(session);
        var manager = Manager(provider);

        await manager.InitializeAsync();
        Assert.True((await manager.AttachAsync(4242)).IsAttached);

        var start = Utc(8, 3);
        var end = Utc(8, 7);
        var events = await manager.ReadActiveEvidenceWindowAsync(start, end);

        Assert.Equal(1, session.ReadCount);
        Assert.Equal(start, session.LastStartUtc);
        Assert.Equal(end, session.LastEndUtc);
        Assert.Collection(
            events,
            item => Assert.Equal("first", item.Summary),
            item => Assert.Equal("second", item.Summary));

        await manager.StopAsync();
    }

    [Fact]
    public async Task ProviderReadFailureDoesNotBlockHealthyProviderEvents()
    {
        var healthyEvent = Event(Utc(8, 5), Utc(8, 5), "healthy");
        var broken = new RecordingProvider(
            new RecordingSession("broken", Array.Empty<EvidenceEvent>(), throwOnRead: true));
        var healthy = new RecordingProvider(
            new RecordingSession("healthy", new[] { healthyEvent }));
        var manager = Manager(broken, healthy);

        await manager.InitializeAsync();
        Assert.True((await manager.AttachAsync(4242)).IsAttached);

        var events = await manager.ReadActiveEvidenceWindowAsync(
            Utc(8, 0),
            Utc(8, 10));

        var item = Assert.Single(events);
        Assert.Equal("healthy", item.Summary);

        await manager.StopAsync();
    }

    [Fact]
    public async Task ReadAfterStopReturnsEmptyWithoutReadingDisposedProvider()
    {
        var session = new RecordingSession(
            "fake",
            new[] { Event(Utc(8, 5), Utc(8, 5), "event") });
        var manager = Manager(new RecordingProvider(session));

        await manager.InitializeAsync();
        Assert.True((await manager.AttachAsync(4242)).IsAttached);
        await manager.StopAsync();

        var readsBefore = session.ReadCount;
        var events = await manager.ReadActiveEvidenceWindowAsync(
            Utc(8, 0),
            Utc(8, 10));

        Assert.Empty(events);
        Assert.Equal(readsBefore, session.ReadCount);
        Assert.Equal(1, session.DisposeCount);
    }

    private static WorkloadSessionManager Manager(params IEvidenceProvider[] providers) =>
        new(
            new MonitoredProcessTracker(
                new FakeProbe(Running(4242, Utc(8, 0), "game"))),
            new SamplingModeController(),
            new FakeRepository(),
            new FakeClock(Utc(8, 5)),
            new EvidenceProviderHost(providers));

    private static EvidenceEvent Event(
        DateTimeOffset timestampUtc,
        DateTimeOffset observedAtUtc,
        string summary) =>
        new(
            timestampUtc,
            observedAtUtc,
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

    private static DateTimeOffset Utc(int hour, int minute) =>
        new(2026, 9, 11, hour, minute, 0, TimeSpan.Zero);

    private sealed class RecordingProvider : IEvidenceProvider
    {
        private readonly IEvidenceProviderSession _session;

        public RecordingProvider(IEvidenceProviderSession session)
        {
            _session = session;
        }

        public string ProviderName => _session.ProviderName;

        public bool IsEnabled => true;

        public int StartCount { get; private set; }

        public ValueTask<IEvidenceProviderSession> StartAsync(
            EvidenceProviderSessionContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            return ValueTask.FromResult(_session);
        }
    }

    private sealed class RecordingSession : IEvidenceProviderSession
    {
        private readonly IReadOnlyList<EvidenceEvent> _events;
        private readonly bool _throwOnRead;

        public RecordingSession(
            string providerName,
            IReadOnlyList<EvidenceEvent> events,
            bool throwOnRead = false)
        {
            ProviderName = providerName;
            _events = events;
            _throwOnRead = throwOnRead;
        }

        public string ProviderName { get; }

        public int ReadCount { get; private set; }

        public int DisposeCount { get; private set; }

        public DateTimeOffset? LastStartUtc { get; private set; }

        public DateTimeOffset? LastEndUtc { get; private set; }

        public ValueTask<IReadOnlyList<EvidenceEvent>> ReadWindowAsync(
            DateTimeOffset startUtc,
            DateTimeOffset endUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            LastStartUtc = startUtc;
            LastEndUtc = endUtc;

            if (_throwOnRead)
            {
                throw new InvalidOperationException("read failed");
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

    private sealed class FakeProbe : IProcessProbe
    {
        private readonly ProcessProbeResult _result;

        public FakeProbe(ProcessProbeResult result)
        {
            _result = result;
        }

        public ValueTask<ProcessProbeResult> ProbeAsync(
            int processId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_result);
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