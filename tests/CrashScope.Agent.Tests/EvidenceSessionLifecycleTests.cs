using CrashScope.Agent.Evidence;
using CrashScope.Agent.Processes;
using CrashScope.Agent.Sampling;
using CrashScope.Agent.Sessions;
using CrashScope.Core.Evidence;
using CrashScope.Core.Sessions;

namespace CrashScope.Agent.Tests;

public sealed class EvidenceSessionLifecycleTests
{
    [Fact]
    public async Task AttachStartsProviderWithWorkloadContextAndStopDisposesIt()
    {
        var providerSession = new RecordingEvidenceSession("fake-evidence");
        var provider = new RecordingEvidenceProvider(
            "fake-evidence",
            (_, _) => ValueTask.FromResult<IEvidenceProviderSession>(providerSession));
        var mode = new SamplingModeController();
        var manager = Manager(
            new FakeProbe(Running(4242, Utc(8, 0), "game")),
            new FakeRepository(),
            mode,
            provider);

        await manager.InitializeAsync();
        var attached = await manager.AttachAsync(4242);

        Assert.True(attached.IsAttached);
        Assert.Equal(1, provider.StartCount);
        var context = Assert.IsType<EvidenceProviderSessionContext>(provider.LastContext);
        Assert.Equal(attached.Session!.SessionId, context.SessionId);
        Assert.Equal(attached.Session.StartedAtUtc, context.StartedAtUtc);
        Assert.Equal(4242, context.ProcessId);
        Assert.Equal(@"C:\Games\game.exe", context.ExecutablePath);
        Assert.Equal(0, providerSession.StopCount);
        Assert.Equal(0, providerSession.DisposeCount);

        var stopped = await manager.StopAsync();

        Assert.NotNull(stopped);
        Assert.Equal(SessionEndReason.UserStopped, stopped!.EndReason);
        Assert.Equal(SamplingMode.Background, mode.Current);
        Assert.Null(manager.ActiveSnapshot());
        Assert.Equal(1, providerSession.StopCount);
        Assert.Equal(1, providerSession.DisposeCount);
    }

    [Fact]
    public async Task ProviderStartFailureDoesNotPreventWorkloadAttach()
    {
        var provider = new RecordingEvidenceProvider(
            "broken-evidence",
            (_, _) => throw new InvalidOperationException("provider start failed"));
        var mode = new SamplingModeController();
        var manager = Manager(
            new FakeProbe(Running(4242, Utc(8, 0), "game")),
            new FakeRepository(),
            mode,
            provider);

        await manager.InitializeAsync();
        var attached = await manager.AttachAsync(4242);

        Assert.True(attached.IsAttached);
        Assert.Equal(1, provider.StartCount);
        Assert.NotNull(manager.ActiveSnapshot());
        Assert.Equal(SamplingMode.Active, mode.Current);

        var stopped = await manager.StopAsync();
        Assert.NotNull(stopped);
        Assert.Equal(SamplingMode.Background, mode.Current);
    }

    [Fact]
    public async Task ProviderStopFailureDoesNotPreventCoreSessionCompletion()
    {
        var providerSession = new RecordingEvidenceSession(
            "broken-stop",
            throwOnStop: true);
        var provider = new RecordingEvidenceProvider(
            "broken-stop",
            (_, _) => ValueTask.FromResult<IEvidenceProviderSession>(providerSession));
        var mode = new SamplingModeController();
        var manager = Manager(
            new FakeProbe(Running(4242, Utc(8, 0), "game")),
            new FakeRepository(),
            mode,
            provider);

        await manager.InitializeAsync();
        Assert.True((await manager.AttachAsync(4242)).IsAttached);

        var stopped = await manager.StopAsync();

        Assert.NotNull(stopped);
        Assert.Equal(SessionEndReason.UserStopped, stopped!.EndReason);
        Assert.Null(manager.ActiveSnapshot());
        Assert.Equal(SamplingMode.Background, mode.Current);
        Assert.Equal(1, providerSession.StopCount);
        Assert.Equal(1, providerSession.DisposeCount);
    }

    [Fact]
    public async Task PersistenceFailureAfterProviderStartCleansUpProviderSession()
    {
        var providerSession = new RecordingEvidenceSession("fake-evidence");
        var provider = new RecordingEvidenceProvider(
            "fake-evidence",
            (_, _) => ValueTask.FromResult<IEvidenceProviderSession>(providerSession));
        var mode = new SamplingModeController();
        var manager = Manager(
            new FakeProbe(Running(4242, Utc(8, 0), "game")),
            new FakeRepository(throwOnSave: true),
            mode,
            provider);

        await manager.InitializeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await manager.AttachAsync(4242));

