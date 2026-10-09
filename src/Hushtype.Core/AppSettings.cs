namespace Hushtype.Core;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public int Hotkey { get; init; } = 0xA5;
    public string Microphone { get; init; } = "default";
    public string Precision { get; init; } = "int8";
    public int Threads { get; init; } = 4;
    public bool Sounds { get; init; } = true;
    public bool Cleanup { get; init; } = true;
    public bool SaveHistory { get; init; }
    public bool LaunchAtStartup { get; init; }
    public bool OnboardingComplete { get; init; }
    public bool AutomaticUpdates { get; init; }
    public static readonly IReadOnlyDictionary<int, string> Hotkeys = new Dictionary<int, string>
    { [0xA5] = "Right Alt", [0xA3] = "Right Ctrl", [0x77] = "F8", [0x78] = "F9", [0x79] = "F10" };
    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("Unsupported settings version.");
        if (!Hotkeys.ContainsKey(Hotkey)) throw new ArgumentException("Choose a supported hotkey.");
        if (string.IsNullOrWhiteSpace(Microphone)) throw new ArgumentException("Choose a microphone.");
        if (Precision is not ("int8" or "fp32")) throw new ArgumentException("Choose INT8 or FP32.");
        if (Threads is < 1 or > 20) throw new ArgumentException("CPU threads must be between 1 and 20.");
    }
}

public sealed record DictionaryCorrection(string Heard, string Replacement);
public sealed record DictationRecord(DateTimeOffset CreatedAt, string App, string RawText, string FinalText,
    double DurationMs, double SttMs, double CleanupMs);
public sealed record HistoryEntry(long Id, DictationRecord Record);
