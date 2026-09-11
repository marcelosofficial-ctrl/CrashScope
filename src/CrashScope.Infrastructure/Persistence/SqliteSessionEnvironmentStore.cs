using System.Globalization;
using System.Text.Json;
using CrashScope.Core.Sessions;
using Microsoft.Data.Sqlite;

namespace CrashScope.Infrastructure.Persistence;

public sealed class SqliteSessionEnvironmentStore
{
    public const int CurrentSchemaVersion = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    public SqliteSessionEnvironmentStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys=ON;
            PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                applied_at_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS session_environment_snapshots (
                session_id TEXT PRIMARY KEY,
                snapshot_json TEXT NOT NULL,
                FOREIGN KEY (session_id)
                    REFERENCES workload_sessions(session_id)
                    ON DELETE CASCADE
            );
            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc)
            VALUES (3, $applied_at_utc);
            """;
        command.Parameters.AddWithValue(
            "$applied_at_utc",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SaveAsync(
        Guid sessionId,
        SessionEnvironmentSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(snapshot);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys=ON;
            PRAGMA busy_timeout=5000;
            INSERT INTO session_environment_snapshots(session_id, snapshot_json)
            VALUES ($session_id, $snapshot_json)
            ON CONFLICT(session_id) DO UPDATE SET
                snapshot_json = excluded.snapshot_json;
            """;
        command.Parameters.AddWithValue("$session_id", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$snapshot_json", JsonSerializer.Serialize(snapshot, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SessionEnvironmentSnapshot?> LoadAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT snapshot_json
            FROM session_environment_snapshots
            WHERE session_id = $session_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$session_id", sessionId.ToString("D"));
        var json = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<SessionEnvironmentSnapshot>(json, JsonOptions);
    }
}
