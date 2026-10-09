using Hushtype.Core;

namespace Hushtype.Cleanup;

// Plain-text dictionary backup: "heard -> replacement", "# off: heard -> replacement" for
// disabled entries, other "#" lines are comments.
public static class DictionaryText
{
    private const string Arrow = "->", Off = "# off:";
    public static string Format(IEnumerable<DictionaryCorrection> entries)
    {
        var lines = new List<string> { "# Hushtype dictionary: heard -> replacement", "# Lines starting with \"# off:\" are disabled corrections." };
        lines.AddRange(entries.Select(x => (x.Enabled ? "" : Off + " ") + x.Heard + " " + Arrow + " " + x.Replacement));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
    public static List<DictionaryCorrection> Parse(string text)
    {
        var entries = new List<DictionaryCorrection>();
        var number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            var line = raw.Trim();
            var enabled = true;
            if (line.StartsWith(Off, StringComparison.OrdinalIgnoreCase)) { enabled = false; line = line[Off.Length..].Trim(); }
            else if (line.Length == 0 || line.StartsWith('#')) continue;
            var arrow = line.IndexOf(Arrow, StringComparison.Ordinal);
            if (arrow < 0) throw new FormatException($"Line {number}: expected \"heard -> replacement\".");
            var heard = line[..arrow].Trim(); var replacement = line[(arrow + Arrow.Length)..].Trim();
            if (heard.Length == 0 || replacement.Length == 0) throw new FormatException($"Line {number}: both sides of \"->\" are needed.");
            entries.Add(new(heard, replacement, enabled));
        }
        return entries;
    }
}
