using System.Globalization;
using CrashScope.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace CrashScope.Infrastructure.Tests;

public sealed class SqliteRetentionPrunerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CrashScope-retention-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PruneAsync_RemovesOnlyExpiredUnprotectedHistory()
    {
        Directory.CreateDirectory(_root);
        var databasePath = Path.Combine(_root, "crashscope.db");
        var incidents = new SqliteIncidentReportRepository(databasePath);
        var sessions = new SqliteWorkloadSessionRepository(databasePath);
        var environments = new SqliteSessionEnvironmentStore(databasePath);
        await incidents.InitializeAsync();
        await sessions.InitializeAsync();
        await environments.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddDays(-30);
        var oldEndedSession = Guid.NewGuid();
        var oldActiveSession = Guid.NewGuid();
        var recentSession = Guid.NewGuid();
        var oldStandaloneIncident = Guid.NewGuid();
        var oldLinkedIncident = Guid.NewGuid();
        var recentIncident = Guid.NewGuid();

        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;");
            await InsertSessionAsync(connection, oldEndedSession, now.AddDays(-70), now.AddDays(-60));
            await InsertSessionAsync(connection, oldActiveSession, now.AddDays(-70), null);
            await InsertSessionAsync(connection, recentSession, now.AddDays(-8), now.AddDays(-5));
            await InsertEnvironmentAsync(connection, oldEndedSession);
            await InsertEnvironmentAsync(connection, oldActiveSession);
            await InsertIncidentAsync(connection, oldStandaloneIncident, now.AddDays(-60));
            await InsertIncidentAsync(connection, oldLinkedIncident, now.AddDays(-60));
            await InsertIncidentAsync(connection, recentIncident, now.AddDays(-5));
            await ExecuteAsync(connection,
                "INSERT INTO workload_session_incidents(session_id, incident_id) VALUES ($session, $incident);",
                ("$session", oldActiveSession.ToString("D")),
                ("$incident", oldLinkedIncident.ToString("D")));
        }

        var result = await new SqliteRetentionPruner(databasePath).PruneAsync(cutoff);

        Assert.Equal(1, result.SessionsDeleted);
        Assert.Equal(1, result.IncidentsDeleted);
        Assert.False(await ExistsAsync(databasePath, "workload_sessions", "session_id", oldEndedSession));
        Assert.True(await ExistsAsync(databasePath, "workload_sessions", "session_id", oldActiveSession));
        Assert.True(await ExistsAsync(databasePath, "workload_sessions", "session_id", recentSession));
        Assert.False(await ExistsAsync(databasePath, "session_environment_snapshots", "session_id", oldEndedSession));
        Assert.True(await ExistsAsync(databasePath, "session_environment_snapshots", "session_id", oldActiveSession));
        Assert.False(await ExistsAsync(databasePath, "incidents", "incident_id", oldStandaloneIncident));
        Assert.True(await ExistsAsync(databasePath, "incidents", "incident_id", oldLinkedIncident));
        Assert.True(await ExistsAsync(databasePath, "incidents", "incident_id", recentIncident));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task InsertSessionAsync(SqliteConnection connection, Guid id, DateTimeOffset started, DateTimeOffset? ended) =>
        await ExecuteAsync(connection, """
            INSERT INTO workload_sessions (
                session_id, process_id, process_start_time_utc, process_name, executable_path,
                started_at_utc, ended_at_utc, end_reason, telemetry_frame_count,
                peak_cpu_utilization_percent, peak_gpu_utilization_percent, peak_gpu_hotspot_celsius,
                peak_gpu_memory_used_mib, peak_system_memory_load_percent)
            VALUES ($id, 1, $started, 'test', NULL, $started, $ended, NULL, 0, NULL, NULL, NULL, NULL, NULL);
            """,
            ("$id", id.ToString("D")),
            ("$started", Format(started)),
            ("$ended", ended.HasValue ? Format(ended.Value) : DBNull.Value));

    private static async Task InsertEnvironmentAsync(SqliteConnection connection, Guid sessionId) =>
        await ExecuteAsync(connection,
            "INSERT INTO session_environment_snapshots(session_id, snapshot_json) VALUES ($id, '{}');",
            ("$id", sessionId.ToString("D")));

    private static async Task InsertIncidentAsync(SqliteConnection connection, Guid id, DateTimeOffset occurred) =>
        await ExecuteAsync(connection, """
            INSERT INTO incidents (
                incident_id, classification, title, summary, assessment, incident_time_utc,
                process_id, process_start_time_utc, process_name, process_observation_state,
                process_observed_at_utc, telemetry_frame_count, telemetry_window_start_utc,
                telemetry_window_end_utc, peak_cpu_utilization_percent, peak_gpu_utilization_percent,
                peak_gpu_hotspot_celsius, peak_gpu_memory_used_mib, peak_system_memory_load_percent)
            VALUES ($id, 0, 'test', 'test', 'test', $occurred, NULL, NULL, NULL, NULL, NULL, 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL);
            """,
            ("$id", id.ToString("D")),
            ("$occurred", Format(occurred)));

    private static async Task<bool> ExistsAsync(string databasePath, string table, string column, Guid id)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 FROM {table} WHERE {column} = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return await command.ExecuteScalarAsync() is not null;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
