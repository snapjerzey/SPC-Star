using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace SPCStar.Core.Infrastructure;

public sealed class SqliteBackedSpcRepository : InMemorySpcRepository, IRepositoryPersistence
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public SqliteBackedSpcRepository(string storagePath)
    {
        StoragePath = storagePath;
        EnsureDatabase();
        Load();
    }

    public string StoragePath { get; }

    public void ImportFrom(ISpcRepository repository)
    {
        RepositorySnapshot.FromRepository(repository).CopyTo(this);
    }

    public void SaveChanges()
    {
        var snapshot = RepositorySnapshot.FromRepository(this);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO repository_snapshots (id, schema_version, saved_at_utc, payload_json)
            VALUES (1, $schemaVersion, $savedAtUtc, $payloadJson)
            ON CONFLICT(id) DO UPDATE SET
                schema_version = excluded.schema_version,
                saved_at_utc = excluded.saved_at_utc,
                payload_json = excluded.payload_json;
            """;
        command.Parameters.AddWithValue("$schemaVersion", CurrentSchemaVersion);
        command.Parameters.AddWithValue("$savedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$payloadJson", json);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private void Load()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM repository_snapshots WHERE id = 1;";
        var json = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var snapshot = JsonSerializer.Deserialize<RepositorySnapshot>(json, JsonOptions);
        snapshot?.CopyTo(this);
    }

    private void EnsureDatabase()
    {
        var directory = Path.GetDirectoryName(StoragePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenConnection();
        ExecuteNonQuery(connection, "PRAGMA journal_mode=WAL;");
        ExecuteNonQuery(connection, "PRAGMA foreign_keys=ON;");
        ExecuteNonQuery(
            connection,
            """
            CREATE TABLE IF NOT EXISTS repository_snapshots (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                schema_version INTEGER NOT NULL,
                saved_at_utc TEXT NOT NULL,
                payload_json TEXT NOT NULL
            );
            """);
    }

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = StoragePath,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }
}
