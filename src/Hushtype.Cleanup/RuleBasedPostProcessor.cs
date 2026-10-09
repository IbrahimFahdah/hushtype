using System.Text.RegularExpressions;
using Hushtype.Core;

namespace Hushtype.Cleanup;

public sealed class RuleBasedPostProcessor : ITextPostProcessor
{
    public Task<CleanedText> ProcessAsync(string raw, TargetApp context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new CleanedText(Clean(raw), []));
    }
    public static string Clean(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var text = Regex.Replace(raw, @"\b(?:um|uh|erm)\b[, ]*", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(^|[.!?,;:]\s*)you know\b[, ]*", "$1", RegexOptions.IgnoreCase);
        // Letters only: repeated digits ("1 1 2", PINs) are content. Some words repeat legitimately.
        text = Regex.Replace(text, @"\b(?!(?:had|that|bye|very|no|so|really)\b)(\p{L}[\p{L}']*)(?:\s+\1\b)+", "$1", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        text = Regex.Replace(text, @"\s+([,.!?;:،؟])", "$1").Trim(' ', ',');
        if (!text.Any(char.IsLetterOrDigit)) return "";
        text = char.ToUpperInvariant(text[0]) + text[1..];
        if (!".!?؟…".Contains(text[^1])) text += ".";
        return text;
    }
}
