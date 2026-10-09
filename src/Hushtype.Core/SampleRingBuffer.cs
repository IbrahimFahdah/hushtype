namespace Hushtype.Core;

// Synchronization belongs to the recorder so snapshot + capture start are atomic.
public sealed class SampleRingBuffer(int capacity)
{
    private readonly float[] samples = capacity > 0 ? new float[capacity] : throw new ArgumentOutOfRangeException(nameof(capacity));
    private int next;
    private int count;
    public void Write(ReadOnlySpan<float> values)
    {
        foreach (var value in values)
        {
            samples[next] = value;
            next = (next + 1) % samples.Length;
            count = Math.Min(count + 1, samples.Length);
        }
    }
    public float[] Latest(int requested)
    {
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
        var length = Math.Min(requested, count);
        var result = new float[length];
        var start = (next - length + samples.Length) % samples.Length;
        for (var i = 0; i < length; i++) result[i] = samples[(start + i) % samples.Length];
        return result;
    }
    public void Clear() { next = count = 0; Array.Clear(samples); }
}
