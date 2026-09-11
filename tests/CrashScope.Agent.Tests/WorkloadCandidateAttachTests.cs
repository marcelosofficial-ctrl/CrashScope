using CrashScope.Agent.Processes;
using CrashScope.Agent.Sampling;
using CrashScope.Agent.Sessions;
using CrashScope.Core.Sessions;

namespace CrashScope.Agent.Tests;

public sealed class WorkloadCandidateAttachTests
{
    [Fact]
    public async Task MatchingCandidateIdentityAttachesNormally()
    {
        var expectedStart = Utc(8, 0);
        var repository = new FakeRepository();
        var mode = new SamplingModeController();
        var manager = Manager(
            new FakeProbe(Running(4242, expectedStart, "game")),
            repository,
            mode);
        await manager.InitializeAsync();

        var result = await manager.AttachCandidateAsync(4242, expectedStart);

        Assert.True(result.IsAttached);
        Assert.Equal(expectedStart, result.Session!.ProcessStartTimeUtc);
        Assert.Equal(1, repository.SaveCount);
        Assert.Equal(SamplingMode.Active, mode.Current);
    }

    [Fact]
    public async Task StaleCandidateIdentityIsRejectedBeforePersistenceOrActiveSampling()
    {
        var discoveredStart = Utc(8, 0);
        var replacementStart = Utc(8, 10);
        var repository = new FakeRepository();
        var mode = new SamplingModeController();
        var manager = Manager(
            new FakeProbe(Running(4242, replacementStart, "replacement")),
            repository,
            mode);
        await manager.InitializeAsync();

        var result = await manager.AttachCandidateAsync(4242, discoveredStart);

        Assert.False(result.IsAttached);
        Assert.Contains("identity changed", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, repository.SaveCount);
        Assert.Empty(manager.Snapshot());
        Assert.Null(manager.ActiveSnapshot());
        Assert.Equal(SamplingMode.Background, mode.Current);
    }

    private static WorkloadSessionManager Manager(
        IProcessProbe probe,
        IWorkloadSessionRepository repository,
        SamplingModeController mode) =>
        new(
            new MonitoredProcessTracker(probe),
            mode,
            repository,
            new FakeClock(Utc(8, 5)));

    private static ProcessProbeResult Running(
        int pid,
        DateTimeOffset start,
        string name) =>
        new(ProcessProbeState.Running, pid, start, name, $@"C:\Games\{name}.exe", null);

    private static DateTimeOffset Utc(int hour, int minute) =>
        new(2026, 9, 8, hour, minute, 0, TimeSpan.Zero);

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
            return ValueTask.FromResult(_results.Dequeue());
        }
    }

    private sealed class FakeRepository : IWorkloadSessionRepository
    {
        public int SaveCount { get; private set; }

        public ValueTask InitializeAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SaveAsync(
            WorkloadSession session,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<WorkloadSession>> LoadAllAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<WorkloadSession>>(Array.Empty<WorkloadSession>());
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
        public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) => TimeSpan.Zero;
        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
