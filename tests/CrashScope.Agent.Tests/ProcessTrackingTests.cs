using CrashScope.Agent.Processes;

namespace CrashScope.Agent.Tests;

public sealed class ProcessTrackingTests
{
    [Fact]
    public async Task AttachUsesPidAndStartTimeAsIdentity()
    {
        var start = Utc(2026, 9, 8, 1, 2, 3);
        var probe = new FakeProbe(new ProcessProbeResult(
            ProcessProbeState.Running,
            4242,
            start,
            "game",
            @"C:\Games\game.exe",
            null));

        var tracker = new MonitoredProcessTracker(probe);
        var result = await tracker.AttachAsync(4242);

        Assert.True(result.IsAttached);
        Assert.NotNull(result.Instance);
        Assert.Equal(4242, result.Instance!.Identity.ProcessId);
        Assert.Equal(start, result.Instance.Identity.StartTimeUtc);
        Assert.Equal("game", result.Instance.Name);
        Assert.Equal(@"C:\Games\game.exe", result.Instance.ExecutablePath);
    }

    [Fact]
    public async Task SamePidAndStartTimeRemainsRunning()
    {
        var start = Utc(2026, 9, 8, 1, 2, 3);
        var target = Instance(101, start);
        var probe = new FakeProbe(new ProcessProbeResult(
            ProcessProbeState.Running,
            101,
            start,
            "game",
            null,
            null));
        var tracker = new MonitoredProcessTracker(
            probe,
            new FixedTimeProvider(Utc(2026, 9, 8, 2, 0, 0)));

        var observation = await tracker.ObserveAsync(target);

        Assert.Equal(ProcessObservationState.Running, observation.State);
        Assert.Equal(target.Identity, observation.CurrentProcess!.Identity);
    }

    [Fact]
    public async Task RecycledPidIsNotMistakenForOriginalProcess()
    {
        var target = Instance(101, Utc(2026, 9, 8, 1, 0, 0));
        var replacementStart = Utc(2026, 9, 8, 1, 30, 0);
        var probe = new FakeProbe(new ProcessProbeResult(
            ProcessProbeState.Running,
            101,
            replacementStart,
            "different-process",
            null,
            null));
        var tracker = new MonitoredProcessTracker(probe);

        var observation = await tracker.ObserveAsync(target);

        Assert.Equal(ProcessObservationState.PidReused, observation.State);
        Assert.NotNull(observation.CurrentProcess);
        Assert.Equal(replacementStart, observation.CurrentProcess!.Identity.StartTimeUtc);
    }

    [Fact]
    public async Task MissingProcessIsObservedAsExitedNotCrashed()
    {
        var target = Instance(101, Utc(2026, 9, 8, 1, 0, 0));
        var probe = new FakeProbe(new ProcessProbeResult(
            ProcessProbeState.NotFound,
            101,
            null,
            null,
            null,
            "Process was not found."));
        var tracker = new MonitoredProcessTracker(probe);

        var observation = await tracker.ObserveAsync(target);

        Assert.Equal(ProcessObservationState.Exited, observation.State);
        Assert.Null(observation.CurrentProcess);
        Assert.DoesNotContain(
            "crash",
            observation.State.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeFailureRemainsUnavailableEvidence()
    {
        var target = Instance(101, Utc(2026, 9, 8, 1, 0, 0));
        var probe = new FakeProbe(new ProcessProbeResult(
            ProcessProbeState.Unavailable,
            101,
            null,
            null,
            null,
            "Access denied."));
        var tracker = new MonitoredProcessTracker(probe);

        var observation = await tracker.ObserveAsync(target);

        Assert.Equal(ProcessObservationState.Unavailable, observation.State);
        Assert.Equal("Access denied.", observation.Detail);
    }

    [Fact]
    public async Task AttachFailsCleanlyWhenProcessCannotBeIdentified()
    {
        var probe = new FakeProbe(new ProcessProbeResult(
            ProcessProbeState.NotFound,
            55,
            null,
            null,
            null,
            "Process was not found."));
        var tracker = new MonitoredProcessTracker(probe);

        var result = await tracker.AttachAsync(55);

        Assert.False(result.IsAttached);
        Assert.Null(result.Instance);
    }

    [Fact]
    public async Task SystemProbeCanIdentifyCurrentProcess()
    {
        var probe = new SystemProcessProbe();
        var processId = Environment.ProcessId;

        var result = await probe.ProbeAsync(processId);

        Assert.Equal(ProcessProbeState.Running, result.State);
        Assert.Equal(processId, result.ProcessId);
        Assert.NotNull(result.StartTimeUtc);
        Assert.False(string.IsNullOrWhiteSpace(result.Name));
    }

    [Fact]
    public void IdentityRequiresPositivePidAndUtcStartTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProcessInstanceIdentity(0, Utc(2026, 9, 8, 1, 0, 0)));

        Assert.Throws<ArgumentException>(() =>
            new ProcessInstanceIdentity(
                1,
                new DateTimeOffset(2026, 9, 8, 1, 0, 0, TimeSpan.FromHours(9))));
    }

    private static ProcessInstance Instance(
        int processId,
        DateTimeOffset startTimeUtc) => new(
        new ProcessInstanceIdentity(processId, startTimeUtc),
        "game",
        null);

    private static DateTimeOffset Utc(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second) =>
        new(year, month, day, hour, minute, second, TimeSpan.Zero);

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
                throw new InvalidOperationException("No fake probe result remains.");
            }

            return ValueTask.FromResult(_results.Dequeue());
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
