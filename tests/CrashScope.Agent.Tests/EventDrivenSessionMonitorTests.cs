using CrashScope.Agent.Processes;
using CrashScope.Agent.Sampling;
using CrashScope.Agent.Sessions;
using CrashScope.Core.Sessions;

namespace CrashScope.Agent.Tests;

public sealed class EventDrivenSessionMonitorTests
{
    [Fact]
    public async Task RunAsync_WaitsForExitWithoutPeriodicProcessProbes()
    {
        var start = Utc(8, 0);
        var probe = new CountingProbe(Running(4242, start, "game"));
        var waiter = new ControlledExitWaiter();
        var repository = new FakeRepository();
        var mode = new SamplingModeController();
        var manager = new WorkloadSessionManager(
            new MonitoredProcessTracker(probe),
            mode,
            repository,
            new FakeClock(Utc(8, 5)),
            waiter);

        await manager.InitializeAsync();
        var attached = await manager.AttachAsync(4242);
        Assert.True(attached.IsAttached);

        using var cancellation = new CancellationTokenSource();
        var monitorTask = manager.RunAsync(cancellation.Token);

        await waiter.Entered.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, probe.CallCount); // attach only; no one-second observation loop

        waiter.Complete(new ProcessExitWaitResult(ProcessExitWaitState.Exited));
        await WaitUntilAsync(() => manager.ActiveSnapshot() is null);

        var session = Assert.Single(manager.Snapshot());
        Assert.Equal(SessionEndReason.ProcessExited, session.EndReason);
        Assert.Equal(SamplingMode.Background, mode.Current);
        Assert.Equal(1, probe.CallCount);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await monitorTask);
    }

    [Fact]
    public async Task StaleObservationCannotCloseAReplacementSession()
    {
        var oldStart = Utc(8, 0);
        var newStart = Utc(8, 30);
        var probe = new RacingProbe(
            Running(4242, oldStart, "old-game"),
            Running(5252, newStart, "new-game"));
        var manager = new WorkloadSessionManager(
            new MonitoredProcessTracker(probe),
            new SamplingModeController(),
            new FakeRepository(),
            new FakeClock(Utc(8, 40)),
            new ControlledExitWaiter());

        await manager.InitializeAsync();
        Assert.True((await manager.AttachAsync(4242)).IsAttached);

        var staleObservation = manager.ObserveActiveProcessAsync().AsTask();
        await probe.ObservationStarted.WaitAsync(TimeSpan.FromSeconds(2));

        await manager.StopAsync();
        Assert.True((await manager.AttachAsync(5252)).IsAttached);

        probe.CompleteOldObservation(new ProcessProbeResult(
            ProcessProbeState.NotFound,
            4242,
            null,
            null,
            null,
            "Old process exited."));

        var observation = await staleObservation;
        Assert.Equal(ProcessObservationState.Exited, observation!.State);

        var active = manager.ActiveSnapshot();
        Assert.NotNull(active);
        Assert.Equal(5252, active!.ProcessId);
        Assert.Equal(newStart, active.ProcessStartTimeUtc);
        Assert.True(active.IsActive);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not reached within two seconds.");
            }

            await Task.Delay(10);
        }
    }

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
        new(2026, 9, 9, hour, minute, 0, TimeSpan.Zero);

    private sealed class ControlledExitWaiter : IProcessExitWaiter
    {
        private readonly TaskCompletionSource<ProcessExitWaitResult> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public void Complete(ProcessExitWaitResult result) => _result.TrySetResult(result);

        public async ValueTask<ProcessExitWaitResult> WaitForExitAsync(
            ProcessInstance target,
            CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            return await _result.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class CountingProbe : IProcessProbe
    {
        private readonly ProcessProbeResult _result;

        public CountingProbe(ProcessProbeResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public ValueTask<ProcessProbeResult> ProbeAsync(
            int processId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult(_result);
        }
    }

    private sealed class RacingProbe : IProcessProbe
    {
        private readonly ProcessProbeResult _oldAttach;
        private readonly ProcessProbeResult _newAttach;
        private readonly TaskCompletionSource<ProcessProbeResult> _oldObservation =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _observationStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public RacingProbe(
            ProcessProbeResult oldAttach,
            ProcessProbeResult newAttach)
        {
            _oldAttach = oldAttach;
            _newAttach = newAttach;
        }

        public Task ObservationStarted => _observationStarted.Task;

        public void CompleteOldObservation(ProcessProbeResult result) =>
            _oldObservation.TrySetResult(result);

        public ValueTask<ProcessProbeResult> ProbeAsync(
            int processId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var call = Interlocked.Increment(ref _callCount);
            return call switch
            {
                1 => ValueTask.FromResult(_oldAttach),
                2 => WaitForOldObservationAsync(cancellationToken),
                3 => ValueTask.FromResult(_newAttach),
                _ => throw new InvalidOperationException($"Unexpected process probe call {call}.")
            };
        }

        private async ValueTask<ProcessProbeResult> WaitForOldObservationAsync(
            CancellationToken cancellationToken)
        {
            _observationStarted.TrySetResult();
            return await _oldObservation.Task.WaitAsync(cancellationToken);
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
            var index = _sessions.FindIndex(x => x.SessionId == session.SessionId);
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
        public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) => TimeSpan.Zero;

        public ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) =>
            new(Task.Delay(delay, cancellationToken));
    }
}
