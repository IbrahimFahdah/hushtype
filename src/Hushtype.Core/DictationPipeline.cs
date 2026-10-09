using System.Diagnostics;

namespace Hushtype.Core;

// Called on the UI dispatcher. A state transition occurs before any await, so
// repeated hotkey edges cannot start overlapping capture or inference.
public sealed class DictationPipeline(IAudioRecorder recorder, ITranscriber transcriber,
    ITextPostProcessor cleaner, ITextInserter inserter, IForegroundAppProvider foreground)
{
    public DictationState State { get; private set; }
    public event EventHandler<DictationState>? StateChanged;
    public event EventHandler<DictationMetrics>? Completed;
    public event EventHandler? CaptureStopped;
    public event EventHandler<DictationRecord>? TranscriptReady;
    public event EventHandler<NothingHeardReason>? NothingHeard;
    private long started;
    private TargetApp? target;
    public TimeSpan MinimumHold { get; init; } = TimeSpan.FromMilliseconds(150);
    public TimeSpan PostRoll { get; init; } = TimeSpan.FromMilliseconds(120);

    public void Begin()
    {
        if (State != DictationState.Idle) return;
        target = foreground.GetCurrent();
        recorder.Start();
        started = Stopwatch.GetTimestamp();
        SetState(DictationState.Recording);
    }

    public void CancelRecording()
    {
        if (State != DictationState.Recording) return;
        try { recorder.Stop(); }
        finally { SetState(DictationState.Idle); }
    }

    public async Task EndAsync(CancellationToken ct)
    {
        if (State != DictationState.Recording) return;
        var release = Stopwatch.StartNew();
        try
        {
            var held = Stopwatch.GetElapsedTime(started);
            SetState(DictationState.Transcribing);
            // Keep capturing briefly so audio still in device buffers isn't clipped.
            // Not cancellable: Stop must always run to end the utterance.
            if (held >= MinimumHold && PostRoll > TimeSpan.Zero) await Task.Delay(PostRoll, CancellationToken.None);
            var samples = recorder.Stop();
            CaptureStopped?.Invoke(this, EventArgs.Empty);
            if (held < MinimumHold || samples.Length == 0) return;
            ct.ThrowIfCancellationRequested();
            var stage = Stopwatch.StartNew();
            var raw = await transcriber.TranscribeAsync(samples, new(), ct);
            var sttMs = stage.Elapsed.TotalMilliseconds;
            if (string.IsNullOrWhiteSpace(raw)) { NothingHeard?.Invoke(this, AudioSignal.Diagnose(samples, held)); return; }
            stage.Restart();
            var cleaned = await cleaner.ProcessAsync(raw, target!, ct);
            var text = cleaned.Text;
            var cleanupMs = stage.Elapsed.TotalMilliseconds;
            if (string.IsNullOrWhiteSpace(text)) return;
            ct.ThrowIfCancellationRequested();
            SetState(DictationState.Inserting);
            TranscriptReady?.Invoke(this, new(DateTimeOffset.UtcNow, target!.ProcessName, raw, text, samples.Length / 16.0, sttMs, cleanupMs,
                cleaned.Corrections.Count == 0 ? null : cleaned.Corrections));
            stage.Restart();
            await inserter.InsertAsync(text, target!, ct);
            Completed?.Invoke(this, new(samples.Length / 16.0, sttMs, cleanupMs,
                stage.Elapsed.TotalMilliseconds, release.Elapsed.TotalMilliseconds));
        }
        finally { SetState(DictationState.Idle); }
    }

    private void SetState(DictationState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
