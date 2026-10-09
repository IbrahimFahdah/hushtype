using System.Text.Json;

namespace Hushtype.Speech;

public sealed record SpeechConfiguration(string Precision = "int8", int Threads = 4);
public sealed record ModelFile(string Name, long Size, string Sha256);
public sealed record SpeechModelSpec(string Name, string Precision, string Repository, string Revision, ModelFile[] Files, string Encoder, string Decoder, string Joiner);
public sealed record ModelProgress(string Message, double? Fraction = null);
public static class ModelCatalog
{
    private static readonly Dictionary<string, SpeechModelSpec> Models = Load();
    public static SpeechModelSpec Get(string precision) => Models.TryGetValue(precision, out var spec)
        ? spec : throw new ArgumentException("Speech precision must be fp32 or int8.", nameof(precision));
    private static Dictionary<string, SpeechModelSpec> Load()
    {
        using var stream = typeof(ModelCatalog).Assembly.GetManifestResourceStream("Hushtype.Speech.models.json")
            ?? throw new InvalidOperationException("Missing model manifest.");
        return JsonSerializer.Deserialize<Dictionary<string, SpeechModelSpec>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
