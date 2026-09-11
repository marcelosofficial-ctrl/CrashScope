using System.Diagnostics;
using CrashScope.Agent.Buffering;
using CrashScope.Agent.Incidents;
using CrashScope.Agent.Sampling;
using CrashScope.Core.Diagnostics;
using CrashScope.Core.Incidents;
using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Tests;

public sealed class RealtimeIncidentMonitorTests
{
    [Fact]
    public async Task RealtimeSignalWakesMonitorWithoutHistoricalReconciliation()
    {
        var now = Utc(12, 0);
        var events = new ControlledRealtimeEventSource();
        var clock = new ControlledSamplingClock(now);
        var monitor = CreateMonitor(events, clock);

        using var cancellation = new CancellationTokenSource();
        var run = monitor.RunAsync(cancellation.Token);

        await WaitUntilAsync(() => events.ReadCount >= 1 && events.WaitCount >= 1);
        Assert.Equal(0, events.ReconcileCount);

        events.Signal();
        await WaitUntilAsync(() => events.ReadCount >= 2);

        Assert.Equal(0, events.ReconcileCount);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await run);
    }

    [Fact]
    public async Task QuietRealtimeSourceUsesSparseReconciliationDeadline()
    {
        var now = Utc(12, 0);
        var events = new ControlledRealtimeEventSource();
        var clock = new ControlledSamplingClock(now);
        var monitor = CreateMonitor(events, clock);

        using var cancellation = new CancellationTokenSource();
        var run = monitor.RunAsync(cancellation.Token);

        await WaitUntilAsync(() => events.WaitCount >= 1 && clock.DelayCount >= 1);
        clock.CompleteNextDelay();

        await WaitUntilAsync(() => events.ReconcileCount >= 1 && events.ReadCount >= 2);
        Assert.Equal(TimeSpan.FromSeconds(60), clock.LastRequestedDelay);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await run);
    }

    private static LiveIncidentMonitor CreateMonitor(
        IRealtimeDiagnosticEventSource events,
        ISamplingClock clock)
    {
        var buffer = new TelemetryRingBuffer();
        var coordinator = new IncidentCoordinator(buffer, postTriggerWindow: TimeSpan.Zero);
        coordinator.WriteAsync(CreateFrame(Utc(12, 0))).AsTask().GetAwaiter().GetResult();

        return new LiveIncidentMonitor(
            events,
            new EmptyArtifactSource(),
            new DiagnosticEvidenceDeduplicator(),
            coordinator,
            new IncidentReportBuilder(),
            new InMemoryIncidentReportSink(),
            clock,
            scanInterval: TimeSpan.FromSeconds(5),
            reconciliationInterval: TimeSpan.FromSeconds(60));
    }

    private static TelemetryFrame CreateFrame(DateTimeOffset timestampUtc)
    {
        var available = MetricReading.Available(10);
        var unsupported = MetricReading.Unsupported();
        return new TelemetryFrame(
            1,
            timestampUtc,
            Stopwatch.GetTimestamp(),
            new SystemTelemetry(available, available, available),
            new CpuTelemetry(
                available,
                Array.Empty<LogicalProcessorLoad>(),
                unsupported,
                unsupported,
                unsupported),
            Array.Empty<GpuTelemetry>());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not reached within three seconds.");
            }

            await Task.Delay(10);
        }
    }

    private static DateTimeOffset Utc(int hour, int minute) =>
        new(2026, 9, 9, hour, minute, 0, TimeSpan.Zero);

    private sealed class ControlledRealtimeEventSource : IRealtimeDiagnosticEventSource
    {
        private readonly object _sync = new();
        private TaskCompletionSource _signal = NewSignal();
        private int _readCount;
        private int _waitCount;
        private int _reconcileCount;

        public string Name => "ControlledRealtime";
        public bool RealtimeAvailable => true;
        public int ReadCount => Volatile.Read(ref _readCount);
        public int WaitCount => Volatile.Read(ref _waitCount);
        public int ReconcileCount => Volatile.Read(ref _reconcileCount);

        public ValueTask<IReadOnlyList<DiagnosticEvent>> ReadSinceAsync(
            DateTimeOffset sinceUtc,
            int maximumEvents = 256,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _readCount);
            return ValueTask.FromResult<IReadOnlyList<DiagnosticEvent>>(Array.Empty<DiagnosticEvent>());
        }

        public async ValueTask WaitForEventAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _waitCount);
            Task wait;
            lock (_sync)
            {
                wait = _signal.Task;
            }

            await wait.WaitAsync(cancellationToken);
        }

        public ValueTask ReconcileAsync(
            DateTimeOffset sinceUtc,
            int maximumEvents = 512,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _reconcileCount);
            return ValueTask.CompletedTask;
        }

        public void Signal()
        {
            TaskCompletionSource current;
            lock (_sync)
            {
                current = _signal;
                _signal = NewSignal();
            }

            current.TrySetResult();
        }

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ControlledSamplingClock : ISamplingClock
    {
        private readonly object _sync = new();
        private TaskCompletionSource _delay = NewDelay();
        private int _delayCount;

        public ControlledSamplingClock(DateTimeOffset now)
        {
            Now = now;
        }

        public DateTimeOffset Now { get; private set; }
        public int DelayCount => Volatile.Read(ref _delayCount);
        public TimeSpan LastRequestedDelay { get; private set; }

        public DateTimeOffset GetUtcNow() => Now;
        public long GetTimestamp() => Stopwatch.GetTimestamp();
        public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) =>
            Stopwatch.GetElapsedTime(startTimestamp, endTimestamp);

        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _delayCount);
            LastRequestedDelay = delay;
            Task wait;
            lock (_sync)
            {
                wait = _delay.Task;
            }

            return new ValueTask(wait.WaitAsync(cancellationToken));
        }

        public void CompleteNextDelay()
        {
            TaskCompletionSource current;
            lock (_sync)
            {
                current = _delay;
                _delay = NewDelay();
            }

            Now += LastRequestedDelay;
            current.TrySetResult();
        }

        private static TaskCompletionSource NewDelay() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class EmptyArtifactSource : IDiagnosticArtifactSource
    {
        public string Name => "EmptyArtifacts";

        public ValueTask<IReadOnlyList<DiagnosticArtifact>> ReadSinceAsync(
            DateTimeOffset sinceUtc,
            int maximumArtifacts = 256,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<DiagnosticArtifact>>(Array.Empty<DiagnosticArtifact>());
    }
}
