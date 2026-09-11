using System.Diagnostics;
using CrashScope.Agent.Sampling;
using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Buffering;

internal sealed class TelemetryRingBuffer : ITelemetryFrameSink
{
    public static TimeSpan DefaultRetention { get; } = TimeSpan.FromSeconds(120);
    public const int DefaultMaximumFrameCount = 2048;

    private readonly object _sync = new();
    private readonly Queue<TelemetryFrame> _frames = new();
    private readonly TimeSpan _retention;
    private readonly int _maximumFrameCount;
    private long? _latestMonotonicTimestamp;

    public TelemetryRingBuffer(
        TimeSpan? retention = null,
        int maximumFrameCount = DefaultMaximumFrameCount)
    {
        _retention = retention ?? DefaultRetention;

        if (_retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention));
        }

        if (maximumFrameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFrameCount));
        }

        _maximumFrameCount = maximumFrameCount;
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _frames.Count;
            }
        }
    }

    public ValueTask WriteAsync(
        TelemetryFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_latestMonotonicTimestamp.HasValue &&
                frame.MonotonicTimestampTicks < _latestMonotonicTimestamp.Value)
            {
                throw new InvalidOperationException(
                    "Telemetry frames must be written in monotonic timestamp order.");
            }

            _frames.Enqueue(frame);
            _latestMonotonicTimestamp = frame.MonotonicTimestampTicks;
            Trim(frame.MonotonicTimestampTicks);
        }

        return ValueTask.CompletedTask;
    }

    public IReadOnlyList<TelemetryFrame> Snapshot()
    {
        lock (_sync)
        {
            return _frames.ToArray();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _frames.Clear();
            _latestMonotonicTimestamp = null;
        }
    }

    private void Trim(long newestTimestamp)
    {
        while (_frames.Count > 0)
        {
            var oldest = _frames.Peek();
            var age = Stopwatch.GetElapsedTime(
                oldest.MonotonicTimestampTicks,
                newestTimestamp);

            if (age <= _retention && _frames.Count <= _maximumFrameCount)
            {
                break;
            }

            _frames.Dequeue();
        }
    }
}
