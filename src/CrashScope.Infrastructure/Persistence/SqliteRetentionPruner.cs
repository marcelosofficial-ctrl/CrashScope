using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CrashScope.Infrastructure.Persistence;

public sealed record RetentionPruneResult(
    int SessionsDeleted,
    int IncidentsDeleted,
    DateTimeOffset CutoffUtc);

public sealed class SqliteRetentionPruner
{
    private readonly string _connectionString;

    public SqliteRetentionPruner(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async ValueTask<RetentionPruneResult> PruneAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        cutoffUtc = cutoffUtc.ToUniversalTime();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var pragmas = connection.CreateCommand())
        {
            pragmas.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            await pragmas.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        using var transaction = connection.BeginTransaction();
        var cutoff = cutoffUtc.ToString("O", CultureInfo.InvariantCulture);

        int sessionsDeleted;
        await using (var sessions = connection.CreateCommand())
        {
            sessions.Transaction = transaction;
            sessions.CommandText = """
                DELETE FROM workload_sessions
                WHERE ended_at_utc IS NOT NULL
                  AND ended_at_utc < $cutoff_utc;
                """;
            sessions.Parameters.AddWithValue("$cutoff_utc", cutoff);
            sessionsDeleted = await sessions.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        int incidentsDeleted;
        await using (var incidents = connection.CreateCommand())
        {
            incidents.Transaction = transaction;
            incidents.CommandText = """
                DELETE FROM incidents
                WHERE incident_time_utc < $cutoff_utc
                  AND NOT EXISTS (
                      SELECT 1
                      FROM workload_session_incidents links
                      WHERE links.incident_id = incidents.incident_id
                  );
                """;
            incidents.Parameters.AddWithValue("$cutoff_utc", cutoff);
            incidentsDeleted = await incidents.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new RetentionPruneResult(sessionsDeleted, incidentsDeleted, cutoffUtc);
    }
}
