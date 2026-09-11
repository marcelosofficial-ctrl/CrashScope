using CrashScope.Core.Telemetry;

namespace CrashScope.Infrastructure.Telemetry.LibreHardwareMonitor;

internal static class LhmTelemetryMapper
{
    public static HardwareTelemetrySnapshot Map(
        IReadOnlyList<LhmHardwareSnapshot> hardware)
    {
        ArgumentNullException.ThrowIfNull(hardware);

        return new HardwareTelemetrySnapshot(
            MapSystem(hardware),
            MapCpu(hardware),
            hardware
                .Where(x => x.Kind is LhmHardwareKind.GpuAmd or LhmHardwareKind.GpuNvidia or LhmHardwareKind.GpuIntel)
                .Select(MapGpu)
                .ToArray());
    }

    private static SystemTelemetry MapSystem(IReadOnlyList<LhmHardwareSnapshot> hardware)
    {
        var memory = hardware.FirstOrDefault(x => x.Kind == LhmHardwareKind.Memory);
        if (memory is null)
        {
            return new SystemTelemetry(
                Unsupported("Memory hardware was not exposed by LibreHardwareMonitor."),
                Unsupported("Memory hardware was not exposed by LibreHardwareMonitor."),
                Unsupported("Memory hardware was not exposed by LibreHardwareMonitor."));
        }

        return new SystemTelemetry(
            MapDataGiBToMiB(memory, "Memory Used"),
            MapDataGiBToMiB(memory, "Memory Available"),
            MapPercent(memory, LhmSensorKind.Load, "Memory"));
    }

