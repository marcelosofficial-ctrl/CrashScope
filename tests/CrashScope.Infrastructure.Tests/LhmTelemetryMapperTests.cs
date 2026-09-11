using CrashScope.Core.Telemetry;
using CrashScope.Infrastructure.Telemetry.LibreHardwareMonitor;

namespace CrashScope.Infrastructure.Tests;

public sealed class LhmTelemetryMapperTests
{
    [Fact]
    public void MapsSystemMemoryFromGiBToMiB()
    {
        var snapshot = LhmTelemetryMapper.Map(new[]
        {
            Hardware(
                "memory",
                "Generic Memory",
                LhmHardwareKind.Memory,
                Sensor("memory/used", "Memory Used", LhmSensorKind.Data, 8),
                Sensor("memory/available", "Memory Available", LhmSensorKind.Data, 24),
                Sensor("memory/load", "Memory", LhmSensorKind.Load, 25))
        });

        Assert.Equal(8192d, snapshot.System.PhysicalMemoryUsedMiB.Value);
        Assert.Equal(24576d, snapshot.System.PhysicalMemoryAvailableMiB.Value);
        Assert.Equal(25d, snapshot.System.PhysicalMemoryLoadPercent.Value);
    }

    [Fact]
    public void RejectsKnownZeroCpuTemperatureAndPowerReadings()
    {
        var snapshot = LhmTelemetryMapper.Map(new[]
        {
            Hardware(
                "cpu/0",
                "AMD Ryzen Fixture",
                LhmHardwareKind.Cpu,
                Sensor("cpu/load/0", "CPU Total", LhmSensorKind.Load, 12),
                Sensor("cpu/load/1", "CPU Core #1 Thread #1", LhmSensorKind.Load, 10),
                Sensor("cpu/temp/0", "Core (Tctl/Tdie)", LhmSensorKind.Temperature, 0),
                Sensor("cpu/power/0", "CPU Package", LhmSensorKind.Power, 0),
                Sensor("cpu/clock/0", "CPU Core #1", LhmSensorKind.Clock, 0))
        });

        Assert.Equal(MetricState.Invalid, snapshot.Cpu.TemperatureCelsius.State);
        Assert.Equal(0d, snapshot.Cpu.TemperatureCelsius.RawValue);
        Assert.Equal(MetricState.Invalid, snapshot.Cpu.PackagePowerWatts.State);
        Assert.Equal(MetricState.Invalid, snapshot.Cpu.AverageClockMHz.State);
    }

    [Fact]
    public void MapsGpuCoreMetricsWithoutUsingNoisyD3dEngineCounters()
    {
        var snapshot = LhmTelemetryMapper.Map(new[]
        {
            Hardware(
                "gpu-amd/0",
                "AMD Radeon Fixture",
                LhmHardwareKind.GpuAmd,
                Sensor("gpu/load/core", "GPU Core", LhmSensorKind.Load, 88),
                Sensor("gpu/load/memory", "GPU Memory", LhmSensorKind.Load, 42),
                Sensor("gpu/load/video", "D3D Video Codec Engine", LhmSensorKind.Load, 129.74),
                Sensor("gpu/temp/core", "GPU Core", LhmSensorKind.Temperature, 63),
                Sensor("gpu/temp/hotspot", "GPU Hot Spot", LhmSensorKind.Temperature, 79),
                Sensor("gpu/power/package", "GPU Package", LhmSensorKind.Power, 220),
                Sensor("gpu/clock/core", "GPU Core", LhmSensorKind.Clock, 2500),
                Sensor("gpu/clock/memory", "GPU Memory", LhmSensorKind.Clock, 2500),
                Sensor("gpu/memory/used", "GPU Memory Used", LhmSensorKind.SmallData, 8192),
                Sensor("gpu/memory/total", "GPU Memory Total", LhmSensorKind.SmallData, 16384),
                Sensor("gpu/fan/0", "GPU Fan", LhmSensorKind.Fan, 0),
                Sensor("gpu/control/0", "GPU Fan", LhmSensorKind.Control, 0))
        });

        var gpu = Assert.Single(snapshot.Gpus);

        Assert.Equal(88d, gpu.CoreUtilizationPercent.Value);
        Assert.Equal(42d, gpu.MemoryUtilizationPercent.Value);
        Assert.Equal(79d, gpu.HotspotTemperatureCelsius.Value);
        Assert.Equal(8192d, gpu.DedicatedMemoryUsedMiB.Value);
        Assert.Equal(16384d, gpu.DedicatedMemoryTotalMiB.Value);
        Assert.Equal(0d, gpu.FanSpeedRpm.Value);
        Assert.Equal(0d, gpu.FanControlPercent.Value);
    }

    [Fact]
    public void PreservesMultipleGpuDevices()
    {
        var snapshot = LhmTelemetryMapper.Map(new[]
        {
            Hardware("gpu-amd/0", "AMD GPU", LhmHardwareKind.GpuAmd),
            Hardware("gpu-nvidia/0", "NVIDIA GPU", LhmHardwareKind.GpuNvidia),
            Hardware("gpu-intel/0", "Intel GPU", LhmHardwareKind.GpuIntel)
        });

        Assert.Equal(3, snapshot.Gpus.Count);
        Assert.Equal(new[] { "gpu-amd/0", "gpu-nvidia/0", "gpu-intel/0" }, snapshot.Gpus.Select(x => x.DeviceId));
    }

    [Fact]
    public void MapsLogicalProcessorLoadsWithStableDistinctIndexes()
    {
        var snapshot = LhmTelemetryMapper.Map(new[]
        {
            Hardware(
                "cpu/0",
                "CPU Fixture",
                LhmHardwareKind.Cpu,
                Sensor("cpu/load/2", "CPU Core #2 Thread #1", LhmSensorKind.Load, 20),
                Sensor("cpu/load/1", "CPU Core #1 Thread #1", LhmSensorKind.Load, 10),
                Sensor("cpu/load/0", "CPU Total", LhmSensorKind.Load, 15))
        });

        Assert.Equal(2, snapshot.Cpu.LogicalProcessorUtilization.Count);
        Assert.Equal(0, snapshot.Cpu.LogicalProcessorUtilization[0].LogicalProcessorIndex);
        Assert.Equal(10d, snapshot.Cpu.LogicalProcessorUtilization[0].UtilizationPercent.Value);
        Assert.Equal(1, snapshot.Cpu.LogicalProcessorUtilization[1].LogicalProcessorIndex);
        Assert.Equal(20d, snapshot.Cpu.LogicalProcessorUtilization[1].UtilizationPercent.Value);
    }

    private static LhmHardwareSnapshot Hardware(
        string id,
        string name,
        LhmHardwareKind kind,
        params LhmSensorSnapshot[] sensors) =>
        new(id, name, kind, sensors);

    private static LhmSensorSnapshot Sensor(
        string id,
        string name,
        LhmSensorKind kind,
        double? value) =>
        new(id, name, kind, value);
}
