using Hushtype.Core;
using Hushtype.Data;
using Hushtype.Cleanup;

var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
var folder = Path.Combine(Path.GetTempPath(), "hushtype-data-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var settingsPath = Path.Combine(folder, "settings.json");
    var store = new SettingsStore(settingsPath);
    var legacy = Path.Combine(folder, "ui-preferences.json");
    await File.WriteAllTextAsync(legacy, "{\"Sounds\":false}");
    Check(!(await store.LoadAsync(legacy)).Sounds, "Migrate legacy sound toggle");
    Check((await store.LoadAsync()).Hotkey == 0xA3, "New settings default to Right Ctrl");
    await File.WriteAllTextAsync(settingsPath, "{\"Hotkey\":165}");
    Check((await store.LoadAsync()).Hotkey == 0xA5, "Saved Right Alt choice is kept");
    File.Delete(settingsPath);
    var settings = new AppSettings { Hotkey = 0x78, Sounds = false, SaveHistory = true, Threads = 8 };
    await store.SaveAsync(settings);
    Check(await new SettingsStore(settingsPath).LoadAsync() == settings, "Settings survive restart");
    try { await store.SaveAsync(settings with { Threads = 0 }); throw new Exception("Invalid settings accepted"); } catch (ArgumentException) { checks++; }
    Check(await store.LoadAsync() == settings, "Invalid save preserves existing settings");
    Check(Directory.GetFiles(folder, "*.tmp").Length == 0, "No partial settings left");
    await File.WriteAllTextAsync(settingsPath, "invalid json");
    try { await store.LoadAsync(); throw new Exception("Corruption ignored"); } catch (System.Text.Json.JsonException) { checks++; }

    var dbPath = Path.Combine(folder, "hushtype.db"); var db = new LocalStore(dbPath); await db.InitializeAsync();
    var record = new DictationRecord(DateTimeOffset.UtcNow, "test.exe", "O'Brien says $hello", "O'Brien says $hello.", 1000, 50, 1);
    await db.AddHistoryAsync(record);
    var history = await db.HistoryAsync();
    Check(history.Count == 1 && history[0].Record == record, "History parameterization and round trip");
    await new LocalStore(dbPath).InitializeAsync();
    Check((await new LocalStore(dbPath).HistoryAsync()).Count == 1, "Schema initialization preserves history");
    await db.DeleteHistoryAsync(history[0].Id); Check((await db.HistoryAsync()).Count == 0, "Delete selected history");
    for (var i = 0; i < 501; i++) await db.AddHistoryAsync(record with { FinalText = i.ToString() });
    history = await db.HistoryAsync();
    Check(history.Count == 500 && history[0].Record.FinalText == "500" && history[^1].Record.FinalText == "1", "History retains newest 500");
    await db.DeleteHistoryAsync(); Check((await db.HistoryAsync()).Count == 0, "Clear history");
    await db.SaveCorrectionAsync(new("cube cuddle", "kubectl"));
    await db.SaveCorrectionAsync(new("CUBE CUDDLE", "kubectl"));
    Check((await db.DictionaryAsync()).Count == 1, "Case-insensitive dictionary upsert");
    await db.SaveCorrectionAsync(new("open ai", "OpenAI"));
    await db.SaveCorrectionAsync(new("open", "opened"));
    var cleaner = new PersonalPostProcessor(); cleaner.SetCorrections(await db.DictionaryAsync());
    var target = new TargetApp("test", "", 0);
    Check((await cleaner.ProcessAsync("use cube cuddle with open ai", target, default)).Text == "Use kubectl with OpenAI.", "Name correction and longest phrase wins");
    cleaner.SetCorrections([new("cat", "dog"), new("dog", "wolf"), new("c++", "C++")]); cleaner.UseRules = false;
    Check((await cleaner.ProcessAsync("cat scatter dog c++", target, default)).Text == "dog scatter wolf C++", "Whole words, literal punctuation, no cascading");
    Check((await cleaner.ProcessAsync("um raw text", target, default)).Text == "um raw text", "Cleanup off preserves raw wording");
    await db.DeleteCorrectionAsync("OPEN AI"); Check((await db.DictionaryAsync()).Count == 2, "Delete dictionary case-insensitively");

    // Version 1 databases gain the new columns without losing rows.
    var oldPath = Path.Combine(folder, "v1.db");
    using (var old = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={oldPath};Pooling=False"))
    {
        old.Open(); using var command = old.CreateCommand();
        command.CommandText = """
            CREATE TABLE history(id INTEGER PRIMARY KEY, created_at TEXT NOT NULL, app TEXT NOT NULL, raw_text TEXT NOT NULL, final_text TEXT NOT NULL, duration_ms REAL NOT NULL, stt_ms REAL NOT NULL, cleanup_ms REAL NOT NULL);
            CREATE TABLE dictionary(heard TEXT PRIMARY KEY COLLATE NOCASE, replacement TEXT NOT NULL);
            INSERT INTO dictionary VALUES('cube cuddle','kubectl');
            INSERT INTO history(created_at,app,raw_text,final_text,duration_ms,stt_ms,cleanup_ms) VALUES('2026-01-01T00:00:00+00:00','a.exe','raw','Final.',1,1,1);
            PRAGMA user_version=1;
            """;
        command.ExecuteNonQuery();
    }
    var migrated = new LocalStore(oldPath); await migrated.InitializeAsync(); await migrated.InitializeAsync();
    Check((await migrated.DictionaryAsync()).Single() == new DictionaryCorrection("cube cuddle", "kubectl", true), "Migration keeps corrections, enabled by default");
    Check((await migrated.HistoryAsync()).Single().Record.Corrections is null, "Migration keeps history");

    // Enable/disable without restart.
    await db.SaveCorrectionAsync(new("cube cuddle", "kubectl"));
    await db.SetCorrectionEnabledAsync("CUBE CUDDLE", false);
    var listed = await db.DictionaryAsync();
    Check(listed.Any(x => x.Heard == "cube cuddle" && !x.Enabled), "Disabled correction stays listed");
    cleaner = new PersonalPostProcessor { UseRules = false }; cleaner.SetCorrections(listed);
    Check((await cleaner.ProcessAsync("cube cuddle", target, default)).Text == "cube cuddle", "Disabled correction not applied");
    await db.SetCorrectionEnabledAsync("cube cuddle", true); cleaner.SetCorrections(await db.DictionaryAsync());
    Check((await cleaner.ProcessAsync("cube cuddle", target, default)).Text == "kubectl", "Re-enabled correction applies");

    // Matching: run-together and hyphenated forms, NFC, boundaries, literal replacement, report.
    cleaner.SetCorrections([new("cube cuddle", "kubectl"), new("café latte", "Café Latte"), new("cost", "$0 cost"), new("cube", "box")]);
    var result = await cleaner.ProcessAsync("cubecuddle, cube-cuddle and Cube  Cuddle", target, default);
    Check(result.Text == "kubectl, kubectl and kubectl", "Run-together, hyphenated and spaced triggers");
    Check(result.Corrections.Count == 3 && result.Corrections.All(x => x.Replacement == "kubectl" && x.Count == 1) && result.Corrections[1].Matched == "cube-cuddle", "Report lists each matched form");
    Check((await cleaner.ProcessAsync("cube cube cuddle", target, default)).Corrections.Sum(x => x.Count) == 2, "Report counts every replacement");
    Check((await cleaner.ProcessAsync("cubes cube", target, default)).Text == "cubes box", "Whole-word boundaries kept");
    Check((await cleaner.ProcessAsync("café latte", target, default)).Text == "Café Latte", "Decomposed text matches composed trigger");
    cleaner.SetCorrections([new("café", "coffee")]);
    Check((await cleaner.ProcessAsync("café", target, default)).Text == "coffee", "Decomposed trigger matches composed text");
    cleaner.SetCorrections([new("cost", "$0 cost"), new("box", "cost")]);
    var literal = await cleaner.ProcessAsync("box cost", target, default);
    Check(literal.Text == "cost $0 cost" && literal.Corrections.Count == 2, "Literal replacement, single pass");
    Check((await cleaner.ProcessAsync("nothing here", target, default)).Corrections.Count == 0, "No corrections reported when none fire");

    // Applied corrections round-trip with history; search is case-insensitive, Unicode-aware and literal.
    await db.DeleteHistoryAsync();
    await db.AddHistoryAsync(record with { App = "Code.exe", RawText = "use cube cuddle", FinalText = "Use kubectl.", Corrections = [new("cube cuddle", "kubectl", 1)] });
    await db.AddHistoryAsync(record with { App = "notepad.exe", RawText = "über 100% done", FinalText = "Über 100% done_now." });
    var saved = (await db.HistoryAsync()).Single(x => x.Record.App == "Code.exe").Record.Corrections;
    Check(saved is { Count: 1 } && saved[0] == new AppliedCorrection("cube cuddle", "kubectl", 1), "Applied corrections saved with history");
    Check((await db.HistoryAsync("KUBECTL")).Count == 1, "History search matches final text, any case");
    Check((await db.HistoryAsync("cube cud")).Count == 1, "History search matches raw text");
    Check((await db.HistoryAsync("NOTEPAD")).Count == 1, "History search matches app name");
    Check((await db.HistoryAsync("ÜBER")).Count == 1, "History search folds non-ASCII case");
    Check((await db.HistoryAsync("100%")).Count == 1 && (await db.HistoryAsync("%")).Count == 1 && (await db.HistoryAsync("_now")).Count == 1 && (await db.HistoryAsync("%d")).Count == 0, "Wildcards are literal");
    Check((await db.HistoryAsync("  ")).Count == 2 && (await db.HistoryAsync("zzz")).Count == 0, "Blank search lists all; misses list none");
    Check((await db.DictionaryAsync("KUBE")).Count == 1 && (await db.DictionaryAsync("cuddle")).Count == 1 && (await db.DictionaryAsync("zzz")).Count == 0, "Dictionary search matches heard and replacement");

    // Trigger warnings never block saving.
    Check(CorrectionWarnings.For("the", "thee").Count == 2, "Common short word warns twice");
    Check(CorrectionWarnings.For("kubectl", "kubectl").Count == 1, "Identical replacement warns");
    Check(CorrectionWarnings.For("github", "GitHub").Count == 0 && CorrectionWarnings.For("cube cuddle", "kubectl").Count == 0, "Case fixes and names do not warn");

    // Import/export.
    var exported = DictionaryText.Format([new("cube cuddle", "kubectl"), new("open ai", "OpenAI", false)]);
    var imported = DictionaryText.Parse(exported + "\n# a comment\n\n  sequel  ->  SQL \r\n");
    Check(imported.Count == 3 && imported[1] == new DictionaryCorrection("open ai", "OpenAI", false) && imported[2] == new DictionaryCorrection("sequel", "SQL"), "Dictionary export round-trips; comments skipped");
    try { DictionaryText.Parse("ok -> fine\nbroken line"); throw new Exception("Bad import accepted"); } catch (FormatException e) when (e.Message.StartsWith("Line 2")) { checks++; }
    await db.SaveCorrectionsAsync(imported);
    Check((await db.DictionaryAsync()).Count(x => !x.Enabled) == 1, "Imported disabled entries stay disabled");
    Console.WriteLine($"PASS: {checks} settings, history and dictionary checks.");
}
finally { Directory.Delete(folder, recursive: true); }
