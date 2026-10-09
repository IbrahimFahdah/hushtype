namespace Hushtype.Core;

// Adjacent 20-second decoder windows share one second of audio. Remove only
// exact word overlaps; keep the original casing and punctuation in the result.
public static class TranscriptMerger
{
    public static string Append(string previous, string next)
    {
        if (string.IsNullOrWhiteSpace(previous)) return next.Trim();
        if (string.IsNullOrWhiteSpace(next)) return previous;
        var left = previous.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var right = next.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var overlap = 0;
        for (var count = Math.Min(20, Math.Min(left.Length, right.Length)); count > 0; count--)
        {
            var matches = true;
            for (var i = 0; i < count; i++)
            {
                var a = Normalize(left[left.Length - count + i]);
                var b = Normalize(right[i]);
                if (a.Length == 0 || !a.Equals(b, StringComparison.OrdinalIgnoreCase)) { matches = false; break; }
            }
            if (matches) { overlap = count; break; }
        }
        return string.Join(" ", left.Concat(right.Skip(overlap)));
    }
    private static string Normalize(string value) => new(value.Where(c => char.IsLetterOrDigit(c) || c == 39).ToArray());
}
