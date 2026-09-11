using System.Diagnostics;
using CrashScope.Agent.Buffering;
using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Tests;

public sealed class TelemetryRingBufferTests
{
    [Fact]
    public async Task RetainsOnlyFramesInsideConfiguredDuration()
    {
        var buffer = new TelemetryRingBuffer(TimeSpan.FromSeconds(120));

        await buffer.WriteAsync(Frame(0, Seconds(0)));
        await buffer.WriteAsync(Frame(1, Seconds(60)));
        await buffer.WriteAsync(Frame(2, Seconds(120)));
        await buffer.WriteAsync(Frame(3, Seconds(121)));

        var snapshot = buffer.Snapshot();

        Assert.Equal(new long[] { 1, 2, 3 }, snapshot.Select(x => x.Sequence));
    }

    [Fact]
    public async Task HardFrameCapPreventsUnboundedGrowth()
    {
        var buffer = new TelemetryRingBuffer(
            TimeSpan.FromMinutes(10),
            maximumFrameCount: 3);

        await buffer.WriteAsync(Frame(0, Seconds(0)));
        await buffer.WriteAsync(Frame(1, Seconds(1)));
        await buffer.WriteAsync(Frame(2, Seconds(2)));
        await buffer.WriteAsync(Frame(3, Seconds(3)));

        var snapshot = buffer.Snapshot();

        Assert.Equal(3, buffer.Count);
        Assert.Equal(new long[] { 1, 2, 3 }, snapshot.Select(x => x.Sequence));
    }

    [Fact]
    public async Task SnapshotIsOrderedAndDefensive()
    {
        var buffer = new TelemetryRingBuffer();

        await buffer.WriteAsync(Frame(10, Seconds(10)));
        await buffer.WriteAsync(Frame(11, Seconds(11)));

        var first = buffer.Snapshot();
        var mutable = Assert.IsType<TelemetryFrame[]>(first);
        mutable[0] = Frame(999, Seconds(12));

        var second = buffer.Snapshot();

        Assert.Equal(new long[] { 10, 11 }, second.Select(x => x.Sequence));
    }

    [Fact]
    public async Task RejectsMonotonicTimestampRegression()
    {
        var buffer = new TelemetryRingBuffer();

        await buffer.WriteAsync(Frame(0, Seconds(10)));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await buffer.WriteAsync(Frame(1, Seconds(9))));
    }

    [Fact]
    public async Task CancellationPreventsWrite()
    {
        var buffer = new TelemetryRingBuffer();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await buffer.WriteAsync(Frame(0, Seconds(0)), cts.Token));

        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public async Task ClearRemovesFramesAndAllowsNewTimeline()
    {
        var buffer = new TelemetryRingBuffer();

        await buffer.WriteAsync(Frame(0, Seconds(100)));
        buffer.Clear();
        await buffer.WriteAsync(Frame(0, Seconds(1)));

        var snapshot = buffer.Snapshot();

        Assert.Single(snapshot);
        Assert.Equal(Seconds(1), snapshot[0].MonotonicTimestampTicks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidMaximumFrameCount(int maximumFrameCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TelemetryRingBuffer(
                TimeSpan.FromSeconds(120),
                maximumFrameCount));
    }

    [Fact]
    public void RejectsInvalidRetention()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TelemetryRingBuffer(TimeSpan.Zero));
    }

    private static long Seconds(double seconds) =>
        checked((long)(seconds * Stopwatch.Frequency));

    private static TelemetryFrame Frame(long sequence, long monotonicTimestamp)
    {
        var unsupported = MetricReading.Unsupported("fixture");

        var system = new SystemTelemetry(
            unsupported,
            unsupported,
            unsupported);

        var cpu = new CpuTelemetry(
            unsupported,
            Array.Empty<LogicalProcessorLoad>(),
            unsupported,
            unsupported,
            unsupported);

        return new TelemetryFrame(
            sequence,
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            monotonicTimestamp,
            system,
            cpu,
            Array.Empty<GpuTelemetry>());
    }
}
