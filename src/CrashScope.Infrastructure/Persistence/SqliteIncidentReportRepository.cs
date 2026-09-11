using System.Globalization;
using CrashScope.Core.Incidents;
using Microsoft.Data.Sqlite;

namespace CrashScope.Infrastructure.Persistence;

public sealed class SqliteIncidentReportRepository : IIncidentReportRepository
{
    public const int CurrentSchemaVersion = 1;

    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteIncidentReportRepository(string databasePath)
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

    public string DatabasePath => _databasePath;

    public async ValueTask InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await ExecuteNonQueryAsync(connection, null, "PRAGMA journal_mode=WAL;", cancellationToken)
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA synchronous=NORMAL;", cancellationToken)
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA foreign_keys=ON;", cancellationToken)
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA busy_timeout=5000;", cancellationToken)
            .ConfigureAwait(false);

        using var transaction = connection.BeginTransaction();

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                applied_at_utc TEXT NOT NULL
            );
            """,
            cancellationToken).ConfigureAwait(false);

        await ApplyMigration1Async(connection, transaction, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SaveAsync(
        IncidentReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await EnableConnectionPragmasAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        using var transaction = connection.BeginTransaction();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO incidents (
                    incident_id,
                    classification,
                    title,
                    summary,
                    assessment,
                    incident_time_utc,
                    process_id,
                    process_start_time_utc,
                    process_name,
                    process_observation_state,
                    process_observed_at_utc,
                    telemetry_frame_count,
                    telemetry_window_start_utc,
                    telemetry_window_end_utc,
                    peak_cpu_utilization_percent,
                    peak_gpu_utilization_percent,
                    peak_gpu_hotspot_celsius,
                    peak_gpu_memory_used_mib,
                    peak_system_memory_load_percent)
                VALUES (
                    $incident_id,
                    $classification,
                    $title,
                    $summary,
                    $assessment,
                    $incident_time_utc,
                    $process_id,
                    $process_start_time_utc,
                    $process_name,
                    $process_observation_state,
                    $process_observed_at_utc,
                    $telemetry_frame_count,
                    $telemetry_window_start_utc,
                    $telemetry_window_end_utc,
                    $peak_cpu_utilization_percent,
                    $peak_gpu_utilization_percent,
                    $peak_gpu_hotspot_celsius,
                    $peak_gpu_memory_used_mib,
                    $peak_system_memory_load_percent)
                ON CONFLICT(incident_id) DO UPDATE SET
                    classification = excluded.classification,
                    title = excluded.title,
                    summary = excluded.summary,
                    assessment = excluded.assessment,
                    incident_time_utc = excluded.incident_time_utc,
                    process_id = excluded.process_id,
                    process_start_time_utc = excluded.process_start_time_utc,
                    process_name = excluded.process_name,
                    process_observation_state = excluded.process_observation_state,
                    process_observed_at_utc = excluded.process_observed_at_utc,
                    telemetry_frame_count = excluded.telemetry_frame_count,
                    telemetry_window_start_utc = excluded.telemetry_window_start_utc,
                    telemetry_window_end_utc = excluded.telemetry_window_end_utc,
                    peak_cpu_utilization_percent = excluded.peak_cpu_utilization_percent,
                    peak_gpu_utilization_percent = excluded.peak_gpu_utilization_percent,
                    peak_gpu_hotspot_celsius = excluded.peak_gpu_hotspot_celsius,
                    peak_gpu_memory_used_mib = excluded.peak_gpu_memory_used_mib,
                    peak_system_memory_load_percent = excluded.peak_system_memory_load_percent;
                """;

            AddParameter(command, "$incident_id", report.IncidentId.ToString("D"));
            AddParameter(command, "$classification", (int)report.Classification);
            AddParameter(command, "$title", report.Title);
            AddParameter(command, "$summary", report.Summary);
            AddParameter(command, "$assessment", report.Assessment);
            AddParameter(command, "$incident_time_utc", FormatUtc(report.IncidentTimeUtc));
            AddParameter(command, "$process_id", report.Process?.ProcessId);
            AddParameter(command, "$process_start_time_utc", FormatUtc(report.Process?.StartTimeUtc));
            AddParameter(command, "$process_name", report.Process?.Name);
            AddParameter(command, "$process_observation_state", report.Process?.ObservationState);
            AddParameter(command, "$process_observed_at_utc", FormatUtc(report.Process?.ObservedAtUtc));
            AddParameter(command, "$telemetry_frame_count", report.Telemetry.FrameCount);
            AddParameter(command, "$telemetry_window_start_utc", FormatUtc(report.Telemetry.WindowStartUtc));
            AddParameter(command, "$telemetry_window_end_utc", FormatUtc(report.Telemetry.WindowEndUtc));
            AddParameter(command, "$peak_cpu_utilization_percent", report.Telemetry.PeakCpuUtilizationPercent);
            AddParameter(command, "$peak_gpu_utilization_percent", report.Telemetry.PeakGpuUtilizationPercent);
            AddParameter(command, "$peak_gpu_hotspot_celsius", report.Telemetry.PeakGpuHotspotCelsius);
            AddParameter(command, "$peak_gpu_memory_used_mib", report.Telemetry.PeakGpuMemoryUsedMiB);
            AddParameter(command, "$peak_system_memory_load_percent", report.Telemetry.PeakSystemMemoryLoadPercent);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var deleteEvidence = connection.CreateCommand())
        {
            deleteEvidence.Transaction = transaction;
            deleteEvidence.CommandText =
                "DELETE FROM incident_evidence WHERE incident_id = $incident_id;";
            AddParameter(deleteEvidence, "$incident_id", report.IncidentId.ToString("D"));
            await deleteEvidence.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        for (var index = 0; index < report.Evidence.Count; index++)
        {
            var evidence = report.Evidence[index];
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO incident_evidence (
                    incident_id,
                    ordinal,
                    occurred_at_utc,
                    observed_at_utc,
                    role,
                    source,
                    kind,
                    summary,
                    evidence_key)
                VALUES (
                    $incident_id,
                    $ordinal,
                    $occurred_at_utc,
                    $observed_at_utc,
                    $role,
                    $source,
                    $kind,
                    $summary,
                    $evidence_key);
                """;

            AddParameter(command, "$incident_id", report.IncidentId.ToString("D"));
            AddParameter(command, "$ordinal", index);
            AddParameter(command, "$occurred_at_utc", FormatUtc(evidence.OccurredAtUtc));
            AddParameter(command, "$observed_at_utc", FormatUtc(evidence.ObservedAtUtc));
            AddParameter(command, "$role", (int)evidence.Role);
            AddParameter(command, "$source", evidence.Source);
            AddParameter(command, "$kind", evidence.Kind);
            AddParameter(command, "$summary", evidence.Summary);
            AddParameter(command, "$evidence_key", evidence.EvidenceKey);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<IncidentReport>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rows = await LoadIncidentRowsAsync(cancellationToken).ConfigureAwait(false);
        var reports = new List<IncidentReport>(rows.Count);

        foreach (var row in rows)
        {
            var evidence = await LoadEvidenceAsync(row.IncidentId, cancellationToken)
                .ConfigureAwait(false);

            reports.Add(new IncidentReport(
                row.IncidentId,
                row.Classification,
                row.Title,
                row.Summary,
                row.Assessment,
                row.IncidentTimeUtc,
                row.Process,
                row.Telemetry,
                evidence));
        }

        return reports;
    }

    private async ValueTask<IReadOnlyList<IncidentRow>> LoadIncidentRowsAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await EnableConnectionPragmasAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<IncidentRow>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                incident_id,
                classification,
                title,
                summary,
                assessment,
                incident_time_utc,
                process_id,
                process_start_time_utc,
                process_name,
                process_observation_state,
                process_observed_at_utc,
                telemetry_frame_count,
                telemetry_window_start_utc,
                telemetry_window_end_utc,
                peak_cpu_utilization_percent,
                peak_gpu_utilization_percent,
                peak_gpu_hotspot_celsius,
                peak_gpu_memory_used_mib,
                peak_system_memory_load_percent
            FROM incidents
            ORDER BY incident_time_utc ASC, incident_id ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var process = reader.IsDBNull(6)
                ? null
                : new IncidentProcessContext(
                    reader.GetInt32(6),
                    ParseUtc(reader.GetString(7)),
                    reader.GetString(8),
                    reader.GetString(9),
                    ParseUtc(reader.GetString(10)));

            var telemetry = new IncidentTelemetrySummary(
                reader.GetInt32(11),
                GetNullableUtc(reader, 12),
                GetNullableUtc(reader, 13),
                GetNullableDouble(reader, 14),
                GetNullableDouble(reader, 15),
                GetNullableDouble(reader, 16),
                GetNullableDouble(reader, 17),
                GetNullableDouble(reader, 18));

            rows.Add(new IncidentRow(
                Guid.Parse(reader.GetString(0)),
                (IncidentClassification)reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                ParseUtc(reader.GetString(5)),
                process,
                telemetry));
        }

        return rows;
    }

    private async ValueTask<IReadOnlyList<IncidentEvidenceItem>> LoadEvidenceAsync(
        Guid incidentId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await EnableConnectionPragmasAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        var evidence = new List<IncidentEvidenceItem>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                occurred_at_utc,
                observed_at_utc,
                role,
                source,
                kind,
                summary,
                evidence_key
            FROM incident_evidence
            WHERE incident_id = $incident_id
            ORDER BY ordinal ASC;
            """;
        AddParameter(command, "$incident_id", incidentId.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            evidence.Add(new IncidentEvidenceItem(
                ParseUtc(reader.GetString(0)),
                ParseUtc(reader.GetString(1)),
                (IncidentEvidenceRole)reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return evidence;
    }

    private static async ValueTask ApplyMigration1Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (await MigrationExistsAsync(connection, transaction, 1, cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            CREATE TABLE incidents (
                incident_id TEXT PRIMARY KEY,
                classification INTEGER NOT NULL,
                title TEXT NOT NULL,
                summary TEXT NOT NULL,
                assessment TEXT NOT NULL,
                incident_time_utc TEXT NOT NULL,
                process_id INTEGER NULL,
                process_start_time_utc TEXT NULL,
                process_name TEXT NULL,
                process_observation_state TEXT NULL,
                process_observed_at_utc TEXT NULL,
                telemetry_frame_count INTEGER NOT NULL,
                telemetry_window_start_utc TEXT NULL,
                telemetry_window_end_utc TEXT NULL,
                peak_cpu_utilization_percent REAL NULL,
                peak_gpu_utilization_percent REAL NULL,
                peak_gpu_hotspot_celsius REAL NULL,
                peak_gpu_memory_used_mib REAL NULL,
                peak_system_memory_load_percent REAL NULL
            );

            CREATE TABLE incident_evidence (
                incident_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                observed_at_utc TEXT NOT NULL,
                role INTEGER NOT NULL,
                source TEXT NOT NULL,
                kind TEXT NOT NULL,
                summary TEXT NOT NULL,
                evidence_key TEXT NULL,
                PRIMARY KEY (incident_id, ordinal),
                FOREIGN KEY (incident_id)
                    REFERENCES incidents(incident_id)
                    ON DELETE CASCADE
            );

            CREATE INDEX ix_incidents_incident_time
                ON incidents(incident_time_utc);

            CREATE INDEX ix_incident_evidence_occurred_at
                ON incident_evidence(occurred_at_utc);
            """,
            cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO schema_migrations(version, applied_at_utc)
            VALUES ($version, $applied_at_utc);
            """;
        AddParameter(command, "$version", 1);
        AddParameter(command, "$applied_at_utc", FormatUtc(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<bool> MigrationExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT 1 FROM schema_migrations WHERE version = $version LIMIT 1;";
        AddParameter(command, "$version", version);

        var result = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return result is not null;
    }

    private async ValueTask<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async ValueTask EnableConnectionPragmasAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            connection,
            null,
            "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;",
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ExecuteNonQueryAsync(
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

    private static void AddParameter(
        SqliteCommand command,
        string name,
        object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string? FormatUtc(DateTimeOffset? value) =>
        value.HasValue ? FormatUtc(value.Value) : null;

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).ToUniversalTime();

    private static DateTimeOffset? GetNullableUtc(
        SqliteDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ParseUtc(reader.GetString(ordinal));

    private static double? GetNullableDouble(
        SqliteDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private sealed record IncidentRow(
        Guid IncidentId,
        IncidentClassification Classification,
        string Title,
        string Summary,
        string Assessment,
        DateTimeOffset IncidentTimeUtc,
        IncidentProcessContext? Process,
        IncidentTelemetrySummary Telemetry);
}
