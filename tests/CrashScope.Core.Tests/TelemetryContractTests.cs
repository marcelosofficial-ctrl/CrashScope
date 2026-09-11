using CrashScope.Core.Telemetry;

namespace CrashScope.Core.Tests;

public sealed class TelemetryContractTests
{
    [Fact]
    public void AvailableZeroRemainsARealReading()
    {
        var reading = MetricReading.Available(0);

        Assert.True(reading.IsAvailable);
        Assert.Equal(MetricState.Available, reading.State);
        Assert.Equal(0d, reading.Value);
    }

    [Fact]
    public void AvailableCanPreserveDifferentRawAndNormalizedValues()
    {
        var reading = MetricReading.Available(100, 129.74);

        Assert.True(reading.IsAvailable);
        Assert.Equal(100d, reading.Value);
        Assert.Equal(129.74d, reading.RawValue);
    }

    [Fact]
    public void UnsupportedDoesNotBecomeFakeZero()
    {
        var reading = MetricReading.Unsupported("Sensor not exposed.");

        Assert.False(reading.IsAvailable);
        Assert.Null(reading.Value);
        Assert.Equal(MetricState.Unsupported, reading.State);
    }

    [Fact]
    public void InvalidReadingPreservesRawEvidence()
    {
        var reading = MetricReading.Invalid(
            0,
            "CPU temperature of 0 C was rejected.");

        Assert.False(reading.IsAvailable);
        Assert.Null(reading.Value);
        Assert.Equal(0d, reading.RawValue);
        Assert.Equal(MetricState.Invalid, reading.State);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void AvailableRejectsNonFiniteNumbers(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MetricReading.Available(value));
    }

    [Fact]
    public void CpuTelemetryRejectsDuplicateLogicalProcessorIndexes()
    {
        Assert.Throws<ArgumentException>(
            () => new CpuTelemetry(
                MetricReading.Available(10),
                new[]
                {
                    new LogicalProcessorLoad(0, MetricReading.Available(8)),
                    new LogicalProcessorLoad(0, MetricReading.Available(12))
                },
                MetricReading.Unsupported(),
                MetricReading.Unsupported(),
                MetricReading.Unsupported()));
    }

    [Fact]
    public void TelemetryFrameRequiresUtcWallClockTime()
    {
        var nonUtc = new DateTimeOffset(
            2026, 9, 8, 12, 0, 0, TimeSpan.FromHours(9));

        Assert.Throws<ArgumentException>(
            () => new TelemetryFrame(
                1,
                nonUtc,
                1,
                CreateSystem(),
                CreateCpu(),
                Array.Empty<GpuTelemetry>()));
    }

    [Fact]
    public void TelemetryFrameDefensivelyCopiesGpuCollection()
    {
        var source = new List<GpuTelemetry> { CreateGpu() };

        var frame = new TelemetryFrame(
            1,
            DateTimeOffset.UtcNow,
            1,
            CreateSystem(),
            CreateCpu(),
            source);

        source.Clear();

        Assert.Single(frame.Gpus);
    }

    [Fact]
    public void TelemetryFrameRejectsNegativeSequence()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TelemetryFrame(
                -1,
                DateTimeOffset.UtcNow,
                1,
                CreateSystem(),
                CreateCpu(),
                Array.Empty<GpuTelemetry>()));
    }

    private static SystemTelemetry CreateSystem() =>
        new(
            MetricReading.Available(8192),
            MetricReading.Available(24576),
            MetricReading.Available(25));

    private static CpuTelemetry CreateCpu() =>
        new(
            MetricReading.Available(10),
            new[]
            {
                new LogicalProcessorLoad(0, MetricReading.Available(8)),
                new LogicalProcessorLoad(1, MetricReading.Available(12))
            },
            MetricReading.Unsupported(),
            MetricReading.Unsupported(),
            MetricReading.Unsupported());

    private static GpuTelemetry CreateGpu() =>
        new(
            "gpu-0",
            "Fixture GPU",
            MetricReading.Available(10),
            MetricReading.Available(5),
            MetricReading.Available(50),
            MetricReading.Available(55),
            MetricReading.Available(70),
            MetricReading.Available(40),
            MetricReading.Available(500),
            MetricReading.Available(2000),
            MetricReading.Available(4096),
            MetricReading.Available(16384),
            MetricReading.Available(0),
            MetricReading.Available(0));
}
