using System.Globalization;
using CrashScope.Core.Sessions;
using Microsoft.Data.Sqlite;

namespace CrashScope.Infrastructure.Persistence;

public sealed class SqliteWorkloadSessionRepository : IWorkloadSessionRepository
{
    public const int CurrentSchemaVersion = 2;

    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteWorkloadSessionRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, null, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, null, "PRAGMA synchronous=NORMAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, null, "PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, null, "PRAGMA busy_timeout=5000;", cancellationToken).ConfigureAwait(false);

        using var transaction = connection.BeginTransaction();

        await ExecuteAsync(
            connection,
            transaction,
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                applied_at_utc TEXT NOT NULL
            );
            """,
            cancellationToken).ConfigureAwait(false);

        if (!await MigrationExistsAsync(connection, transaction, 2, cancellationToken).ConfigureAwait(false))
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE TABLE workload_sessions (
                    session_id TEXT PRIMARY KEY,
                    process_id INTEGER NOT NULL,
                    process_start_time_utc TEXT NOT NULL,
                    process_name TEXT NOT NULL,
                    executable_path TEXT NULL,
                    started_at_utc TEXT NOT NULL,
                    ended_at_utc TEXT NULL,
                    end_reason INTEGER NULL,
                    telemetry_frame_count INTEGER NOT NULL,
                    peak_cpu_utilization_percent REAL NULL,
                    peak_gpu_utilization_percent REAL NULL,
                    peak_gpu_hotspot_celsius REAL NULL,
                    peak_gpu_memory_used_mib REAL NULL,
                    peak_system_memory_load_percent REAL NULL
                );

                CREATE TABLE workload_session_incidents (
                    session_id TEXT NOT NULL,
                    incident_id TEXT NOT NULL,
                    PRIMARY KEY (session_id, incident_id),
                    FOREIGN KEY (session_id)
                        REFERENCES workload_sessions(session_id)
                        ON DELETE CASCADE
                );

                CREATE INDEX ix_workload_sessions_started_at
                    ON workload_sessions(started_at_utc);

                CREATE INDEX ix_workload_sessions_process_identity
                    ON workload_sessions(process_id, process_start_time_utc);
                """,
                cancellationToken).ConfigureAwait(false);

            await using var migration = connection.CreateCommand();
            migration.Transaction = transaction;
            migration.CommandText = """
                INSERT INTO schema_migrations(version, applied_at_utc)
                VALUES (2, $applied_at_utc);
                """;
            migration.Parameters.AddWithValue(
                "$applied_at_utc",
                FormatUtc(DateTimeOffset.UtcNow));
            await migration.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SaveAsync(
        WorkloadSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await EnablePragmasAsync(connection, cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO workload_sessions (
                    session_id,
                    process_id,
                    process_start_time_utc,
                    process_name,
                    executable_path,
                    started_at_utc,
                    ended_at_utc,
                    end_reason,
                    telemetry_frame_count,
                    peak_cpu_utilization_percent,
                    peak_gpu_utilization_percent,
                    peak_gpu_hotspot_celsius,
                    peak_gpu_memory_used_mib,
                    peak_system_memory_load_percent)
                VALUES (
                    $session_id,
                    $process_id,
                    $process_start_time_utc,
                    $process_name,
                    $executable_path,
                    $started_at_utc,
                    $ended_at_utc,
                    $end_reason,
                    $telemetry_frame_count,
                    $peak_cpu_utilization_percent,
                    $peak_gpu_utilization_percent,
                    $peak_gpu_hotspot_celsius,
                    $peak_gpu_memory_used_mib,
                    $peak_system_memory_load_percent)
                ON CONFLICT(session_id) DO UPDATE SET
                    process_id = excluded.process_id,
                    process_start_time_utc = excluded.process_start_time_utc,
                    process_name = excluded.process_name,
                    executable_path = excluded.executable_path,
                    started_at_utc = excluded.started_at_utc,
                    ended_at_utc = excluded.ended_at_utc,
                    end_reason = excluded.end_reason,
                    telemetry_frame_count = excluded.telemetry_frame_count,
                    peak_cpu_utilization_percent = excluded.peak_cpu_utilization_percent,
                    peak_gpu_utilization_percent = excluded.peak_gpu_utilization_percent,
                    peak_gpu_hotspot_celsius = excluded.peak_gpu_hotspot_celsius,
                    peak_gpu_memory_used_mib = excluded.peak_gpu_memory_used_mib,
                    peak_system_memory_load_percent = excluded.peak_system_memory_load_percent;
                """;

            Add(command, "$session_id", session.SessionId.ToString("D"));
            Add(command, "$process_id", session.ProcessId);
            Add(command, "$process_start_time_utc", FormatUtc(session.ProcessStartTimeUtc));
            Add(command, "$process_name", session.ProcessName);
            Add(command, "$executable_path", session.ExecutablePath);
            Add(command, "$started_at_utc", FormatUtc(session.StartedAtUtc));
            Add(command, "$ended_at_utc", FormatUtc(session.EndedAtUtc));
            Add(command, "$end_reason", session.EndReason.HasValue ? (int)session.EndReason.Value : null);
            Add(command, "$telemetry_frame_count", session.Telemetry.FrameCount);
            Add(command, "$peak_cpu_utilization_percent", session.Telemetry.PeakCpuUtilizationPercent);
            Add(command, "$peak_gpu_utilization_percent", session.Telemetry.PeakGpuUtilizationPercent);
            Add(command, "$peak_gpu_hotspot_celsius", session.Telemetry.PeakGpuHotspotCelsius);
            Add(command, "$peak_gpu_memory_used_mib", session.Telemetry.PeakGpuMemoryUsedMiB);
            Add(command, "$peak_system_memory_load_percent", session.Telemetry.PeakSystemMemoryLoadPercent);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM workload_session_incidents WHERE session_id = $session_id;";
            Add(delete, "$session_id", session.SessionId.ToString("D"));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var incidentId in session.IncidentIds.Distinct())
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO workload_session_incidents(session_id, incident_id)
                VALUES ($session_id, $incident_id);
                """;
            Add(command, "$session_id", session.SessionId.ToString("D"));
            Add(command, "$incident_id", incidentId.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<WorkloadSession>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await EnablePragmasAsync(connection, cancellationToken).ConfigureAwait(false);

        var rows = new List<SessionRow>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    session_id,
                    process_id,
                    process_start_time_utc,
                    process_name,
                    executable_path,
                    started_at_utc,
                    ended_at_utc,
                    end_reason,
                    telemetry_frame_count,
                    peak_cpu_utilization_percent,
                    peak_gpu_utilization_percent,
                    peak_gpu_hotspot_celsius,
                    peak_gpu_memory_used_mib,
                    peak_system_memory_load_percent
                FROM workload_sessions
                ORDER BY started_at_utc ASC, session_id ASC;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new SessionRow(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetInt32(1),
                    ParseUtc(reader.GetString(2)),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    ParseUtc(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : ParseUtc(reader.GetString(6)),
                    reader.IsDBNull(7) ? null : (SessionEndReason)reader.GetInt32(7),
                    reader.GetInt64(8),
                    GetNullableDouble(reader, 9),
                    GetNullableDouble(reader, 10),
                    GetNullableDouble(reader, 11),
                    GetNullableDouble(reader, 12),
                    GetNullableDouble(reader, 13)));
            }
        }

        var sessions = new List<WorkloadSession>(rows.Count);
        foreach (var row in rows)
        {
            var incidentIds = await LoadIncidentIdsAsync(row.SessionId, cancellationToken).ConfigureAwait(false);
            sessions.Add(new WorkloadSession(
                row.SessionId,
                row.ProcessId,
                row.ProcessStartTimeUtc,
                row.ProcessName,
                row.ExecutablePath,
                row.StartedAtUtc,
                row.EndedAtUtc,
                row.EndReason,
                new SessionTelemetrySummary(
                    row.FrameCount,
                    row.PeakCpu,
                    row.PeakGpu,
                    row.PeakHotspot,
                    row.PeakVram,
                    row.PeakMemoryLoad),
                incidentIds));
        }

        return sessions;
    }

    private async ValueTask<IReadOnlyList<Guid>> LoadIncidentIdsAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await EnablePragmasAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT incident_id
            FROM workload_session_incidents
            WHERE session_id = $session_id
            ORDER BY incident_id ASC;
            """;
        Add(command, "$session_id", sessionId.ToString("D"));

        var result = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(Guid.Parse(reader.GetString(0)));
        }

        return result;
    }

    private async ValueTask<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async ValueTask EnablePragmasAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            connection,
            null,
            "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;",
            cancellationToken).ConfigureAwait(false);

    private static async ValueTask<bool> MigrationExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM schema_migrations WHERE version = $version LIMIT 1;";
        Add(command, "$version", version);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static async ValueTask ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string? FormatUtc(DateTimeOffset? value) =>
        value.HasValue ? FormatUtc(value.Value) : null;

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .ToUniversalTime();

    private static double? GetNullableDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private sealed record SessionRow(
        Guid SessionId,
        int ProcessId,
        DateTimeOffset ProcessStartTimeUtc,
        string ProcessName,
        string? ExecutablePath,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc,
        SessionEndReason? EndReason,
        long FrameCount,
        double? PeakCpu,
        double? PeakGpu,
        double? PeakHotspot,
        double? PeakVram,
        double? PeakMemoryLoad);
}
