using CrashScope.Core.Sessions;
using CrashScope.Infrastructure.Persistence;

namespace CrashScope.Infrastructure.Tests;

public sealed class SessionEnvironmentStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CrashScope.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_RoundTripsEnvironmentSnapshot()
    {
        var path = Path.Combine(_directory, "crashscope.db");
        var sessions = new SqliteWorkloadSessionRepository(path);
        await sessions.InitializeAsync();

        var session = new WorkloadSession(
            Guid.NewGuid(),
            4242,
            Utc(9, 0),
            "ExampleGame",
            @"C:\Games\ExampleGame.exe",
            Utc(9, 1),
            null,
            null,
            SessionTelemetrySummary.Empty);
        await sessions.SaveAsync(session);

        var store = new SqliteSessionEnvironmentStore(path);
        await store.InitializeAsync();
        var expected = new SessionEnvironmentSnapshot(
            Utc(9, 1),
            "Microsoft Windows 11",
            "X64",
            "X64",
            ".NET 10.0.11",
            "1.0.0",
            "AMD Ryzen 5 7500F",
            12,
            53865,
            new[]
            {
                new EnvironmentGpuSnapshot("AMD Radeon RX 9070 XT", "32.0.31035.1003")
            });

        await store.SaveAsync(session.SessionId, expected);
        var actual = await store.LoadAsync(session.SessionId);

        Assert.NotNull(actual);
        Assert.Equal(expected.CapturedAtUtc, actual!.CapturedAtUtc);
        Assert.Equal(expected.OperatingSystem, actual.OperatingSystem);
        Assert.Equal(expected.CpuName, actual.CpuName);
        Assert.Equal(expected.LogicalProcessorCount, actual.LogicalProcessorCount);
        Assert.Equal(expected.PhysicalMemoryMiB, actual.PhysicalMemoryMiB);
        Assert.Equal(expected.Gpus, actual.Gpus);
    }

    [Fact]
    public async Task Initialize_IsIdempotentAndAdvancesSchemaToVersionThree()
    {
        var path = Path.Combine(_directory, "crashscope.db");
        var sessions = new SqliteWorkloadSessionRepository(path);
        await sessions.InitializeAsync();

        var store = new SqliteSessionEnvironmentStore(path);
        await store.InitializeAsync();
        await store.InitializeAsync();

        Assert.Equal(3, SqliteSessionEnvironmentStore.CurrentSchemaVersion);
    }

    private static DateTimeOffset Utc(int hour, int minute) =>
        new(2026, 9, 8, hour, minute, 0, TimeSpan.Zero);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