    private static CpuTelemetry MapCpu(IReadOnlyList<LhmHardwareSnapshot> hardware)
    {
        var cpu = hardware.FirstOrDefault(x => x.Kind == LhmHardwareKind.Cpu);
        if (cpu is null)
        {
            return new CpuTelemetry(
                Unsupported("CPU hardware was not exposed by LibreHardwareMonitor."),
                Array.Empty<LogicalProcessorLoad>(),
                Unsupported("CPU hardware was not exposed by LibreHardwareMonitor."),
                Unsupported("CPU hardware was not exposed by LibreHardwareMonitor."),
                Unsupported("CPU hardware was not exposed by LibreHardwareMonitor."));
        }

        var logicalLoads = cpu.Sensors
            .Where(x => x.Kind == LhmSensorKind.Load)
            .Where(x => x.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .Select((x, index) => new LogicalProcessorLoad(index, MapPercent(x)))
            .ToArray();

        var temperature = FindFirst(
            cpu,
            LhmSensorKind.Temperature,
            "Core (Tctl/Tdie)",
            "CPU Package",
            "Core Average",
            "Core Max");

        var packagePower = FindFirst(
            cpu,
            LhmSensorKind.Power,
            "CPU Package",
            "Package",
            "Package Power");

        var coreClocks = cpu.Sensors
            .Where(x => x.Kind == LhmSensorKind.Clock)
            .Where(x => x.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        MetricReading averageClock;
        var positiveClocks = coreClocks
            .Where(x => x.Value is > 0)
            .Select(x => x.Value!.Value)
            .ToArray();

        if (positiveClocks.Length > 0)
        {
            averageClock = MetricReading.Available(positiveClocks.Average());
        }
        else if (coreClocks.Length > 0)
        {
            averageClock = MetricReading.Invalid(
                coreClocks.First().Value,
                "CPU clock sensors were exposed but did not provide a positive reading.");
        }
        else
        {
            averageClock = Unsupported("CPU core clock sensors were not exposed.");
        }

        return new CpuTelemetry(
            MapPercent(cpu, LhmSensorKind.Load, "CPU Total"),
            logicalLoads,
            MapPositive(temperature, "CPU temperature"),
            MapPositive(packagePower, "CPU package power"),
            averageClock);
    }

    private static GpuTelemetry MapGpu(LhmHardwareSnapshot gpu)
    {
        var memoryUsed = FindFirst(gpu, LhmSensorKind.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
        var memoryTotal = FindFirst(gpu, LhmSensorKind.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total");
        var fan = gpu.Sensors.FirstOrDefault(x => x.Kind == LhmSensorKind.Fan && x.Name.StartsWith("GPU Fan", StringComparison.OrdinalIgnoreCase));
        var fanControl = gpu.Sensors.FirstOrDefault(x => x.Kind == LhmSensorKind.Control && x.Name.StartsWith("GPU Fan", StringComparison.OrdinalIgnoreCase));

        return new GpuTelemetry(
            gpu.Id,
            gpu.Name,
            MapPercent(gpu, LhmSensorKind.Load, "GPU Core"),
            MapPercent(gpu, LhmSensorKind.Load, "GPU Memory"),
            MapPositive(FindFirst(gpu, LhmSensorKind.Temperature, "GPU Core"), "GPU core temperature"),
            MapPositive(FindFirst(gpu, LhmSensorKind.Temperature, "GPU Hot Spot", "GPU Hotspot"), "GPU hotspot temperature"),
            MapPositive(FindFirst(gpu, LhmSensorKind.Temperature, "GPU Memory"), "GPU memory temperature"),
            MapNonNegative(FindFirst(gpu, LhmSensorKind.Power, "GPU Package", "GPU Power"), "GPU package power"),
            MapNonNegative(FindFirst(gpu, LhmSensorKind.Clock, "GPU Core"), "GPU core clock"),
            MapNonNegative(FindFirst(gpu, LhmSensorKind.Clock, "GPU Memory"), "GPU memory clock"),
            MapNonNegative(memoryUsed, "GPU dedicated memory used"),
            MapPositive(memoryTotal, "GPU dedicated memory total"),
            MapNonNegative(fan, "GPU fan speed"),
            MapPercent(fanControl));
    }

    private static MetricReading MapDataGiBToMiB(LhmHardwareSnapshot hardware, string name)
    {
        var sensor = FindFirst(hardware, LhmSensorKind.Data, name);
        if (sensor?.Value is not double raw)
        {
            return Unsupported($"{name} was not exposed.");
        }

        if (!double.IsFinite(raw) || raw < 0)
        {
            return MetricReading.Invalid(raw, $"{name} returned an invalid value.");
        }

        return MetricReading.Available(raw * 1024d, raw);
    }

    private static MetricReading MapPercent(
        LhmHardwareSnapshot hardware,
        LhmSensorKind kind,
        params string[] names) =>
        MapPercent(FindFirst(hardware, kind, names));

    private static MetricReading MapPercent(LhmSensorSnapshot? sensor)
    {
        if (sensor?.Value is not double raw)
        {
            return Unsupported("Percentage sensor was not exposed.");
        }

        if (!double.IsFinite(raw) || raw < 0 || raw > 100)
        {
            return MetricReading.Invalid(raw, $"{sensor.Name} returned a percentage outside 0-100.");
        }

        return MetricReading.Available(raw);
    }

    private static MetricReading MapPositive(LhmSensorSnapshot? sensor, string label)
    {
        if (sensor?.Value is not double raw)
        {
            return Unsupported($"{label} sensor was not exposed.");
        }

        if (!double.IsFinite(raw) || raw <= 0)
        {
            return MetricReading.Invalid(raw, $"{label} did not provide a positive reading.");
        }

        return MetricReading.Available(raw);
    }

    private static MetricReading MapNonNegative(LhmSensorSnapshot? sensor, string label)
    {
        if (sensor?.Value is not double raw)
        {
            return Unsupported($"{label} sensor was not exposed.");
        }

        if (!double.IsFinite(raw) || raw < 0)
        {
            return MetricReading.Invalid(raw, $"{label} returned a negative or non-finite reading.");
        }

        return MetricReading.Available(raw);
    }

    private static LhmSensorSnapshot? FindFirst(
        LhmHardwareSnapshot hardware,
        LhmSensorKind kind,
        params string[] names)
    {
        foreach (var name in names)
        {
            var sensor = hardware.Sensors.FirstOrDefault(
                x => x.Kind == kind && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (sensor is not null)
            {
                return sensor;
            }
        }

        return null;
    }

    private static MetricReading Unsupported(string detail) =>
        MetricReading.Unsupported(detail);
}
