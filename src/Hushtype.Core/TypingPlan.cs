namespace Hushtype.Core;

// Direct typing sends KEYEVENTF_UNICODE input; newlines become a real Enter key.
public static class TypingPlan
{
    public const string Enter = "\n";
    public const int MaximumTypedLength = 200;
    // Short single-line text is typed; anything longer is pasted.
    public static bool ShouldType(string text) => text.Length <= MaximumTypedLength && text.IndexOfAny(['\r', '\n']) < 0;
    public static List<string> Split(string text, int chunk = 40)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        void Flush() { if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); } }
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                Flush(); parts.Add(Enter); continue;
            }
            // Never end a chunk between the halves of a surrogate pair.
            if (current.Length >= chunk && !char.IsLowSurrogate(c)) Flush();
            current.Append(c);
        }
        Flush();
        return parts;
    }
}
