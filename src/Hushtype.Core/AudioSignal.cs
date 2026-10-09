namespace Hushtype.Core;

public enum NothingHeardReason { Unknown, MicrophoneBlocked, TooQuiet }

public static class AudioSignal
{
    private const int Frame = 320; // 20 ms at 16 kHz
    // Real microphones have a noise floor; Windows privacy blocking delivers exact zeros.
    public const int BlockedRun = 24000; // 1.5 s
    public static int LongestZeroRun(float[] samples)
    {
        int longest = 0, current = 0;
        foreach (var sample in samples)
        {
            current = sample == 0f ? current + 1 : 0;
            if (current > longest) longest = current;
        }
        return longest;
    }
    public static IEnumerable<float> FrameRms(float[] samples)
    {
        for (var offset = 0; offset < samples.Length; offset += Frame)
        {
            var end = Math.Min(offset + Frame, samples.Length);
            double sum = 0;
            for (var i = offset; i < end; i++) sum += samples[i] * samples[i];
            yield return (float)Math.Sqrt(sum / (end - offset));
        }
    }
    // Called when a recording produced no transcript, to explain why if the audio shows it.
    public static NothingHeardReason Diagnose(float[] samples, TimeSpan held)
    {
        if (LongestZeroRun(samples) >= BlockedRun) return NothingHeardReason.MicrophoneBlocked;
        if (held >= TimeSpan.FromSeconds(1) && samples.Length > 0 && FrameRms(samples).Max() < 0.01f) return NothingHeardReason.TooQuiet;
        return NothingHeardReason.Unknown;
    }
}
