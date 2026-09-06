using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Grindcrest.Live;
using Microsoft.Data.Sqlite;

namespace Grindcrest.Api;

/// <summary>One current session per publisher. Ended sessions retain a small tombstone.</summary>
public sealed class SessionStore
{
    public const int ExpirySeconds = 90;
    private readonly string _connectionString;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public SessionStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connectionString = new SqliteConnectionStringBuilder
        { DataSource = databasePath, DefaultTimeout = 5 }.ToString();
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS publishers (
                id TEXT PRIMARY KEY, token_hash TEXT NOT NULL UNIQUE, label TEXT NOT NULL,
                session_id TEXT, sequence INTEGER NOT NULL DEFAULT 0, started_at INTEGER,
                updated_at INTEGER, ended INTEGER NOT NULL DEFAULT 1, payload TEXT
            );
            CREATE TABLE IF NOT EXISTS ended_sessions (
                owner_id TEXT NOT NULL REFERENCES publishers(id) ON DELETE CASCADE,
                session_id TEXT NOT NULL, ended_at INTEGER NOT NULL,
                PRIMARY KEY(owner_id,session_id)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS unique_public_session ON publishers(session_id) WHERE session_id IS NOT NULL;
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON";
        command.ExecuteNonQuery();
        return db;
    }

    public (string Id, string Token) IssueKey(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var id = Guid.NewGuid().ToString("N");
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "INSERT INTO publishers(id,token_hash,label) VALUES($id,$hash,$label)";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$hash", Hash(token));
        command.Parameters.AddWithValue("$label", label);
        command.ExecuteNonQuery();
        return (id, token);
    }

    public bool RevokeKey(string id)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "DELETE FROM publishers WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    public string? Authenticate(string? token)
    {
        if (!LiveEndpoint.IsValidToken(token)) return null;
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT id FROM publishers WHERE token_hash=$hash";
        command.Parameters.AddWithValue("$hash", Hash(token!));
        return command.ExecuteScalar() as string;
    }

    public bool Upsert(string owner, LiveSessionUpdate session, DateTimeOffset now)
    {
        // A conditional atomic update rejects stale sequences, replays after end,
        // and delayed writes from a session superseded by a newer start.
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            UPDATE publishers SET session_id=$session, sequence=$sequence, started_at=$start,
                updated_at=$now, ended=0, payload=$payload
            WHERE id=$owner AND NOT EXISTS (
                SELECT 1 FROM ended_sessions WHERE owner_id=$owner AND session_id=$session
            ) AND NOT EXISTS (
                SELECT 1 FROM publishers WHERE id<>$owner AND session_id=$session
            ) AND (
                session_id IS NULL OR
                (session_id=$session AND ended=0 AND sequence<$sequence) OR
                (session_id<>$session AND started_at<$start)
            )
            """;
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$session", session.SessionId.ToString());
        command.Parameters.AddWithValue("$sequence", session.Sequence);
        command.Parameters.AddWithValue("$start", session.SharedAt.UtcTicks);
        command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(session, Json));
        return command.ExecuteNonQuery() > 0;
    }

    public void End(string owner, Guid sessionId, DateTimeOffset now)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO ended_sessions(owner_id,session_id,ended_at) VALUES($owner,$session,$now);
            UPDATE publishers SET ended=1,payload=NULL WHERE id=$owner AND session_id=$session;
            """;
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$session", sessionId.ToString());
        command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<PublicLiveSession> List(DateTimeOffset now)
    {
        using var db = Open();
        // Keep only the last ID/sequence/start as a replay guard, never a session history.
        using (var cleanup = db.CreateCommand())
        {
            cleanup.CommandText = """
                UPDATE publishers SET payload=NULL WHERE updated_at<=$cutoff AND payload IS NOT NULL;
                DELETE FROM ended_sessions WHERE ended_at<$old;
                """;
            cleanup.Parameters.AddWithValue("$cutoff", now.AddSeconds(-ExpirySeconds).ToUnixTimeMilliseconds());
            cleanup.Parameters.AddWithValue("$old", now.AddDays(-31).ToUnixTimeMilliseconds());
            cleanup.ExecuteNonQuery();
        }
        using var command = db.CreateCommand();
        command.CommandText = "SELECT payload,updated_at FROM publishers WHERE ended=0 AND payload IS NOT NULL AND updated_at>$cutoff ORDER BY updated_at DESC";
        command.Parameters.AddWithValue("$cutoff", now.AddSeconds(-ExpirySeconds).ToUnixTimeMilliseconds());
        using var reader = command.ExecuteReader();
        var result = new List<PublicLiveSession>();
        while (reader.Read())
        {
            var session = JsonSerializer.Deserialize<LiveSessionUpdate>(reader.GetString(0), Json)!;
            var updated = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1));
            result.Add(new(session, updated, updated.AddSeconds(ExpirySeconds)));
        }
        return result;
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
