namespace CrashScope.Core.Sessions;

public enum SessionEndReason
{
    ProcessExited,
    PidReused,
    UserStopped,
    InterruptedOnRestart
}

public sealed record EnvironmentGpuSnapshot(
    string Name,
    string? DriverVersion);

public sealed record SessionEnvironmentSnapshot
{
    public DateTimeOffset CapturedAtUtc { get; }
    public string OperatingSystem { get; }
    public string OsArchitecture { get; }
    public string ProcessArchitecture { get; }
    public string RuntimeDescription { get; }
    public string CrashScopeVersion { get; }
    public string? CpuName { get; }
    public int LogicalProcessorCount { get; }
    public long? PhysicalMemoryMiB { get; }
    public IReadOnlyList<EnvironmentGpuSnapshot> Gpus { get; }

    public SessionEnvironmentSnapshot(
        DateTimeOffset capturedAtUtc,
        string operatingSystem,
        string osArchitecture,
        string processArchitecture,
        string runtimeDescription,
        string crashScopeVersion,
        string? cpuName,
        int logicalProcessorCount,
        long? physicalMemoryMiB,
        IReadOnlyList<EnvironmentGpuSnapshot>? gpus = null)
    {
        if (capturedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Environment snapshot time must be UTC.", nameof(capturedAtUtc));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(operatingSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(osArchitecture);
        ArgumentException.ThrowIfNullOrWhiteSpace(processArchitecture);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDescription);
        ArgumentException.ThrowIfNullOrWhiteSpace(crashScopeVersion);

        if (logicalProcessorCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalProcessorCount));
        }

        if (physicalMemoryMiB is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(physicalMemoryMiB));
        }

        CapturedAtUtc = capturedAtUtc;
        OperatingSystem = operatingSystem;
        OsArchitecture = osArchitecture;
        ProcessArchitecture = processArchitecture;
        RuntimeDescription = runtimeDescription;
        CrashScopeVersion = crashScopeVersion;
        CpuName = string.IsNullOrWhiteSpace(cpuName) ? null : cpuName.Trim();
        LogicalProcessorCount = logicalProcessorCount;
        PhysicalMemoryMiB = physicalMemoryMiB;
        Gpus = (gpus ?? Array.Empty<EnvironmentGpuSnapshot>()).ToArray();
    }
}

public interface ISessionEnvironmentSnapshotProvider
{
    ValueTask<SessionEnvironmentSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default);
}

public sealed record WorkloadCandidate
{
    public int ProcessId { get; }
    public DateTimeOffset ProcessStartTimeUtc { get; }
    public string ProcessName { get; }
    public string? ExecutablePath { get; }
    public string? WindowTitle { get; }
    public double WorkingSetMiB { get; }
    public WorkloadKind Kind { get; }
    public WorkloadRecommendation Recommendation { get; }
    public int RecommendationScore { get; }
    public IReadOnlyList<string> RecommendationReasons { get; }

    public WorkloadCandidate(
        int processId,
        DateTimeOffset processStartTimeUtc,
        string processName,
        string? executablePath,
        string? windowTitle,
        double workingSetMiB,
        WorkloadKind kind,
        WorkloadRecommendation recommendation,
        int recommendationScore,
        IReadOnlyList<string>? recommendationReasons = null)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        if (processStartTimeUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Workload candidate process start time must be UTC.", nameof(processStartTimeUtc));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(processName);

        if (!double.IsFinite(workingSetMiB) || workingSetMiB < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workingSetMiB));
        }

        if (recommendationScore is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(recommendationScore));
        }

        ProcessId = processId;
        ProcessStartTimeUtc = processStartTimeUtc;
        ProcessName = processName;
        ExecutablePath = string.IsNullOrWhiteSpace(executablePath) ? null : executablePath;
        WindowTitle = string.IsNullOrWhiteSpace(windowTitle) ? null : windowTitle;
        WorkingSetMiB = workingSetMiB;
        Kind = kind;
        Recommendation = recommendation;
        RecommendationScore = recommendationScore;
        RecommendationReasons = (recommendationReasons ?? Array.Empty<string>()).ToArray();
    }
}

public sealed record SessionTelemetrySummary(
    long FrameCount,
    double? PeakCpuUtilizationPercent,
    double? PeakGpuUtilizationPercent,
    double? PeakGpuHotspotCelsius,
    double? PeakGpuMemoryUsedMiB,
    double? PeakSystemMemoryLoadPercent)
{
    public static SessionTelemetrySummary Empty { get; } =
        new(0, null, null, null, null, null);
}

public sealed record WorkloadSession
{
    public Guid SessionId { get; }
    public int ProcessId { get; }
    public DateTimeOffset ProcessStartTimeUtc { get; }
    public string ProcessName { get; }
    public string? ExecutablePath { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset? EndedAtUtc { get; }
    public SessionEndReason? EndReason { get; }
    public SessionTelemetrySummary Telemetry { get; }
    public IReadOnlyList<Guid> IncidentIds { get; }
    public SessionEnvironmentSnapshot? Environment { get; }

    public bool IsActive => !EndedAtUtc.HasValue;

    public WorkloadSession(
        Guid sessionId,
        int processId,
        DateTimeOffset processStartTimeUtc,
        string processName,
        string? executablePath,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        SessionEndReason? endReason,
        SessionTelemetrySummary telemetry,
        IReadOnlyList<Guid>? incidentIds = null,
        SessionEnvironmentSnapshot? environment = null)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
        }

        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        if (processStartTimeUtc.Offset != TimeSpan.Zero ||
            startedAtUtc.Offset != TimeSpan.Zero ||
            (endedAtUtc.HasValue && endedAtUtc.Value.Offset != TimeSpan.Zero))
        {
            throw new ArgumentException("Session timestamps must be UTC.");
        }

        if (endedAtUtc.HasValue && endedAtUtc.Value < startedAtUtc)
        {
            throw new ArgumentException("Session end cannot precede session start.", nameof(endedAtUtc));
        }

        if (endedAtUtc.HasValue != endReason.HasValue)
        {
            throw new ArgumentException("Ended sessions require an end reason and active sessions cannot have one.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        ArgumentNullException.ThrowIfNull(telemetry);

        SessionId = sessionId;
        ProcessId = processId;
        ProcessStartTimeUtc = processStartTimeUtc;
        ProcessName = processName;
        ExecutablePath = string.IsNullOrWhiteSpace(executablePath) ? null : executablePath;
        StartedAtUtc = startedAtUtc;
        EndedAtUtc = endedAtUtc;
        EndReason = endReason;
        Telemetry = telemetry;
        IncidentIds = (incidentIds ?? Array.Empty<Guid>()).Distinct().ToArray();
        Environment = environment;
    }
}

public interface IWorkloadSessionRepository
{
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        WorkloadSession session,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<WorkloadSession>> LoadAllAsync(
        CancellationToken cancellationToken = default);
}
