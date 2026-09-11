using CrashScope.Core.Incidents;
using CrashScope.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace CrashScope.Infrastructure.Tests;

public sealed class SqliteIncidentReportRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CrashScope.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Initialize_CreatesSchemaMigrationAndWalDatabase()
    {
        var path = DatabasePath();
        var repository = new SqliteIncidentReportRepository(path);

        await repository.InitializeAsync();

        Assert.True(File.Exists(path));

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        await using var migration = connection.CreateCommand();
        migration.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        var version = Convert.ToInt32(await migration.ExecuteScalarAsync());
        Assert.Equal(SqliteIncidentReportRepository.CurrentSchemaVersion, version);

        await using var journal = connection.CreateCommand();
        journal.CommandText = "PRAGMA journal_mode;";
        var mode = Convert.ToString(await journal.ExecuteScalarAsync());
        Assert.Equal("wal", mode, ignoreCase: true);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsCompleteIncidentReport()
    {
        var repository = new SqliteIncidentReportRepository(DatabasePath());
        await repository.InitializeAsync();
        var expected = CreateReport(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 9, 8, 7, 30, 0, TimeSpan.Zero));

        await repository.SaveAsync(expected);
        var reports = await repository.LoadAllAsync();

        var actual = Assert.Single(reports);
        Assert.Equal(expected.IncidentId, actual.IncidentId);
        Assert.Equal(expected.Classification, actual.Classification);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Summary, actual.Summary);
        Assert.Equal(expected.Assessment, actual.Assessment);
        Assert.Equal(expected.IncidentTimeUtc, actual.IncidentTimeUtc);
        Assert.Equal(expected.Process, actual.Process);
        Assert.Equal(expected.Telemetry, actual.Telemetry);
        Assert.Equal(expected.Evidence, actual.Evidence);
    }

    [Fact]
    public async Task Save_SameIncidentIdIsIdempotentAndReplacesEvidence()
    {
        var repository = new SqliteIncidentReportRepository(DatabasePath());
        await repository.InitializeAsync();
        var id = Guid.NewGuid();
        var first = CreateReport(
            id,
            new DateTimeOffset(2026, 9, 8, 7, 30, 0, TimeSpan.Zero));
        var replacement = new IncidentReport(
            id,
            IncidentClassification.HardwareError,
            "Updated title",
            "Updated summary",
            "Updated assessment",
            first.IncidentTimeUtc,
            null,
            new IncidentTelemetrySummary(3, null, null, 50, 60, 70, 8000, 40),
            new[]
            {
                new IncidentEvidenceItem(
                    first.IncidentTimeUtc,
                    first.IncidentTimeUtc,
                    IncidentEvidenceRole.Trigger,
                    "WHEA",
                    "HardwareError",
                    "Replacement evidence",
                    "event:replacement")
            });

        await repository.SaveAsync(first);
        await repository.SaveAsync(replacement);
        var reports = await repository.LoadAllAsync();

        var actual = Assert.Single(reports);
        Assert.Equal("Updated title", actual.Title);
        Assert.Single(actual.Evidence);
        Assert.Equal("Replacement evidence", actual.Evidence[0].Summary);
    }

    [Fact]
    public async Task LoadAll_ReturnsIncidentsInChronologicalOrder()
    {
        var repository = new SqliteIncidentReportRepository(DatabasePath());
        await repository.InitializeAsync();

        var later = CreateReport(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero));
        var earlier = CreateReport(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 9, 8, 8, 0, 0, TimeSpan.Zero));

        await repository.SaveAsync(later);
        await repository.SaveAsync(earlier);

        var reports = await repository.LoadAllAsync();

        Assert.Equal(new[] { earlier.IncidentId, later.IncidentId },
            reports.Select(x => x.IncidentId));
    }

    [Fact]
    public async Task Initialize_CanRunRepeatedlyWithoutChangingSchemaVersion()
    {
        var repository = new SqliteIncidentReportRepository(DatabasePath());

        await repository.InitializeAsync();
        await repository.InitializeAsync();

        await using var connection = new SqliteConnection(
            $"Data Source={repository.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations;";

        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    private static IncidentReport CreateReport(
        Guid id,
        DateTimeOffset incidentTimeUtc)
    {
        var process = new IncidentProcessContext(
            4242,
            incidentTimeUtc.AddMinutes(-10),
            "ExampleGame",
            "Exited",
            incidentTimeUtc.AddSeconds(1));
        var telemetry = new IncidentTelemetrySummary(
            91,
            incidentTimeUtc.AddSeconds(-60),
            incidentTimeUtc.AddSeconds(30),
            92.5,
            99,
            87,
            15000,
            78);
        var evidence = new[]
        {
            new IncidentEvidenceItem(
                incidentTimeUtc,
                incidentTimeUtc.AddSeconds(2),
                IncidentEvidenceRole.Trigger,
                "WindowsErrorReporting",
                "WindowsErrorReport",
                "LiveKernelEvent 193 was reported.",
                "report:abc"),
            new IncidentEvidenceItem(
                incidentTimeUtc.AddSeconds(1),
                incidentTimeUtc.AddSeconds(1),
                IncidentEvidenceRole.Corroborating,
                "Display",
                "DisplayDriver",
                "Display-driver evidence was recorded.",
                null)
        };

        return new IncidentReport(
            id,
            IncidentClassification.KernelOrDriverWatchdog,
            "ExampleGame kernel/driver watchdog evidence",
            "CrashScope correlated evidence around the incident.",
            "Evidence is consistent with a GPU/driver instability event without proving root cause.",
            incidentTimeUtc,
            process,
            telemetry,
            evidence);
    }

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
