using NAudio.Wave;

namespace Hushtype.Audio;

public sealed class AudioCues : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim gate = new(1);
    private bool disposed;
    public bool Enabled { get; set; } = true;
    public event EventHandler<Exception>? Failed;

    public void Play(bool start)
    {
        if (!Enabled || disposed) return;
        _ = PlayAsync(start);
    }

    public async Task PlayAsync(bool start)
    {
        if (!Enabled || disposed) return;
        try
        {
            await Task.Run(async () =>
            {
                if (!await gate.WaitAsync(0, lifetime.Token)) return;
                try
                {
                    if (!Enabled) return;
                    using var stream = new MemoryStream(CreateTone(start));
                    using var source = new RawSourceWaveStream(stream, new WaveFormat(22050, 16, 1));
                    using var output = new WaveOutEvent();
                    var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    output.PlaybackStopped += (_, e) => { if (e.Exception is null) finished.TrySetResult(); else finished.TrySetException(e.Exception); };
                    output.Init(source); output.Play();
                    try { await finished.Task.WaitAsync(TimeSpan.FromSeconds(2), lifetime.Token); }
                    finally { output.Stop(); }
                }
                finally { gate.Release(); }
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Failed?.Invoke(this, ex); }
    }

    private static byte[] CreateTone(bool start)
    {
        const int samples = 1764; // 80 ms, soft fade at both ends
        var bytes = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var t = i / 22050.0;
            var envelope = Math.Sin(Math.PI * i / (samples - 1));
            var frequency = start ? 660 : 440;
            var value = (short)(Math.Sin(2 * Math.PI * frequency * t) * envelope * envelope * 0.035 * short.MaxValue);
            bytes[i * 2] = (byte)(value & 255); bytes[i * 2 + 1] = (byte)(value >> 8);
        }
        return bytes;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; lifetime.Cancel();
        // Callbacks may still be finishing; do not dispose their synchronization primitives here.
    }
}
