namespace Hushtype.Cleanup;

// Advice shown when saving a correction; never blocks the save.
public static class CorrectionWarnings
{
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "about", "after", "again", "all", "also", "am", "an", "and", "any", "are", "as", "at", "back", "be", "because", "been", "before",
        "being", "but", "by", "can", "could", "day", "did", "do", "does", "done", "down", "each", "even", "first", "for", "from", "get", "give",
        "go", "good", "great", "had", "has", "have", "he", "her", "here", "him", "his", "how", "i", "if", "in", "into", "is", "it", "its", "just",
        "know", "last", "like", "little", "long", "look", "make", "man", "many", "may", "me", "more", "most", "much", "must", "my", "new", "no",
        "not", "now", "of", "off", "old", "on", "one", "only", "or", "other", "our", "out", "over", "own", "people", "right", "said", "same",
        "say", "see", "she", "should", "so", "some", "still", "such", "take", "than", "that", "the", "their", "them", "then", "there", "these",
        "they", "thing", "think", "this", "those", "through", "time", "to", "too", "two", "under", "up", "us", "use", "very", "want", "was",
        "way", "we", "well", "were", "what", "when", "where", "which", "while", "who", "why", "will", "with", "work", "would", "year", "yes",
        "you", "your", "call", "come", "find", "help", "keep", "let", "need", "open", "part", "put", "run", "set", "show", "start", "tell",
        "try", "turn", "test", "file", "line", "code", "next", "end", "home", "high", "low", "big", "small", "few", "every", "never", "always",
    };
    public static IReadOnlyList<string> For(string heard, string replacement)
    {
        heard = heard.Trim(); replacement = replacement.Trim();
        var warnings = new List<string>();
        if (heard == replacement) warnings.Add("The replacement is identical to the recognized phrase, so this correction changes nothing.");
        if (heard.Length <= 3) warnings.Add("Very short phrases can match more often than intended.");
        if (!heard.Contains(' ') && Common.Contains(heard)) warnings.Add($"\"{heard}\" is a common English word; it will be replaced everywhere it appears.");
        return warnings;
    }
}
