using System.Text.RegularExpressions;
using Hushtype.Core;

namespace Hushtype.Cleanup;

public sealed class PersonalPostProcessor : ITextPostProcessor
{
    private sealed record CorrectionSet(Regex? Pattern, IReadOnlyDictionary<string, string> Replacements);
    private CorrectionSet corrections = new(null, new Dictionary<string, string>());
    public bool UseRules { get; set; } = true;
    public void SetCorrections(IEnumerable<DictionaryCorrection> entries)
    {
        var map = entries.ToDictionary(x => x.Heard, x => x.Replacement, StringComparer.OrdinalIgnoreCase);
        var pattern = map.Count == 0 ? null : new Regex(@"(?<![\p{L}\p{N}_])(?:" + string.Join("|", map.Keys.OrderByDescending(x => x.Length).Select(Regex.Escape)) + @")(?![\p{L}\p{N}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        Volatile.Write(ref corrections, new(pattern, map));
    }
    public Task<string> ProcessAsync(string raw, TargetApp context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var text = UseRules ? RuleBasedPostProcessor.Clean(raw) : raw.Trim();
        var snapshot = Volatile.Read(ref corrections);
        // One replacement pass prevents dictionary entries cascading into one another.
        if (snapshot.Pattern is not null) text = snapshot.Pattern.Replace(text, match => snapshot.Replacements[match.Value]);
        return Task.FromResult(text);
    }
}
