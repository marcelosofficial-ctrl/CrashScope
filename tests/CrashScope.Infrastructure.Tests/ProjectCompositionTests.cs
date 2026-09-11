using CrashScope.Core.Telemetry;

namespace CrashScope.Infrastructure.Tests;

public sealed class ProjectCompositionTests
{
    [Fact]
    public void InfrastructureCanConsumeCoreTelemetryContracts()
    {
        var metric = TelemetryMetric.GpuHotspotTemperatureCelsius;

        Assert.Equal(
            TelemetryMetric.GpuHotspotTemperatureCelsius,
            metric);
    }
}
