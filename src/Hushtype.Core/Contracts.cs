namespace Hushtype.Core;

public enum DictationState { Idle, Recording, Transcribing, Inserting }
public sealed record TargetApp(string ProcessName, string WindowTitle, nint WindowHandle);
public sealed record TranscribeOptions(string Language = "en", string[]? Dictionary = null);
public sealed record DictationMetrics(double AudioMs, double SttMs, double CleanupMs, double InsertMs, double ReleaseToPasteMs);
public interface IHotkeyService : IDisposable
{
    event EventHandler? PressStarted;
    event EventHandler? PressEnded;
    void Start();
    void Stop();
}
public interface IAudioRecorder : IDisposable
{
    void Start();
    float[] Stop();
    event EventHandler<float>? LevelChanged;
}
public interface ITranscriber { Task<string> TranscribeAsync(float[] samples, TranscribeOptions options, CancellationToken ct); }
public interface ITextPostProcessor { Task<string> ProcessAsync(string raw, TargetApp context, CancellationToken ct); }
public interface ITextInserter { Task InsertAsync(string text, TargetApp target, CancellationToken ct); }
public interface IForegroundAppProvider { TargetApp GetCurrent(); }
