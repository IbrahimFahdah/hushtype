namespace Hushtype.Core;

public static class SilenceTrimmer
{
    public static float[] Trim(float[] samples, float rmsThreshold = 0.006f)
    {
        const int frame = 320; // 20 ms at 16 kHz
        var first = -1;
        var last = 0;
        var voicedFrames = 0;
        for (var offset = 0; offset < samples.Length; offset += frame)
        {
            var end = Math.Min(offset + frame, samples.Length);
            double sum = 0;
            for (var i = offset; i < end; i++) sum += samples[i] * samples[i];
            if (Math.Sqrt(sum / (end - offset)) < rmsThreshold) continue;
            if (first < 0) first = offset;
            last = end;
            voicedFrames++;
        }
        if (first < 0 || voicedFrames < 3) return [];
        first = Math.Max(0, first - 1600);
        last = Math.Min(samples.Length, last + 2400);
        return samples[first..last];
    }
}
