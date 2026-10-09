using Microsoft.Data.Sqlite;
using Hushtype.Core;

namespace Hushtype.Data;

public sealed class LocalStore(string path)
{
    private readonly SemaphoreSlim gate = new(1);
    // Microsoft.Data.Sqlite executes synchronously. Move all database work off the UI thread.
    private async Task<T> Run<T>(Func<SqliteConnection, T> action)
    {
        await gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
                db.Open(); return action(db);
            });
        }
        finally { gate.Release(); }
    }
    public Task InitializeAsync() => Run(db =>
    {
        using var command = db.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS history(id INTEGER PRIMARY KEY, created_at TEXT NOT NULL, app TEXT NOT NULL,
                raw_text TEXT NOT NULL, final_text TEXT NOT NULL, duration_ms REAL NOT NULL, stt_ms REAL NOT NULL, cleanup_ms REAL NOT NULL);
            CREATE TABLE IF NOT EXISTS dictionary(heard TEXT PRIMARY KEY COLLATE NOCASE, replacement TEXT NOT NULL);
            PRAGMA user_version=1;
            """;
        return command.ExecuteNonQuery();
    });
    public Task AddHistoryAsync(DictationRecord record) => Run(db =>
    {
        using var transaction = db.BeginTransaction();
        using var command = db.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO history(created_at,app,raw_text,final_text,duration_ms,stt_ms,cleanup_ms) VALUES($at,$app,$raw,$final,$duration,$stt,$cleanup)";
        command.Parameters.AddWithValue("$at", record.CreatedAt.ToString("O")); command.Parameters.AddWithValue("$app", record.App);
        command.Parameters.AddWithValue("$raw", record.RawText); command.Parameters.AddWithValue("$final", record.FinalText);
        command.Parameters.AddWithValue("$duration", record.DurationMs); command.Parameters.AddWithValue("$stt", record.SttMs); command.Parameters.AddWithValue("$cleanup", record.CleanupMs);
        command.ExecuteNonQuery();
        command.CommandText = "DELETE FROM history WHERE id NOT IN (SELECT id FROM history ORDER BY id DESC LIMIT 500)";
        command.Parameters.Clear(); command.ExecuteNonQuery(); transaction.Commit(); return 0;
    });
    public Task<List<HistoryEntry>> HistoryAsync() => Run(db =>
    {
        using var command = db.CreateCommand(); command.CommandText = "SELECT id,created_at,app,raw_text,final_text,duration_ms,stt_ms,cleanup_ms FROM history ORDER BY id DESC LIMIT 500";
        using var reader = command.ExecuteReader(); var items = new List<HistoryEntry>();
        while (reader.Read()) items.Add(new(reader.GetInt64(0), new(DateTimeOffset.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7))));
        return items;
    });
    public Task DeleteHistoryAsync(long? id = null) => Run(db =>
    {
        using var command = db.CreateCommand(); command.CommandText = id is null ? "DELETE FROM history" : "DELETE FROM history WHERE id=$id";
        if (id is not null) command.Parameters.AddWithValue("$id", id.Value);
        return command.ExecuteNonQuery();
    });
    public Task<List<DictionaryCorrection>> DictionaryAsync() => Run(db =>
    {
        using var command = db.CreateCommand(); command.CommandText = "SELECT heard,replacement FROM dictionary ORDER BY length(heard) DESC,heard";
        using var reader = command.ExecuteReader(); var items = new List<DictionaryCorrection>();
        while (reader.Read()) items.Add(new(reader.GetString(0), reader.GetString(1))); return items;
    });
    public Task SaveCorrectionAsync(DictionaryCorrection correction) => Run(db =>
    {
        if (string.IsNullOrWhiteSpace(correction.Heard) || string.IsNullOrWhiteSpace(correction.Replacement) || correction.Heard.Length > 200 || correction.Replacement.Length > 200)
            throw new ArgumentException("Enter both phrases, up to 200 characters each.");
        using var command = db.CreateCommand(); command.CommandText = "INSERT INTO dictionary(heard,replacement) VALUES($heard,$replacement) ON CONFLICT(heard) DO UPDATE SET replacement=excluded.replacement";
        command.Parameters.AddWithValue("$heard", correction.Heard.Trim()); command.Parameters.AddWithValue("$replacement", correction.Replacement.Trim()); return command.ExecuteNonQuery();
    });
    public Task DeleteCorrectionAsync(string heard) => Run(db =>
    {
        using var command = db.CreateCommand(); command.CommandText = "DELETE FROM dictionary WHERE heard=$heard"; command.Parameters.AddWithValue("$heard", heard); return command.ExecuteNonQuery();
    });
}
