using System.Diagnostics;

namespace CrashScope.Agent.Sampling;

internal sealed class SystemSamplingClock : ISamplingClock
{
    public DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;

    public long GetTimestamp() => Stopwatch.GetTimestamp();

    public TimeSpan GetElapsedTime(
        long startTimestamp,
        long endTimestamp) =>
        Stopwatch.GetElapsedTime(startTimestamp, endTimestamp);

    public ValueTask DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return ValueTask.CompletedTask;
        }

        return new ValueTask(Task.Delay(delay, cancellationToken));
    }
}
