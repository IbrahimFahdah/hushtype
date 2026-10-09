using System.Text.Json;
using Hushtype.Core;

namespace Hushtype.Data;

public sealed class SettingsStore(string path)
{
    private readonly SemaphoreSlim gate = new(1);
    public async Task<AppSettings> LoadAsync(string? legacyPath = null)
    {
        if (File.Exists(path))
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(path)) ?? throw new InvalidDataException("Settings are empty.");
            settings.Validate(); return settings;
        }
        var result = new AppSettings();
        if (legacyPath is not null && File.Exists(legacyPath))
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(legacyPath));
            if (document.RootElement.TryGetProperty("Sounds", out var sounds)) result = result with { Sounds = sounds.GetBoolean() };
        }
        return result;
    }
    public async Task SaveAsync(AppSettings settings)
    {
        settings.Validate();
        await gate.WaitAsync();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); gate.Release(); }
    }
}
