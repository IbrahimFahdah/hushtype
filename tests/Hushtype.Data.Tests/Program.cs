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
    Check(await cleaner.ProcessAsync("use cube cuddle with open ai", target, default) == "Use kubectl with OpenAI.", "Name correction and longest phrase wins");
    cleaner.SetCorrections([new("cat", "dog"), new("dog", "wolf"), new("c++", "C++")]); cleaner.UseRules = false;
    Check(await cleaner.ProcessAsync("cat scatter dog c++", target, default) == "dog scatter wolf C++", "Whole words, literal punctuation, no cascading");
    Check(await cleaner.ProcessAsync("um raw text", target, default) == "um raw text", "Cleanup off preserves raw wording");
    await db.DeleteCorrectionAsync("OPEN AI"); Check((await db.DictionaryAsync()).Count == 2, "Delete dictionary case-insensitively");
    Console.WriteLine($"PASS: {checks} settings, history and dictionary checks.");
}
finally { Directory.Delete(folder, recursive: true); }
