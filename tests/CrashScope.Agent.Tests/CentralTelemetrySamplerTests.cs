using CrashScope.Agent.Sampling;
using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Tests;

public sealed class CentralTelemetrySamplerTests
{
    [Fact]
    public async Task RunAsyncReadsProviderOnceAndSharesFrameAcrossSinks()
    {
        var startUtc = new DateTimeOffset(
            2026, 9, 8, 4, 0, 0, TimeSpan.Zero);
        var clock = new FakeSamplingClock(startUtc);
        using var cancellation = new CancellationTokenSource();
        clock.OnDelay = cancellation.Cancel;

        var provider = new FakeHardwareProvider(
            CreateSnapshot(),
            () => clock.Advance(TimeSpan.FromMilliseconds(10)));
        var firstSink = new RecordingSink();
        var secondSink = new RecordingSink();
        var diagnostics = new RecordingDiagnosticsSink();

        var sampler = CreateSampler(
            provider,
            new ITelemetryFrameSink[] { firstSink, secondSink },
            new SamplingModeController(),
            clock,
            diagnostics);

        await sampler.RunAsync(cancellation.Token);

        Assert.Equal(1, provider.ReadCount);
        var firstFrame = Assert.Single(firstSink.Frames);
        var secondFrame = Assert.Single(secondSink.Frames);
        Assert.Same(firstFrame, secondFrame);
        Assert.Equal(0, firstFrame.Sequence);
        Assert.Equal(startUtc, firstFrame.TimestampUtc);
        Assert.Equal(0, firstFrame.MonotonicTimestampTicks);

        var cycle = Assert.Single(diagnostics.Cycles);
        Assert.Equal(TimeSpan.FromSeconds(2), cycle.Interval);
        Assert.Equal(TimeSpan.FromMilliseconds(10), cycle.ProviderDuration);
        Assert.False(cycle.IsOverrun);

        Assert.Equal(
            TimeSpan.FromMilliseconds(1990),
            Assert.Single(clock.Delays));
    }

    [Fact]
    public async Task ActiveModeUsesOneSecondInterval()
    {
        var clock = new FakeSamplingClock(DateTimeOffset.UnixEpoch);
        using var cancellation = new CancellationTokenSource();
        clock.OnDelay = cancellation.Cancel;

        var provider = new FakeHardwareProvider(
            CreateSnapshot(),
            () => clock.Advance(TimeSpan.FromMilliseconds(100)));
        var mode = new SamplingModeController();
        mode.SetMode(SamplingMode.Active);
        var diagnostics = new RecordingDiagnosticsSink();

        var sampler = CreateSampler(
            provider,
            Array.Empty<ITelemetryFrameSink>(),
            mode,
            clock,
            diagnostics);

        await sampler.RunAsync(cancellation.Token);

        var cycle = Assert.Single(diagnostics.Cycles);
        Assert.Equal(SamplingMode.Active, cycle.Mode);
        Assert.Equal(TimeSpan.FromSeconds(1), cycle.Interval);
        Assert.Equal(
            TimeSpan.FromMilliseconds(900),
            Assert.Single(clock.Delays));
    }

    [Fact]
    public async Task OverrunIsReportedWithoutCatchUpDelay()
    {
        var clock = new FakeSamplingClock(DateTimeOffset.UnixEpoch);
        using var cancellation = new CancellationTokenSource();

        var provider = new FakeHardwareProvider(
            CreateSnapshot(),
            () => clock.Advance(TimeSpan.FromMilliseconds(2100)));
        var diagnostics = new RecordingDiagnosticsSink
        {
            OnCycleCompleted = _ => cancellation.Cancel()
        };

        var sampler = CreateSampler(
            provider,
            Array.Empty<ITelemetryFrameSink>(),
            new SamplingModeController(),
            clock,
            diagnostics);

        await sampler.RunAsync(cancellation.Token);

        var cycle = Assert.Single(diagnostics.Cycles);
        Assert.True(cycle.IsOverrun);
        Assert.Equal(TimeSpan.FromMilliseconds(2100), cycle.ProviderDuration);
        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task ProviderFailureIsReportedAndBackedOffByFullInterval()
    {
        var clock = new FakeSamplingClock(DateTimeOffset.UnixEpoch);
        using var cancellation = new CancellationTokenSource();
        clock.OnDelay = cancellation.Cancel;

        var provider = new FakeHardwareProvider(
            CreateSnapshot(),
            exception: new InvalidOperationException("fixture failure"));
        var diagnostics = new RecordingDiagnosticsSink();

        var sampler = CreateSampler(
            provider,
            Array.Empty<ITelemetryFrameSink>(),
            new SamplingModeController(),
            clock,
            diagnostics);

        await sampler.RunAsync(cancellation.Token);

        Assert.Equal(1, provider.ReadCount);
        var failure = Assert.Single(diagnostics.Failures);
        Assert.Equal("provider", failure.Component);
        Assert.IsType<InvalidOperationException>(failure.Exception);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(clock.Delays));
        Assert.Empty(diagnostics.Cycles);
    }

