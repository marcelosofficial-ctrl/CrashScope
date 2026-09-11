namespace CrashScope.Core.Telemetry;

public enum MetricState
{
    Available,
    Unsupported,
    Invalid,
    Error
}

public enum TelemetryMetric
{
    SystemPhysicalMemoryUsedMiB,
    SystemPhysicalMemoryAvailableMiB,
    SystemPhysicalMemoryLoadPercent,
    CpuTotalUtilizationPercent,
    CpuLogicalProcessorUtilizationPercent,
    CpuTemperatureCelsius,
    CpuPackagePowerWatts,
    CpuAverageClockMHz,
    GpuCoreUtilizationPercent,
    GpuMemoryUtilizationPercent,
    GpuCoreTemperatureCelsius,
    GpuHotspotTemperatureCelsius,
    GpuMemoryTemperatureCelsius,
    GpuPackagePowerWatts,
    GpuCoreClockMHz,
    GpuMemoryClockMHz,
    GpuDedicatedMemoryUsedMiB,
    GpuDedicatedMemoryTotalMiB,
    GpuFanSpeedRpm,
    GpuFanControlPercent
}

public readonly record struct MetricReading
{
    public double? Value { get; }
    public double? RawValue { get; }
    public MetricState State { get; }
    public string? Detail { get; }

    public bool IsAvailable =>
        State == MetricState.Available && Value.HasValue;

    private MetricReading(
        double? value,
        double? rawValue,
        MetricState state,
        string? detail)
    {
        Value = value;
        RawValue = rawValue;
        State = state;
        Detail = detail;
    }

    public static MetricReading Available(double value, double? rawValue = null)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Available telemetry must be finite.");
        }

        return new(value, rawValue ?? value, MetricState.Available, null);
    }

    public static MetricReading Unsupported(string? detail = null) =>
        new(null, null, MetricState.Unsupported, detail);

    public static MetricReading Invalid(double? rawValue, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        return new(null, rawValue, MetricState.Invalid, detail);
    }

    public static MetricReading Error(string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        return new(null, null, MetricState.Error, detail);
    }
}

public interface ITelemetryProvider
{
    string Name { get; }
    IReadOnlyCollection<TelemetryMetric> Capabilities { get; }
}

public interface IHardwareTelemetryProvider : ITelemetryProvider
{
    ValueTask<HardwareTelemetrySnapshot> ReadAsync(
        CancellationToken cancellationToken = default);
}

public interface ICpuTelemetryProvider : ITelemetryProvider
{
    ValueTask<CpuTelemetry> ReadAsync(
        CancellationToken cancellationToken = default);
}

public interface IGpuTelemetryProvider : ITelemetryProvider
{
    ValueTask<IReadOnlyList<GpuTelemetry>> ReadAsync(
        CancellationToken cancellationToken = default);
}

public interface ISystemTelemetryProvider : ITelemetryProvider
{
    ValueTask<SystemTelemetry> ReadAsync(
        CancellationToken cancellationToken = default);
}

public readonly record struct LogicalProcessorLoad(
    int LogicalProcessorIndex,
    MetricReading UtilizationPercent);

public sealed record CpuTelemetry
{
    public MetricReading TotalUtilizationPercent { get; }
    public IReadOnlyList<LogicalProcessorLoad> LogicalProcessorUtilization { get; }
    public MetricReading TemperatureCelsius { get; }
    public MetricReading PackagePowerWatts { get; }
    public MetricReading AverageClockMHz { get; }

    public CpuTelemetry(
        MetricReading totalUtilizationPercent,
        IReadOnlyList<LogicalProcessorLoad> logicalProcessorUtilization,
        MetricReading temperatureCelsius,
        MetricReading packagePowerWatts,
        MetricReading averageClockMHz)
    {
        ArgumentNullException.ThrowIfNull(logicalProcessorUtilization);

        var logicalProcessorCopy = logicalProcessorUtilization.ToArray();

        if (logicalProcessorCopy.Any(x => x.LogicalProcessorIndex < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(logicalProcessorUtilization),
                "Logical processor indexes cannot be negative.");
        }

        if (logicalProcessorCopy
            .Select(x => x.LogicalProcessorIndex)
            .Distinct()
            .Count() != logicalProcessorCopy.Length)
        {
            throw new ArgumentException(
                "Logical processor indexes must be unique.",
                nameof(logicalProcessorUtilization));
        }

        TotalUtilizationPercent = totalUtilizationPercent;
        LogicalProcessorUtilization = logicalProcessorCopy;
        TemperatureCelsius = temperatureCelsius;
        PackagePowerWatts = packagePowerWatts;
        AverageClockMHz = averageClockMHz;
    }
}

