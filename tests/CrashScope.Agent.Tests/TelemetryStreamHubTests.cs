using CrashScope.Agent.Streaming;
using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Tests;

public sealed class TelemetryStreamHubTests
{
    [Fact]
    public async Task SlowSubscriberReceivesLatestFrameWithoutBackpressuringWriter()
    {
        var hub = new TelemetryStreamHub();
        using var subscription = hub.Subscribe();

        await hub.WriteAsync(Frame(1));
        await hub.WriteAsync(Frame(2));
        await hub.WriteAsync(Frame(3));

        var latest = await subscription.Reader.ReadAsync();
        var diagnostics = hub.Diagnostics;

        Assert.Equal(3, latest.Sequence);
        Assert.Equal(1, hub.SubscriberCount);
        Assert.Equal(3, diagnostics.FramesPublished);
        Assert.Equal(3, diagnostics.DeliveryAttempts);
        Assert.Equal(2, diagnostics.DroppedStaleFrames);
        Assert.Equal(0, diagnostics.DeliveryMisses);
    }

    [Fact]
    public async Task NoSubscriberStillCountsPublishedFramesWithoutDeliveryWork()
    {
        var hub = new TelemetryStreamHub();

        await hub.WriteAsync(Frame(1));
        await hub.WriteAsync(Frame(2));

        var diagnostics = hub.Diagnostics;

        Assert.Equal(2, diagnostics.FramesPublished);
        Assert.Equal(0, diagnostics.DeliveryAttempts);
        Assert.Equal(0, diagnostics.DroppedStaleFrames);
        Assert.Equal(0, diagnostics.DeliveryMisses);
        Assert.Equal(0, diagnostics.SubscriberCount);
    }

    [Fact]
    public void DisposingSubscriptionRemovesSubscriber()
    {
        var hub = new TelemetryStreamHub();
        var subscription = hub.Subscribe();

        Assert.Equal(1, hub.SubscriberCount);

        subscription.Dispose();

        Assert.Equal(0, hub.SubscriberCount);
        Assert.Equal(0, hub.Diagnostics.SubscriberCount);
    }

    private static TelemetryFrame Frame(long sequence)
    {
        var unavailable = MetricReading.Unsupported();
        return new TelemetryFrame(
            sequence,
            new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero).AddSeconds(sequence),
            sequence,
            new SystemTelemetry(unavailable, unavailable, unavailable),
            new CpuTelemetry(
                unavailable,
                Array.Empty<LogicalProcessorLoad>(),
                unavailable,
                unavailable,
                unavailable),
            Array.Empty<GpuTelemetry>());
    }
}
