namespace CrashScope.Infrastructure.Telemetry.LibreHardwareMonitor;

internal enum LhmHardwareKind
{
    Cpu,
    Memory,
    GpuAmd,
    GpuNvidia,
    GpuIntel,
    Other
}

internal enum LhmSensorKind
{
    Load,
    Temperature,
    Power,
    Clock,
    Data,
    SmallData,
    Fan,
    Control,
    Other
}

internal sealed record LhmSensorSnapshot(
    string Id,
    string Name,
    LhmSensorKind Kind,
    double? Value);

internal sealed record LhmHardwareSnapshot(
    string Id,
    string Name,
    LhmHardwareKind Kind,
    IReadOnlyList<LhmSensorSnapshot> Sensors);
