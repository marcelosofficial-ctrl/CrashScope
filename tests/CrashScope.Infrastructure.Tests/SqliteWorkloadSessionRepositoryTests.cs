using CrashScope.Core.Sessions;
using CrashScope.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace CrashScope.Infrastructure.Tests;

public sealed class SqliteWorkloadSessionRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CrashScope.SessionTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Initialize_AddsVersionTwoMigrationAndSessionTables()
    {
        var path = DatabasePath();
        var incidentRepository = new SqliteIncidentReportRepository(path);
        var repository = new SqliteWorkloadSessionRepository(path);

        await incidentRepository.InitializeAsync();
        await repository.InitializeAsync();

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(version) FROM schema_migrations;";

        Assert.Equal(2, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsSessionSummaryAndIncidents()
    {
        var repository = new SqliteWorkloadSessionRepository(DatabasePath());
        await repository.InitializeAsync();
        var incidentA = Guid.NewGuid();
        var incidentB = Guid.NewGuid();
        var expected = Session(
            Guid.NewGuid(),
            Utc(8, 0),
            Utc(9, 0),
            SessionEndReason.ProcessExited,
            new SessionTelemetrySummary(3600, 91, 99, 86, 15100, 74),
            new[] { incidentA, incidentB });

        await repository.SaveAsync(expected);
        var loaded = await repository.LoadAllAsync();

        var actual = Assert.Single(loaded);
        Assert.Equal(expected.SessionId, actual.SessionId);
        Assert.Equal(expected.ProcessId, actual.ProcessId);
        Assert.Equal(expected.ProcessStartTimeUtc, actual.ProcessStartTimeUtc);
        Assert.Equal(expected.ProcessName, actual.ProcessName);
        Assert.Equal(expected.ExecutablePath, actual.ExecutablePath);
        Assert.Equal(expected.StartedAtUtc, actual.StartedAtUtc);
        Assert.Equal(expected.EndedAtUtc, actual.EndedAtUtc);
        Assert.Equal(expected.EndReason, actual.EndReason);
        Assert.Equal(expected.Telemetry, actual.Telemetry);
        Assert.Equal(expected.IncidentIds.Order(), actual.IncidentIds.Order());
    }

    [Fact]
    public async Task Save_SameSessionIdUpdatesWithoutDuplicatingIncidentLinks()
    {
        var repository = new SqliteWorkloadSessionRepository(DatabasePath());
        await repository.InitializeAsync();
        var id = Guid.NewGuid();
        var incident = Guid.NewGuid();
        var active = Session(id, Utc(8, 0), null, null, SessionTelemetrySummary.Empty, Array.Empty<Guid>());
        var ended = Session(
            id,
            Utc(8, 0),
            Utc(8, 20),
            SessionEndReason.UserStopped,
            new SessionTelemetrySummary(20, 50, 60, 70, 8000, 40),
            new[] { incident, incident });

        await repository.SaveAsync(active);
        await repository.SaveAsync(ended);

        var actual = Assert.Single(await repository.LoadAllAsync());
        Assert.False(actual.IsActive);
        Assert.Equal(SessionEndReason.UserStopped, actual.EndReason);
        Assert.Single(actual.IncidentIds);
    }

    private static WorkloadSession Session(
        Guid id,
        DateTimeOffset started,
        DateTimeOffset? ended,
        SessionEndReason? reason,
        SessionTelemetrySummary telemetry,
        IReadOnlyList<Guid> incidents) =>
        new(
            id,
            4242,
            started.AddMinutes(-5),
            "ExampleGame",
            @"C:\Games\ExampleGame.exe",
            started,
            ended,
            reason,
            telemetry,
            incidents);

    private static DateTimeOffset Utc(int hour, int minute) =>
        new(2026, 9, 8, hour, minute, 0, TimeSpan.Zero);

    private string DatabasePath() => Path.Combine(_directory, "crashscope.db");

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