        Assert.Null(manager.ActiveSnapshot());
        Assert.Empty(manager.Snapshot());
        Assert.Equal(SamplingMode.Background, mode.Current);
        Assert.Equal(1, providerSession.StopCount);
        Assert.Equal(1, providerSession.DisposeCount);
    }

    [Fact]
    public async Task ProcessExitAlsoDisposesProviderSession()
    {
        var providerSession = new RecordingEvidenceSession("fake-evidence");
        var provider = new RecordingEvidenceProvider(
            "fake-evidence",
            (_, _) => ValueTask.FromResult<IEvidenceProviderSession>(providerSession));
        var mode = new SamplingModeController();
        var manager = Manager(
            new FakeProbe(
                Running(4242, Utc(8, 0), "game"),
                new ProcessProbeResult(
                    ProcessProbeState.NotFound,
                    4242,
                    null,
                    null,
                    null,
                    "Process was not found.")),
            new FakeRepository(),
            mode,
            provider);

        await manager.InitializeAsync();
        Assert.True((await manager.AttachAsync(4242)).IsAttached);

        var observation = await manager.ObserveActiveProcessAsync();

        Assert.Equal(ProcessObservationState.Exited, observation!.State);
        Assert.Equal(SessionEndReason.ProcessExited, Assert.Single(manager.Snapshot()).EndReason);
        Assert.Null(manager.ActiveSnapshot());
        Assert.Equal(SamplingMode.Background, mode.Current);
        Assert.Equal(1, providerSession.StopCount);
        Assert.Equal(1, providerSession.DisposeCount);
    }

    private static WorkloadSessionManager Manager(
        IProcessProbe probe,
        IWorkloadSessionRepository repository,
        SamplingModeController mode,
        params IEvidenceProvider[] providers) =>
        new(
            new MonitoredProcessTracker(probe),
            mode,
            repository,
            new FakeClock(Utc(8, 5)),
            new EvidenceProviderHost(providers));

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

    private sealed class RecordingEvidenceProvider : IEvidenceProvider
    {
        private readonly Func<
            EvidenceProviderSessionContext,
            CancellationToken,
            ValueTask<IEvidenceProviderSession>> _start;

        public RecordingEvidenceProvider(
            string providerName,
            Func<
                EvidenceProviderSessionContext,
                CancellationToken,
                ValueTask<IEvidenceProviderSession>> start)
        {
            ProviderName = providerName;
            _start = start;
        }

        public string ProviderName { get; }

        public bool IsEnabled => true;

        public int StartCount { get; private set; }

        public EvidenceProviderSessionContext? LastContext { get; private set; }

        public ValueTask<IEvidenceProviderSession> StartAsync(
            EvidenceProviderSessionContext context,
            CancellationToken cancellationToken = default)
        {
            StartCount++;
            LastContext = context;
            return _start(context, cancellationToken);
        }
    }

    private sealed class RecordingEvidenceSession : IEvidenceProviderSession
    {
        private readonly bool _throwOnStop;

        public RecordingEvidenceSession(
            string providerName,
            bool throwOnStop = false)
        {
            ProviderName = providerName;
            _throwOnStop = throwOnStop;
        }

        public string ProviderName { get; }

        public int StopCount { get; private set; }

        public int DisposeCount { get; private set; }

        public ValueTask<IReadOnlyList<EvidenceEvent>> ReadWindowAsync(
            DateTimeOffset startUtc,
            DateTimeOffset endUtc,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<EvidenceEvent>>(
                Array.Empty<EvidenceEvent>());

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;

            if (_throwOnStop)
            {
                throw new InvalidOperationException("provider stop failed");
            }

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
        private readonly Queue<ProcessProbeResult> _results;

        public FakeProbe(params ProcessProbeResult[] results)
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
                throw new InvalidOperationException("No fake process result remains.");
            }

            return ValueTask.FromResult(_results.Dequeue());
        }
    }

    private sealed class FakeRepository : IWorkloadSessionRepository
    {
        private readonly bool _throwOnSave;
        private readonly List<WorkloadSession> _sessions = new();

        public FakeRepository(bool throwOnSave = false)
        {
            _throwOnSave = throwOnSave;
        }

        public ValueTask InitializeAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SaveAsync(
            WorkloadSession session,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_throwOnSave)
            {
                throw new InvalidOperationException("persistence failed");
            }

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