    [Fact]
    public async Task SinkFailureDoesNotBlockOtherSinks()
    {
        var clock = new FakeSamplingClock(DateTimeOffset.UnixEpoch);
        using var cancellation = new CancellationTokenSource();
        clock.OnDelay = cancellation.Cancel;

        var provider = new FakeHardwareProvider(CreateSnapshot());
        var goodSink = new RecordingSink();
        var diagnostics = new RecordingDiagnosticsSink();

        var sampler = CreateSampler(
            provider,
            new ITelemetryFrameSink[]
            {
                new ThrowingSink(),
                goodSink
            },
            new SamplingModeController(),
            clock,
            diagnostics);

        await sampler.RunAsync(cancellation.Token);

        Assert.Single(goodSink.Frames);
        var failure = Assert.Single(diagnostics.Failures);
        Assert.StartsWith("sink:", failure.Component);
        Assert.Single(diagnostics.Cycles);
    }

    [Fact]
    public void SamplingIntervalsRejectNonPositiveValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SamplingIntervals(TimeSpan.Zero, TimeSpan.FromSeconds(1)));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SamplingIntervals(TimeSpan.FromSeconds(2), TimeSpan.Zero));
    }

    private static CentralTelemetrySampler CreateSampler(
        IHardwareTelemetryProvider provider,
        IEnumerable<ITelemetryFrameSink> sinks,
        ISamplingModeSource modeSource,
        ISamplingClock clock,
        ISamplingDiagnosticsSink diagnostics) =>
        new(
            provider,
            sinks,
            modeSource,
            SamplingIntervals.Default,
            clock,
            diagnostics);

    private static HardwareTelemetrySnapshot CreateSnapshot() =>
        new(
            new SystemTelemetry(
                MetricReading.Available(8192),
                MetricReading.Available(24576),
                MetricReading.Available(25)),
            new CpuTelemetry(
                MetricReading.Available(10),
                Array.Empty<LogicalProcessorLoad>(),
                MetricReading.Unsupported(),
                MetricReading.Unsupported(),
                MetricReading.Unsupported()),
            Array.Empty<GpuTelemetry>());

    private sealed class FakeHardwareProvider : IHardwareTelemetryProvider
    {
        private readonly HardwareTelemetrySnapshot _snapshot;
        private readonly Action? _onRead;
        private readonly Exception? _exception;

        public FakeHardwareProvider(
            HardwareTelemetrySnapshot snapshot,
            Action? onRead = null,
            Exception? exception = null)
        {
            _snapshot = snapshot;
            _onRead = onRead;
            _exception = exception;
        }

        public int ReadCount { get; private set; }
        public string Name => "FakeHardware";
        public IReadOnlyCollection<TelemetryMetric> Capabilities =>
            Array.Empty<TelemetryMetric>();

        public ValueTask<HardwareTelemetrySnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            _onRead?.Invoke();

            if (_exception is not null)
            {
                throw _exception;
            }

            return ValueTask.FromResult(_snapshot);
        }
    }

    private sealed class FakeSamplingClock : ISamplingClock
    {
        private long _timestamp;
        private DateTimeOffset _utcNow;

        public FakeSamplingClock(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public List<TimeSpan> Delays { get; } = new();
        public Action? OnDelay { get; set; }

        public DateTimeOffset GetUtcNow() => _utcNow;

        public long GetTimestamp() => _timestamp;

        public TimeSpan GetElapsedTime(
            long startTimestamp,
            long endTimestamp) =>
            TimeSpan.FromTicks(endTimestamp - startTimestamp);

        public ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(delay);
            Advance(delay);
            OnDelay?.Invoke();
            return ValueTask.CompletedTask;
        }

        public void Advance(TimeSpan duration)
        {
            _timestamp += duration.Ticks;
            _utcNow += duration;
        }
    }

    private sealed class RecordingSink : ITelemetryFrameSink
    {
        public List<TelemetryFrame> Frames { get; } = new();

        public ValueTask WriteAsync(
            TelemetryFrame frame,
            CancellationToken cancellationToken = default)
        {
            Frames.Add(frame);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingSink : ITelemetryFrameSink
    {
        public ValueTask WriteAsync(
            TelemetryFrame frame,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("fixture sink failure");
    }

    private sealed class RecordingDiagnosticsSink : ISamplingDiagnosticsSink
    {
        public List<SamplingCycle> Cycles { get; } = new();
        public List<SamplingFailure> Failures { get; } = new();
        public Action<SamplingCycle>? OnCycleCompleted { get; init; }

        public ValueTask CycleCompletedAsync(
            SamplingCycle cycle,
            CancellationToken cancellationToken = default)
        {
            Cycles.Add(cycle);
            OnCycleCompleted?.Invoke(cycle);
            return ValueTask.CompletedTask;
        }

        public ValueTask FailureAsync(
            SamplingFailure failure,
            CancellationToken cancellationToken = default)
        {
            Failures.Add(failure);
            return ValueTask.CompletedTask;
        }
    }
}
