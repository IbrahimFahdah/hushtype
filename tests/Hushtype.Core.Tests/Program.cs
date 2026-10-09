using Hushtype.Core;

var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
var ring = new SampleRingBuffer(5);
Check(ring.Latest(3).Length == 0, "Empty pre-roll");
ring.Write([1,2,3]);
Check(ring.Latest(5).SequenceEqual(new float[] {1,2,3}), "Partial pre-roll");
ring.Write([4,5,6,7]);
Check(ring.Latest(3).SequenceEqual(new float[] {5,6,7}), "Wrapped pre-roll");
Check(ring.Latest(99).SequenceEqual(new float[] {3,4,5,6,7}), "Capacity bounded");
ring.Clear();
Check(ring.Latest(5).Length == 0, "Clear drops prior microphone data");
Check(SilenceTrimmer.Trim(new float[16000]).Length == 0, "Silence skipped");
Check(SilenceTrimmer.Trim(Enumerable.Repeat(0.001f,16000).ToArray()).Length == 0, "Low noise skipped");
var voiced = new float[16000]; Array.Fill(voiced, 0.1f, 6000, 1600);
var trimmed = SilenceTrimmer.Trim(voiced);
Check(trimmed.Length > 1600 && trimmed.Length < 16000, "Speech retained with padding");

Check(TranscriptMerger.Append("", "hello world") == "hello world", "First window");
Check(TranscriptMerger.Append("hello", " ") == "hello", "Empty window");
Check(TranscriptMerger.Append("one two three", "two three four") == "one two three four", "Longest overlap");
Check(TranscriptMerger.Append("Hello WORLD", "world again") == "Hello WORLD again", "Case-insensitive overlap");
Check(TranscriptMerger.Append("Hello, world.", "world again") == "Hello, world. again", "Punctuation overlap");
Check(TranscriptMerger.Append("one two", "three four") == "one two three four", "No overlap retained");
Check(TranscriptMerger.Append("one test", "test test again") == "one test test again", "Intentional next-window repetition retained");
Check(TranscriptMerger.Append("don't stop", "stop now") == "don't stop now", "Apostrophe preserved");
Check(TranscriptMerger.Append("one two", "one two") == "one two", "Duplicate window");

var fake = new Fakes();
var pipeline = new DictationPipeline(fake, fake, fake, fake, fake) { MinimumHold = TimeSpan.Zero, PostRoll = TimeSpan.Zero };
var states = new List<DictationState>();
var captureStopped = 0;
pipeline.CaptureStopped += (_, _) => { Check(fake.Stops == 1 && fake.Calls == 0, "Capture-stopped cue follows recorder stop and precedes inference"); captureStopped++; };
pipeline.StateChanged += (_, state) => states.Add(state);
pipeline.Begin(); pipeline.Begin();
Check(fake.Starts == 1, "Auto-repeat ignored");
var end = pipeline.EndAsync(default);
Check(pipeline.State == DictationState.Transcribing, "Transcribing state");
pipeline.Begin(); await pipeline.EndAsync(default);
Check(fake.Starts == 1 && fake.Calls == 1, "Overlapping pipeline ignored");
fake.Result.SetResult("testing"); await end;
Check(fake.Inserted == "Testing." && pipeline.State == DictationState.Idle, "Pipeline paste and reset");
Check(states.SequenceEqual(new[] {DictationState.Recording, DictationState.Transcribing, DictationState.Inserting, DictationState.Idle}), "State order");
Check(captureStopped == 1, "One stop cue despite overlapping end calls");

fake = new Fakes(); pipeline = new(fake, fake, fake, fake, fake) { MinimumHold = TimeSpan.FromHours(1) };
pipeline.Begin(); await pipeline.EndAsync(default);
Check(fake.Calls == 0 && pipeline.State == DictationState.Idle, "Accidental tap discarded");

fake = new Fakes(); pipeline = new(fake, fake, fake, fake, fake) { MinimumHold = TimeSpan.Zero, PostRoll = TimeSpan.Zero };
pipeline.Begin(); end = pipeline.EndAsync(default); fake.Result.SetException(new IOException("Test failure"));
try { await end; throw new Exception("Expected transcription failure"); } catch (IOException) { }
Check(pipeline.State == DictationState.Idle && fake.Inserted is null, "Failure resets without paste");

