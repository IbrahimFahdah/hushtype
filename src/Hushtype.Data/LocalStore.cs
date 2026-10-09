using System.Text.Json;
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
                db.Open();
                // Unicode-aware case folding for search; SQLite's LIKE only folds ASCII.
                db.CreateFunction("fold", (string? text) => text?.ToLowerInvariant());
                return action(db);
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
                raw_text TEXT NOT NULL, final_text TEXT NOT NULL, duration_ms REAL NOT NULL, stt_ms REAL NOT NULL, cleanup_ms REAL NOT NULL, corrections TEXT);
            CREATE TABLE IF NOT EXISTS dictionary(heard TEXT PRIMARY KEY COLLATE NOCASE, replacement TEXT NOT NULL, enabled INTEGER NOT NULL DEFAULT 1);
            """;
        command.ExecuteNonQuery();
        // Version 1 databases lack these columns.
        using var transaction = db.BeginTransaction(); command.Transaction = transaction;
        bool Has(string table, string column)
        {
            command.CommandText = $"SELECT count(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
            return (long)command.ExecuteScalar()! > 0;
        }
        if (!Has("dictionary", "enabled")) { command.CommandText = "ALTER TABLE dictionary ADD COLUMN enabled INTEGER NOT NULL DEFAULT 1"; command.ExecuteNonQuery(); }
        if (!Has("history", "corrections")) { command.CommandText = "ALTER TABLE history ADD COLUMN corrections TEXT"; command.ExecuteNonQuery(); }
        command.CommandText = "PRAGMA user_version=2"; command.ExecuteNonQuery();
        transaction.Commit(); return 0;
    });
    public Task AddHistoryAsync(DictationRecord record) => Run(db =>
    {
        using var transaction = db.BeginTransaction();
        using var command = db.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO history(created_at,app,raw_text,final_text,duration_ms,stt_ms,cleanup_ms,corrections) VALUES($at,$app,$raw,$final,$duration,$stt,$cleanup,$corrections)";
        command.Parameters.AddWithValue("$corrections", record.Corrections is { Count: > 0 } applied ? JsonSerializer.Serialize(applied) : DBNull.Value);
        command.Parameters.AddWithValue("$at", record.CreatedAt.ToString("O")); command.Parameters.AddWithValue("$app", record.App);
        command.Parameters.AddWithValue("$raw", record.RawText); command.Parameters.AddWithValue("$final", record.FinalText);
        command.Parameters.AddWithValue("$duration", record.DurationMs); command.Parameters.AddWithValue("$stt", record.SttMs); command.Parameters.AddWithValue("$cleanup", record.CleanupMs);
        command.ExecuteNonQuery();
        command.CommandText = "DELETE FROM history WHERE id NOT IN (SELECT id FROM history ORDER BY id DESC LIMIT 500)";
        command.Parameters.Clear(); command.ExecuteNonQuery(); transaction.Commit(); return 0;
    });
    public Task<List<HistoryEntry>> HistoryAsync(string? search = null) => Run(db =>
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT id,created_at,app,raw_text,final_text,duration_ms,stt_ms,cleanup_ms,corrections FROM history"
            + (Search(command, search) ? " WHERE instr(fold(final_text),$q) OR instr(fold(raw_text),$q) OR instr(fold(app),$q)" : "") + " ORDER BY id DESC LIMIT 500";
        using var reader = command.ExecuteReader(); var items = new List<HistoryEntry>();
        while (reader.Read()) items.Add(new(reader.GetInt64(0), new(DateTimeOffset.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7),
            reader.IsDBNull(8) ? null : JsonSerializer.Deserialize<List<AppliedCorrection>>(reader.GetString(8)))));
        return items;
    });
    private static bool Search(SqliteCommand command, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return false;
        command.Parameters.AddWithValue("$q", search.Trim().ToLowerInvariant()); return true;
    }
    public Task DeleteHistoryAsync(long? id = null) => Run(db =>
    {
        using var command = db.CreateCommand(); command.CommandText = id is null ? "DELETE FROM history" : "DELETE FROM history WHERE id=$id";
        if (id is not null) command.Parameters.AddWithValue("$id", id.Value);
        return command.ExecuteNonQuery();
    });
    public Task<List<DictionaryCorrection>> DictionaryAsync(string? search = null) => Run(db =>
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT heard,replacement,enabled FROM dictionary" + (Search(command, search) ? " WHERE instr(fold(heard),$q) OR instr(fold(replacement),$q)" : "") + " ORDER BY length(heard) DESC,heard";
        using var reader = command.ExecuteReader(); var items = new List<DictionaryCorrection>();
        while (reader.Read()) items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt64(2) != 0)); return items;
    });
    public Task SetCorrectionEnabledAsync(string heard, bool enabled) => Run(db =>
    {
        using var command = db.CreateCommand(); command.CommandText = "UPDATE dictionary SET enabled=$enabled WHERE heard=$heard";
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0); command.Parameters.AddWithValue("$heard", heard); return command.ExecuteNonQuery();
    });
    public Task SaveCorrectionAsync(DictionaryCorrection correction) => SaveCorrectionsAsync([correction]);
    public Task SaveCorrectionsAsync(IReadOnlyList<DictionaryCorrection> corrections) => Run(db =>
    {
        foreach (var correction in corrections)
            if (string.IsNullOrWhiteSpace(correction.Heard) || string.IsNullOrWhiteSpace(correction.Replacement) || correction.Heard.Trim().Length > 200 || correction.Replacement.Trim().Length > 200)
                throw new ArgumentException("Enter both phrases, up to 200 characters each.");
        using var transaction = db.BeginTransaction();
        using var command = db.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO dictionary(heard,replacement,enabled) VALUES($heard,$replacement,$enabled) ON CONFLICT(heard) DO UPDATE SET replacement=excluded.replacement,enabled=excluded.enabled";
        foreach (var correction in corrections)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$heard", correction.Heard.Trim()); command.Parameters.AddWithValue("$replacement", correction.Replacement.Trim());
            command.Parameters.AddWithValue("$enabled", correction.Enabled ? 1 : 0); command.ExecuteNonQuery();
        }
        transaction.Commit(); return corrections.Count;
    });
    public Task DeleteCorrectionAsync(string heard) => Run(db =>
    {
        using var command = db.CreateCommand(); command.CommandText = "DELETE FROM dictionary WHERE heard=$heard"; command.Parameters.AddWithValue("$heard", heard); return command.ExecuteNonQuery();
    });
}
