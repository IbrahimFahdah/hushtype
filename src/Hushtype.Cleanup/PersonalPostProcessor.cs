using System.Text;
using System.Text.RegularExpressions;
using Hushtype.Core;

namespace Hushtype.Cleanup;

public sealed class PersonalPostProcessor : ITextPostProcessor
{
    // Group i + 1 of the pattern belongs to Replacements[i].
    private sealed record CorrectionSet(Regex? Pattern, IReadOnlyList<string> Replacements);
    private CorrectionSet corrections = new(null, []);
    public bool UseRules { get; set; } = true;
    public void SetCorrections(IEnumerable<DictionaryCorrection> entries)
    {
        var active = entries.Where(x => x.Enabled)
            .Select(x => (Heard: x.Heard.Normalize(NormalizationForm.FormC), Replacement: x.Replacement.Normalize(NormalizationForm.FormC)))
            .Select(x => (x.Heard, x.Replacement, Parts: x.Heard.Split([' ', '\t', '-'], StringSplitOptions.RemoveEmptyEntries)))
            .Where(x => x.Parts.Length > 0)
            .DistinctBy(x => string.Join(' ', x.Parts), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Heard.Length).ToList();
        // Multi-word triggers also match run-together and hyphenated forms ("cubecuddle", "cube-cuddle").
        var pattern = active.Count == 0 ? null : new Regex(@"(?<![\p{L}\p{N}_])(?:" + string.Join("|", active.Select(x => "(" + string.Join(@"[\s\-]*", x.Parts.Select(Regex.Escape)) + ")")) + @")(?![\p{L}\p{N}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        Volatile.Write(ref corrections, new(pattern, active.Select(x => x.Replacement).ToList()));
    }
    public Task<CleanedText> ProcessAsync(string raw, TargetApp context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var text = (UseRules ? RuleBasedPostProcessor.Clean(raw) : raw.Trim()).Normalize(NormalizationForm.FormC);
        var snapshot = Volatile.Read(ref corrections);
        if (snapshot.Pattern is null) return Task.FromResult(new CleanedText(text, []));
        var applied = new List<(string Matched, string Replacement)>();
        // One replacement pass prevents dictionary entries cascading into one another.
        text = snapshot.Pattern.Replace(text, match =>
        {
            var index = 1;
            while (!match.Groups[index].Success) index++;
            var replacement = snapshot.Replacements[index - 1];
            applied.Add((match.Value, replacement));
            return replacement;
        });
        var report = applied.GroupBy(x => x).Select(g => new AppliedCorrection(g.Key.Matched, g.Key.Replacement, g.Count())).ToList();
        return Task.FromResult(new CleanedText(text, report));
    }
}