fake = new Fakes(); pipeline = new(fake, fake, fake, fake, fake) { MinimumHold = TimeSpan.Zero, PostRoll = TimeSpan.Zero };
using var cts = new CancellationTokenSource(); pipeline.Begin(); end = pipeline.EndAsync(cts.Token); cts.Cancel();
try { await end; throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
Check(pipeline.State == DictationState.Idle && fake.Inserted is null, "Cancellation resets without paste");

fake = new Fakes(); pipeline = new(fake, fake, fake, fake, fake) { MinimumHold = TimeSpan.Zero, PostRoll = TimeSpan.Zero };
pipeline.Begin(); pipeline.CancelRecording(); await pipeline.EndAsync(default);
Check(pipeline.State == DictationState.Idle && fake.Calls == 0, "Disable cancels capture");

fake = new Fakes(); pipeline = new(fake, fake, fake, fake, fake) { MinimumHold = TimeSpan.Zero, PostRoll = TimeSpan.Zero };
pipeline.Begin(); end = pipeline.EndAsync(default); fake.Result.SetResult(" "); await end;
Check(fake.Inserted is null && pipeline.State == DictationState.Idle, "Empty transcript not pasted");
fake = new Fakes { Samples = new float[32000] }; pipeline = new(fake, fake, fake, fake, fake) { MinimumHold = TimeSpan.Zero, PostRoll = TimeSpan.Zero };
var heard = new List<NothingHeardReason>(); pipeline.NothingHeard += (_, reason) => heard.Add(reason);
pipeline.Begin(); end = pipeline.EndAsync(default); fake.Result.SetResult(""); await end;
Check(heard.SequenceEqual([NothingHeardReason.MicrophoneBlocked]) && fake.Inserted is null, "Digital silence reported as a blocked microphone");

// Blocked microphone: 1.5 s of exact zeros; a real noise floor never qualifies.
var noisy = new float[48000]; var random = new Random(1); for (var i = 0; i < noisy.Length; i++) noisy[i] = (float)(random.NextDouble() - 0.5) * 0.0002f;
Check(AudioSignal.Diagnose(new float[24000], TimeSpan.FromSeconds(2)) == NothingHeardReason.MicrophoneBlocked, "1.5 s of zeros is blocked");
Check(AudioSignal.Diagnose(new float[23999], TimeSpan.FromSeconds(2)) != NothingHeardReason.MicrophoneBlocked, "Shorter zero run is not blocked");
Check(AudioSignal.Diagnose(noisy, TimeSpan.FromSeconds(3)) == NothingHeardReason.TooQuiet, "Faint noise over a long hold is too quiet");
Check(AudioSignal.Diagnose(noisy, TimeSpan.FromMilliseconds(400)) == NothingHeardReason.Unknown, "Short taps are not reported");
var loud = (float[])noisy.Clone(); Array.Fill(loud, 0.2f, 1000, 8000);
Check(AudioSignal.Diagnose(loud, TimeSpan.FromSeconds(3)) == NothingHeardReason.Unknown, "Loud audio is not reported as quiet");
// Adaptive silence gate: a quiet but clean microphone still passes; the gate never exceeds 0.006.
var quiet = (float[])noisy.Clone(); for (var i = 16000; i < 24000; i++) quiet[i] = MathF.Sin(i * 0.05f) * 0.004f;
Check(SilenceTrimmer.Trim(quiet).Length > 0, "Quiet speech over a clean floor is kept");
Check(SilenceTrimmer.Trim(quiet, 0.006f).Length == 0, "The old fixed gate would have dropped it");
Check(SilenceTrimmer.Threshold(Enumerable.Repeat(0.05f, 16000).ToArray()) == 0.006f && SilenceTrimmer.Threshold(new float[16000]) == 0.0015f, "Gate clamped to 0.0015-0.006");

// Typing plan: ~40-unit chunks, surrogate pairs never split, newlines become Enter.
Check(TypingPlan.ShouldType(new string('a', 200)) && !TypingPlan.ShouldType(new string('a', 201)) && !TypingPlan.ShouldType("one\ntwo"), "Type short single-line text only");
var chunks = TypingPlan.Split(new string('a', 39) + "😀" + "b");
Check(chunks.Count == 2 && chunks[0].Length == 41 && chunks[0].EndsWith("😀") && chunks[1] == "b", "Surrogate pair kept together");
Check(TypingPlan.Split(new string('x', 100)).Select(x => x.Length).SequenceEqual([40, 40, 20]), "Chunks of 40");
Check(TypingPlan.Split("one\r\ntwo\nthree\r").SequenceEqual(["one", TypingPlan.Enter, "two", TypingPlan.Enter, "three", TypingPlan.Enter]), "Newlines become Enter");
Check(string.Concat(TypingPlan.Split("héllo wörld 😀 ok")) == "héllo wörld 😀 ok", "Chunks rebuild the text");
fake = new Fakes(); pipeline = new(fake, fake, fake, fake, fake) { MinimumHold = TimeSpan.Zero, PostRoll = TimeSpan.FromMilliseconds(50) };
pipeline.Begin(); end = pipeline.EndAsync(default);
Check(fake.Stops == 0 && pipeline.State == DictationState.Transcribing, "Post-roll keeps capturing after release");
pipeline.Begin(); await pipeline.EndAsync(default);
Check(fake.Starts == 1, "Press during post-roll ignored");
fake.Result.SetResult("testing"); await end;
Check(fake.Stops == 1 && fake.Inserted == "Testing.", "Post-roll then paste");

fake = new Fakes(); pipeline = new(fake, fake, fake, fake, fake) { MinimumHold = TimeSpan.FromHours(1), PostRoll = TimeSpan.FromHours(1) };
pipeline.Begin(); await pipeline.EndAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
Check(fake.Stops == 1 && fake.Calls == 0, "Accidental tap skips post-roll");
Console.WriteLine($"PASS: {checks} core checks.");

sealed class Fakes : IAudioRecorder, ITranscriber, ITextPostProcessor, ITextInserter, IForegroundAppProvider
{
    public int Starts, Stops, Calls;
    public string? Inserted;
    public TaskCompletionSource<string> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public event EventHandler<float>? LevelChanged { add { } remove { } }
    public void Start() => Starts++;
    public float[] Samples = new float[16000];
    public float[] Stop() { Stops++; return Samples; }
    public void Dispose() { }
    public Task<string> TranscribeAsync(float[] samples, TranscribeOptions options, CancellationToken ct) { Calls++; return Result.Task.WaitAsync(ct); }
    public Task<CleanedText> ProcessAsync(string raw, TargetApp context, CancellationToken ct) => Task.FromResult(new CleanedText("Testing.", []));
    public Task InsertAsync(string text, TargetApp target, CancellationToken ct) { Inserted = text; return Task.CompletedTask; }
    public TargetApp GetCurrent() => new("notepad.exe", "Test", 1);
}
