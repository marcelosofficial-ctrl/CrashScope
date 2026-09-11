using System.Collections.Concurrent;
using System.Threading.Channels;
using CrashScope.Agent.Sampling;
using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Streaming;

internal sealed record TelemetryStreamDiagnostics(
    long FramesPublished,
    long DeliveryAttempts,
    long DroppedStaleFrames,
    long DeliveryMisses,
    int SubscriberCount);

internal sealed class TelemetryStreamHub : ITelemetryFrameSink
{
    private readonly ConcurrentDictionary<Guid, SubscriberState> _subscribers = new();
    private long _framesPublished;
    private long _deliveryAttempts;
    private long _droppedStaleFrames;
    private long _deliveryMisses;

    public int SubscriberCount => _subscribers.Count;

    public TelemetryStreamDiagnostics Diagnostics => new(
        Interlocked.Read(ref _framesPublished),
        Interlocked.Read(ref _deliveryAttempts),
        Interlocked.Read(ref _droppedStaleFrames),
        Interlocked.Read(ref _deliveryMisses),
        SubscriberCount);

    public TelemetryStreamSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<TelemetryFrame>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        var subscriber = new SubscriberState(channel);

        if (!_subscribers.TryAdd(id, subscriber))
        {
            throw new InvalidOperationException("Could not register telemetry stream subscriber.");
        }

        return new TelemetryStreamSubscription(
            channel.Reader,
            () => Remove(id));
    }

    public ValueTask WriteAsync(
        TelemetryFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();

        Interlocked.Increment(ref _framesPublished);

        foreach (var subscriber in _subscribers.Values)
        {
            Interlocked.Increment(ref _deliveryAttempts);

            if (subscriber.Channel.Writer.TryWrite(frame))
            {
                continue;
            }

            if (subscriber.Channel.Reader.TryRead(out _))
            {
                Interlocked.Increment(ref _droppedStaleFrames);
            }

            if (!subscriber.Channel.Writer.TryWrite(frame))
            {
                Interlocked.Increment(ref _deliveryMisses);
            }
        }

        return ValueTask.CompletedTask;
    }

    private void Remove(Guid id)
    {
        if (_subscribers.TryRemove(id, out var subscriber))
        {
            subscriber.Channel.Writer.TryComplete();
        }
    }

    private sealed record SubscriberState(Channel<TelemetryFrame> Channel);
}

internal sealed class TelemetryStreamSubscription : IDisposable
{
    private Action? _dispose;

    public ChannelReader<TelemetryFrame> Reader { get; }

    public TelemetryStreamSubscription(
        ChannelReader<TelemetryFrame> reader,
        Action dispose)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(dispose);
        Reader = reader;
        _dispose = dispose;
    }

    public void Dispose() =>
        Interlocked.Exchange(ref _dispose, null)?.Invoke();
}
