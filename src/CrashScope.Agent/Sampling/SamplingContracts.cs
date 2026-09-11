using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Sampling;

internal enum SamplingMode
{
    Background,
    Active
}

internal sealed class SamplingIntervals
{
    public static SamplingIntervals Default { get; } = new(
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(1));

    public TimeSpan Background { get; }
    public TimeSpan Active { get; }

    public SamplingIntervals(TimeSpan background, TimeSpan active)
    {
        if (background <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(background));
        }

        if (active <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(active));
        }

        Background = background;
        Active = active;
    }

    public TimeSpan GetInterval(SamplingMode mode) => mode switch
    {
        SamplingMode.Background => Background,
        SamplingMode.Active => Active,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}

internal interface ISamplingModeSource
{
    SamplingMode Current { get; }
}

internal sealed class SamplingModeController : ISamplingModeSource
{
    private int _current;

    public SamplingMode Current =>
        (SamplingMode)Volatile.Read(ref _current);

    public void SetMode(SamplingMode mode)
    {
        if (mode is not SamplingMode.Background and not SamplingMode.Active)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        Volatile.Write(ref _current, (int)mode);
    }
}

internal interface ISamplingClock
{
    DateTimeOffset GetUtcNow();
    long GetTimestamp();
    TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp);
    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal interface ITelemetryFrameSink
{
    ValueTask WriteAsync(
        TelemetryFrame frame,
        CancellationToken cancellationToken = default);
}

internal interface ISamplingDiagnosticsSink
{
    ValueTask CycleCompletedAsync(
        SamplingCycle cycle,
        CancellationToken cancellationToken = default);

    ValueTask FailureAsync(
        SamplingFailure failure,
        CancellationToken cancellationToken = default);
}

internal sealed record SamplingCycle(
    long Sequence,
    SamplingMode Mode,
    TimeSpan Interval,
    TimeSpan ProviderDuration,
    TimeSpan WorkDuration,
    bool IsOverrun);

internal sealed record SamplingFailure(
    string Component,
    DateTimeOffset TimestampUtc,
    Exception Exception);

internal sealed class NullSamplingDiagnosticsSink : ISamplingDiagnosticsSink
{
    public static NullSamplingDiagnosticsSink Instance { get; } = new();

    private NullSamplingDiagnosticsSink()
    {
    }

    public ValueTask CycleCompletedAsync(
        SamplingCycle cycle,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask FailureAsync(
        SamplingFailure failure,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
