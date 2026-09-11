namespace CrashScope.Agent.Processes;

internal enum ProcessProbeState
{
    Running,
    NotFound,
    Unavailable
}

internal sealed record ProcessProbeResult(
    ProcessProbeState State,
    int ProcessId,
    DateTimeOffset? StartTimeUtc,
    string? Name,
    string? ExecutablePath,
    string? Detail);

internal interface IProcessProbe
{
    ValueTask<ProcessProbeResult> ProbeAsync(
        int processId,
        CancellationToken cancellationToken = default);
}

internal readonly record struct ProcessInstanceIdentity
{
    public int ProcessId { get; }
    public DateTimeOffset StartTimeUtc { get; }

    public ProcessInstanceIdentity(int processId, DateTimeOffset startTimeUtc)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        if (startTimeUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Process start time must be UTC.",
                nameof(startTimeUtc));
        }

        ProcessId = processId;
        StartTimeUtc = startTimeUtc;
    }
}

internal sealed record ProcessInstance
{
    public ProcessInstanceIdentity Identity { get; }
    public string Name { get; }
    public string? ExecutablePath { get; }

    public ProcessInstance(
        ProcessInstanceIdentity identity,
        string name,
        string? executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Identity = identity;
        Name = name;
        ExecutablePath = executablePath;
    }
}

internal enum ProcessObservationState
{
    Running,
    Exited,
    PidReused,
    Unavailable
}

internal sealed record ProcessObservation(
    ProcessObservationState State,
    ProcessInstance Target,
    DateTimeOffset ObservedAtUtc,
    ProcessInstance? CurrentProcess,
    string? Detail);

internal sealed record ProcessAttachResult(
    ProcessInstance? Instance,
    string? Detail)
{
    public bool IsAttached => Instance is not null;
}

internal sealed class MonitoredProcessTracker
{
    private readonly IProcessProbe _probe;
    private readonly TimeProvider _timeProvider;

    public MonitoredProcessTracker(
        IProcessProbe probe,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<ProcessAttachResult> AttachAsync(
        int processId,
        CancellationToken cancellationToken = default)
    {
        var result = await _probe
            .ProbeAsync(processId, cancellationToken)
            .ConfigureAwait(false);

        if (result.State != ProcessProbeState.Running ||
            !result.StartTimeUtc.HasValue ||
            string.IsNullOrWhiteSpace(result.Name))
        {
            return new ProcessAttachResult(null, result.Detail ?? result.State.ToString());
        }

        return new ProcessAttachResult(
            CreateInstance(result),
            result.Detail);
    }

    public async ValueTask<ProcessObservation> ObserveAsync(
        ProcessInstance target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var observedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        var result = await _probe
            .ProbeAsync(target.Identity.ProcessId, cancellationToken)
            .ConfigureAwait(false);

        return result.State switch
        {
            ProcessProbeState.NotFound => new ProcessObservation(
                ProcessObservationState.Exited,
                target,
                observedAtUtc,
                null,
                result.Detail),

            ProcessProbeState.Unavailable => new ProcessObservation(
                ProcessObservationState.Unavailable,
                target,
                observedAtUtc,
                null,
                result.Detail),

            ProcessProbeState.Running => ObserveRunning(target, result, observedAtUtc),

            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static ProcessObservation ObserveRunning(
        ProcessInstance target,
        ProcessProbeResult result,
        DateTimeOffset observedAtUtc)
    {
        if (!result.StartTimeUtc.HasValue || string.IsNullOrWhiteSpace(result.Name))
        {
            return new ProcessObservation(
                ProcessObservationState.Unavailable,
                target,
                observedAtUtc,
                null,
                "Running process did not expose enough identity information.");
        }

        var current = CreateInstance(result);
        var state = current.Identity == target.Identity
            ? ProcessObservationState.Running
            : ProcessObservationState.PidReused;

        return new ProcessObservation(
            state,
            target,
            observedAtUtc,
            current,
            result.Detail);
    }

    private static ProcessInstance CreateInstance(ProcessProbeResult result)
    {
        var startTimeUtc = result.StartTimeUtc!.Value.ToUniversalTime();

        return new ProcessInstance(
            new ProcessInstanceIdentity(result.ProcessId, startTimeUtc),
            result.Name!,
            result.ExecutablePath);
    }
}