public sealed record GpuTelemetry
{
    public string DeviceId { get; }
    public string Name { get; }
    public MetricReading CoreUtilizationPercent { get; }
    public MetricReading MemoryUtilizationPercent { get; }
    public MetricReading CoreTemperatureCelsius { get; }
    public MetricReading HotspotTemperatureCelsius { get; }
    public MetricReading MemoryTemperatureCelsius { get; }
    public MetricReading PackagePowerWatts { get; }
    public MetricReading CoreClockMHz { get; }
    public MetricReading MemoryClockMHz { get; }
    public MetricReading DedicatedMemoryUsedMiB { get; }
    public MetricReading DedicatedMemoryTotalMiB { get; }
    public MetricReading FanSpeedRpm { get; }
    public MetricReading FanControlPercent { get; }

    public GpuTelemetry(
        string deviceId,
        string name,
        MetricReading coreUtilizationPercent,
        MetricReading memoryUtilizationPercent,
        MetricReading coreTemperatureCelsius,
        MetricReading hotspotTemperatureCelsius,
        MetricReading memoryTemperatureCelsius,
        MetricReading packagePowerWatts,
        MetricReading coreClockMHz,
        MetricReading memoryClockMHz,
        MetricReading dedicatedMemoryUsedMiB,
        MetricReading dedicatedMemoryTotalMiB,
        MetricReading fanSpeedRpm,
        MetricReading fanControlPercent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        DeviceId = deviceId;
        Name = name;
        CoreUtilizationPercent = coreUtilizationPercent;
        MemoryUtilizationPercent = memoryUtilizationPercent;
        CoreTemperatureCelsius = coreTemperatureCelsius;
        HotspotTemperatureCelsius = hotspotTemperatureCelsius;
        MemoryTemperatureCelsius = memoryTemperatureCelsius;
        PackagePowerWatts = packagePowerWatts;
        CoreClockMHz = coreClockMHz;
        MemoryClockMHz = memoryClockMHz;
        DedicatedMemoryUsedMiB = dedicatedMemoryUsedMiB;
        DedicatedMemoryTotalMiB = dedicatedMemoryTotalMiB;
        FanSpeedRpm = fanSpeedRpm;
        FanControlPercent = fanControlPercent;
    }
}

public sealed record SystemTelemetry(
    MetricReading PhysicalMemoryUsedMiB,
    MetricReading PhysicalMemoryAvailableMiB,
    MetricReading PhysicalMemoryLoadPercent);

public sealed record HardwareTelemetrySnapshot
{
    public SystemTelemetry System { get; }
    public CpuTelemetry Cpu { get; }
    public IReadOnlyList<GpuTelemetry> Gpus { get; }

    public HardwareTelemetrySnapshot(
        SystemTelemetry system,
        CpuTelemetry cpu,
        IReadOnlyList<GpuTelemetry> gpus)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(cpu);
        ArgumentNullException.ThrowIfNull(gpus);

        System = system;
        Cpu = cpu;
        Gpus = gpus.ToArray();
    }
}

public sealed record TelemetryFrame
{
    public long Sequence { get; }
    public DateTimeOffset TimestampUtc { get; }
    public long MonotonicTimestampTicks { get; }
    public SystemTelemetry System { get; }
    public CpuTelemetry Cpu { get; }
    public IReadOnlyList<GpuTelemetry> Gpus { get; }

    public TelemetryFrame(
        long sequence,
        DateTimeOffset timestampUtc,
        long monotonicTimestamp,
        SystemTelemetry system,
        CpuTelemetry cpu,
        IReadOnlyList<GpuTelemetry> gpus)
    {
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (monotonicTimestamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monotonicTimestamp));
        }

        if (timestampUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Telemetry wall-clock timestamps must be UTC.",
                nameof(timestampUtc));
        }

        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(cpu);
        ArgumentNullException.ThrowIfNull(gpus);

        Sequence = sequence;
        TimestampUtc = timestampUtc;
        MonotonicTimestampTicks = monotonicTimestamp;
        System = system;
        Cpu = cpu;
        Gpus = gpus.ToArray();
    }
}
