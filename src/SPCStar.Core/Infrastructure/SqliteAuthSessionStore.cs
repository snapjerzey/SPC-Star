using Microsoft.Data.Sqlite;

namespace SPCStar.Core.Infrastructure;

// Authentication state is separate from inspection backups and setup imports.
public sealed class SqliteAuthSessionStore : IAuthSessionStore
{
    private readonly string connectionString;

    public SqliteAuthSessionStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS auth_sessions (
                token_hash TEXT PRIMARY KEY,
                user_name TEXT NOT NULL COLLATE NOCASE,
                credential_hash TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public void Save(string tokenHash, string userName, string credentialHash)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO auth_sessions (token_hash,user_name,credential_hash) VALUES ($token,$user,$credential);";
        command.Parameters.AddWithValue("$token", tokenHash);
        command.Parameters.AddWithValue("$user", userName);
        command.Parameters.AddWithValue("$credential", credentialHash);
        command.ExecuteNonQuery();
    }

    public bool Contains(string tokenHash, string userName, string credentialHash)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM auth_sessions WHERE token_hash=$token AND user_name=$user AND credential_hash=$credential;";
        command.Parameters.AddWithValue("$token", tokenHash);
        command.Parameters.AddWithValue("$user", userName);
        command.Parameters.AddWithValue("$credential", credentialHash);
        return command.ExecuteScalar() is not null;
    }

    public void Remove(string tokenHash)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM auth_sessions WHERE token_hash=$token;";
        command.Parameters.AddWithValue("$token", tokenHash);
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}
