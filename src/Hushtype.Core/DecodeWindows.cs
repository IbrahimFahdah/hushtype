namespace Hushtype.Core;

// Long audio is decoded in bounded windows whose transcripts TranscriptMerger joins.
public static class DecodeWindows
{
    public const int Window = 20 * 16000;
    public const int Overlap = 16000;
    public static List<(int Start, int End)> Fixed(int length)
    {
        var windows = new List<(int, int)>();
        for (var start = 0; ; start += Window - Overlap)
        {
            var end = Math.Min(start + Window, length);
            windows.Add((start, end));
            if (end == length) return windows;
        }
    }
    // Cuts at the quietest 20 ms frame in the last few seconds before each boundary, so cuts
    // tend to land between words; each side keeps a small margin past the cut.
    public static List<(int Start, int End)> AtPauses(float[] samples, int searchSamples = 3 * 16000, int margin = 4000)
    {
        const int frame = 320;
        var windows = new List<(int, int)>();
        var start = 0;
        while (true)
        {
            var end = Math.Min(start + Window, samples.Length);
            if (end == samples.Length) { windows.Add((start, end)); return windows; }
            int cut = end - margin, quietest = int.MaxValue;
            double best = double.MaxValue;
            for (var offset = end - margin - frame; offset >= end - searchSamples; offset -= frame)
            {
                double sum = 0;
                for (var i = offset; i < offset + frame; i++) sum += samples[i] * samples[i];
                if (sum < best) { best = sum; quietest = offset; }
            }
            if (quietest != int.MaxValue) cut = quietest + frame / 2;
            windows.Add((start, Math.Min(samples.Length, cut + margin)));
            start = cut - margin;
        }
    }
}
