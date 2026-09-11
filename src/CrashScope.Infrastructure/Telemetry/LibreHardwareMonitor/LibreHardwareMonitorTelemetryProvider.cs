using CrashScope.Core.Telemetry;
using LibreHardwareMonitor.Hardware;

namespace CrashScope.Infrastructure.Telemetry.LibreHardwareMonitor;

public sealed class LibreHardwareMonitorTelemetryProvider : IHardwareTelemetryProvider, IDisposable
{
    private static readonly TelemetryMetric[] SupportedMetrics =
    {
        TelemetryMetric.SystemPhysicalMemoryUsedMiB,
        TelemetryMetric.SystemPhysicalMemoryAvailableMiB,
        TelemetryMetric.SystemPhysicalMemoryLoadPercent,
        TelemetryMetric.CpuTotalUtilizationPercent,
        TelemetryMetric.CpuLogicalProcessorUtilizationPercent,
        TelemetryMetric.CpuTemperatureCelsius,
        TelemetryMetric.CpuPackagePowerWatts,
        TelemetryMetric.CpuAverageClockMHz,
        TelemetryMetric.GpuCoreUtilizationPercent,
        TelemetryMetric.GpuMemoryUtilizationPercent,
        TelemetryMetric.GpuCoreTemperatureCelsius,
        TelemetryMetric.GpuHotspotTemperatureCelsius,
        TelemetryMetric.GpuMemoryTemperatureCelsius,
        TelemetryMetric.GpuPackagePowerWatts,
        TelemetryMetric.GpuCoreClockMHz,
        TelemetryMetric.GpuMemoryClockMHz,
        TelemetryMetric.GpuDedicatedMemoryUsedMiB,
        TelemetryMetric.GpuDedicatedMemoryTotalMiB,
        TelemetryMetric.GpuFanSpeedRpm,
        TelemetryMetric.GpuFanControlPercent
    };

    private readonly Computer _computer;
    private readonly object _sync = new();
    private bool _disposed;

    public string Name => "LibreHardwareMonitor";

    public IReadOnlyCollection<TelemetryMetric> Capabilities => SupportedMetrics;

    public LibreHardwareMonitorTelemetryProvider()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = false,
            IsStorageEnabled = false,
            IsNetworkEnabled = false,
            IsControllerEnabled = false
        };

        _computer.Open();
    }

    public ValueTask<HardwareTelemetrySnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            foreach (var hardware in _computer.Hardware)
            {
                UpdateHardware(hardware);
            }

            var snapshot = _computer.Hardware
                .Select(CaptureHardware)
                .ToArray();

            return ValueTask.FromResult(LhmTelemetryMapper.Map(snapshot));
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _computer.Close();
            _disposed = true;
        }
    }

    private static void UpdateHardware(IHardware hardware)
    {
        hardware.Update();

        foreach (var subHardware in hardware.SubHardware)
        {
            UpdateHardware(subHardware);
        }
    }

    private static LhmHardwareSnapshot CaptureHardware(IHardware hardware)
    {
        var sensors = new List<LhmSensorSnapshot>();
        CaptureSensors(hardware, sensors);

        return new LhmHardwareSnapshot(
            hardware.Identifier.ToString(),
            hardware.Name,
            MapHardwareKind(hardware.HardwareType.ToString()),
            sensors);
    }

    private static void CaptureSensors(
        IHardware hardware,
        ICollection<LhmSensorSnapshot> sensors)
    {
        foreach (var sensor in hardware.Sensors)
        {
            sensors.Add(new LhmSensorSnapshot(
                sensor.Identifier.ToString(),
                sensor.Name,
                MapSensorKind(sensor.SensorType),
                sensor.Value.HasValue ? sensor.Value.Value : null));
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            CaptureSensors(subHardware, sensors);
        }
    }

    private static LhmHardwareKind MapHardwareKind(string hardwareType) =>
        hardwareType switch
        {
            "Cpu" => LhmHardwareKind.Cpu,
            "Memory" => LhmHardwareKind.Memory,
            "GpuAmd" => LhmHardwareKind.GpuAmd,
            "GpuNvidia" => LhmHardwareKind.GpuNvidia,
            "GpuIntel" => LhmHardwareKind.GpuIntel,
            _ => LhmHardwareKind.Other
        };

    private static LhmSensorKind MapSensorKind(SensorType sensorType) =>
        sensorType switch
        {
            SensorType.Load => LhmSensorKind.Load,
            SensorType.Temperature => LhmSensorKind.Temperature,
            SensorType.Power => LhmSensorKind.Power,
            SensorType.Clock => LhmSensorKind.Clock,
            SensorType.Data => LhmSensorKind.Data,
            SensorType.SmallData => LhmSensorKind.SmallData,
            SensorType.Fan => LhmSensorKind.Fan,
            SensorType.Control => LhmSensorKind.Control,
            _ => LhmSensorKind.Other
        };
}
