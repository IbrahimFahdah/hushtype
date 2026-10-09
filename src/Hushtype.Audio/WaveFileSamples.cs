using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Hushtype.Audio;

public static class WaveFileSamples
{
    public static float[] Read(string path)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider source = reader;
        if (source.WaveFormat.Channels == 2) source = new StereoToMonoSampleProvider(source);
        if (source.WaveFormat.Channels != 1) throw new NotSupportedException("Use a mono or stereo audio file.");
        if (source.WaveFormat.SampleRate != 16000) source = new WdlResamplingSampleProvider(source, 16000);
        var samples = new List<float>();
        var buffer = new float[16000];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, read).ToArray());
            if (samples.Count > AudioRecorder.MaximumSamples) throw new InvalidDataException("Audio exceeds the five-minute limit.");
        }
        return samples.ToArray();
    }
}
