namespace Hushtype.Core;

public static class SilenceTrimmer
{
    // The gate follows the recording's own noise floor so quiet but clean microphones still
    // pass, without ever exceeding the original fixed 0.006 RMS threshold.
    public static float Threshold(float[] samples)
    {
        var frames = AudioSignal.FrameRms(samples).Order().ToArray();
        if (frames.Length == 0) return 0.006f;
        return Math.Clamp(frames[frames.Length / 10] * 4, 0.0015f, 0.006f);
    }
    public static float[] Trim(float[] samples, float? rmsThreshold = null)
    {
        const int frame = 320; // 20 ms at 16 kHz
        var threshold = rmsThreshold ?? Threshold(samples);
        var first = -1;
        var last = 0;
        var voicedFrames = 0;
        var offset = 0;
        foreach (var rms in AudioSignal.FrameRms(samples))
        {
            var end = Math.Min(offset + frame, samples.Length);
            if (rms >= threshold)
            {
                if (first < 0) first = offset;
                last = end;
                voicedFrames++;
            }
            offset = end;
        }
        if (first < 0 || voicedFrames < 3) return [];
        first = Math.Max(0, first - 1600);
        last = Math.Min(samples.Length, last + 2400);
        return samples[first..last];
    }
}
