using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Sampling;

internal sealed class CentralTelemetrySampler
{
    private readonly IHardwareTelemetryProvider _provider;
    private readonly IReadOnlyList<ITelemetryFrameSink> _sinks;
    private readonly ISamplingModeSource _modeSource;
    private readonly SamplingIntervals _intervals;
    private readonly ISamplingClock _clock;
    private readonly ISamplingDiagnosticsSink _diagnostics;

    private long _nextSequence;
    private int _running;

    public CentralTelemetrySampler(
        IHardwareTelemetryProvider provider,
        IEnumerable<ITelemetryFrameSink> sinks,
        ISamplingModeSource modeSource,
        SamplingIntervals intervals,
        ISamplingClock clock,
        ISamplingDiagnosticsSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(sinks);
        ArgumentNullException.ThrowIfNull(modeSource);
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(clock);

        _provider = provider;
        _sinks = sinks.ToArray();
        _modeSource = modeSource;
        _intervals = intervals;
        _clock = clock;
        _diagnostics = diagnostics ?? NullSamplingDiagnosticsSink.Instance;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0)
        {
            throw new InvalidOperationException(
                "The central telemetry sampler is already running.");
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var mode = _modeSource.Current;
                var interval = _intervals.GetInterval(mode);
                var cycleStart = _clock.GetTimestamp();
                var timestampUtc = _clock.GetUtcNow().ToUniversalTime();

                HardwareTelemetrySnapshot snapshot;
                long providerEnd;

                try
                {
                    snapshot = await _provider
                        .ReadAsync(cancellationToken)
                        .ConfigureAwait(false);

                    providerEnd = _clock.GetTimestamp();
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await ReportFailureAsync(
                            "provider",
                            ex,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (!await DelayAsync(interval, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        break;
                    }

                    continue;
                }

                var sequence = _nextSequence++;
                var frame = new TelemetryFrame(
                    sequence,
                    timestampUtc,
                    cycleStart,
                    snapshot.System,
                    snapshot.Cpu,
                    snapshot.Gpus);

                var cancelled = false;

                foreach (var sink in _sinks)
                {
                    try
                    {
                        await sink
                            .WriteAsync(frame, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        await ReportFailureAsync(
                                $"sink:{sink.GetType().Name}",
                                ex,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                if (cancelled || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var workEnd = _clock.GetTimestamp();
                var providerDuration =
                    _clock.GetElapsedTime(cycleStart, providerEnd);
                var workDuration =
                    _clock.GetElapsedTime(cycleStart, workEnd);

                var cycle = new SamplingCycle(
                    sequence,
                    mode,
                    interval,
                    providerDuration,
                    workDuration,
                    workDuration > interval);

                await ReportCycleAsync(cycle, cancellationToken)
                    .ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var currentTimestamp = _clock.GetTimestamp();
                var elapsed =
                    _clock.GetElapsedTime(cycleStart, currentTimestamp);
                var remaining = interval - elapsed;

                if (remaining > TimeSpan.Zero &&
                    !await DelayAsync(remaining, cancellationToken)
                        .ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private async ValueTask ReportCycleAsync(
        SamplingCycle cycle,
        CancellationToken cancellationToken)
    {
        try
        {
            await _diagnostics
                .CycleCompletedAsync(cycle, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // Diagnostics must never stop telemetry sampling.
        }
    }

    private async ValueTask ReportFailureAsync(
        string component,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var failure = new SamplingFailure(
            component,
            _clock.GetUtcNow().ToUniversalTime(),
            exception);

        try
        {
            await _diagnostics
                .FailureAsync(failure, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // Diagnostics must never stop telemetry sampling.
        }
    }

    private async ValueTask<bool> DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await _clock
                .DelayAsync(delay, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
