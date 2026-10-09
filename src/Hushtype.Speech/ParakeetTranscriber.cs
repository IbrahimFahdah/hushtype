using System.Runtime.InteropServices;
using Hushtype.Core;
using SherpaOnnx;

namespace Hushtype.Speech;

public sealed class ParakeetTranscriber(ModelManager models, SpeechConfiguration configuration) : ITranscriber, IDisposable
{
    private OfflineRecognizer? recognizer;
    private readonly SemaphoreSlim gate = new(1);
    private bool disposed;
    public string Runtime { get; private set; } = "not loaded";
    public string Model { get; private set; } = "not loaded";
    public event EventHandler<string>? Diagnostic;

    public static string ProbeRuntime()
    {
        var library = NativeLibrary.Load("sherpa-onnx-c-api.dll", typeof(OfflineRecognizer).Assembly, null);
        NativeLibrary.Free(library);
        return "speech_engine=sherpa-onnx; native_library=loaded; configured_provider=cpu; cuda_required=false";
    }
    public Task InitializeAsync(IProgress<ModelProgress> progress, CancellationToken ct) => Task.Run(async () =>
    {
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (recognizer is not null) return;
            if (configuration.Threads is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(configuration), "Speech threads must be between 1 and 20.");
            var spec = ModelCatalog.Get(configuration.Precision);
            var folder = await models.EnsureAsync(spec, progress, ct);
            ct.ThrowIfCancellationRequested();
            progress.Report(new("Loading Parakeet v2 (" + spec.Precision.ToUpperInvariant() + ")"));
            var config = new OfflineRecognizerConfig();
            config.FeatConfig.SampleRate = 16000;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.Transducer.Encoder = Path.Combine(folder, spec.Encoder);
            config.ModelConfig.Transducer.Decoder = Path.Combine(folder, spec.Decoder);
            config.ModelConfig.Transducer.Joiner = Path.Combine(folder, spec.Joiner);
            config.ModelConfig.Tokens = Path.Combine(folder, "tokens.txt");
            config.ModelConfig.ModelType = "nemo_transducer";
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.NumThreads = configuration.Threads;
            config.ModelConfig.Debug = 0;
            config.DecodingMethod = "greedy_search";
            var candidate = new OfflineRecognizer(config);
            try
            {
                progress.Report(new("Warming up English speech recognition"));
                using var stream = candidate.CreateStream();
                stream.AcceptWaveform(16000, new float[16000]);
                candidate.Decode(stream);
                ct.ThrowIfCancellationRequested();
                recognizer = candidate;
                Runtime = "cpu";
                Model = spec.Name;
                Diagnostic?.Invoke(this, $"engine=sherpa-onnx; runtime=cpu; model={Model}; language=en; threads={configuration.Threads}");
            }
            catch { candidate.Dispose(); throw; }
        }
        finally { gate.Release(); }
    }, ct);

    public Task<string> TranscribeAsync(float[] samples, TranscribeOptions options, CancellationToken ct) => Task.Run(async () =>
    {
        ct.ThrowIfCancellationRequested();
        if (options.Language is not ("en" or "auto")) throw new NotSupportedException("This model supports English dictation only.");
        if (options.Dictionary is { Length: > 0 }) throw new NotSupportedException("Parakeet TDT does not support Whisper-style dictionary prompts. Dictionary corrections belong in post-processing.");
        var threshold = SilenceTrimmer.Threshold(samples);
        var trimmed = SilenceTrimmer.Trim(samples, threshold);
        if (trimmed.Length == 0) return "";
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (recognizer is null) throw new InvalidOperationException("Speech recognition is still loading.");
            const int window = 20 * 16000;
            const int overlap = 16000;
            var text = "";
            for (var start = 0; start < trimmed.Length; start += window - overlap)
            {
                ct.ThrowIfCancellationRequested();
                var end = Math.Min(start + window, trimmed.Length);
                var chunk = trimmed[start..end];
                if (SilenceTrimmer.Trim(chunk, threshold).Length > 0)
                {
                    using var stream = recognizer.CreateStream();
                    stream.AcceptWaveform(16000, chunk);
                    // Native decode cannot be interrupted. Keep it off the UI thread,
                    // retain the model until it finishes, then honor cancellation.
                    recognizer.Decode(stream);
                    ct.ThrowIfCancellationRequested();
                    text = TranscriptMerger.Append(text, stream.Result.Text);
                }
                if (end == trimmed.Length) break;
            }
            return text.Trim();
        }
        finally { gate.Release(); }
    }, ct);
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        recognizer?.Dispose();
        recognizer = null;
        gate.Dispose();
    }
}
