using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Hushtype.Audio;
using Hushtype.Platform;
using Hushtype.Speech;

if (args.Length > 0 && args[0] == "--benchmark")
{
    // --benchmark file.wav data-directory precision threads
    if (args.Length != 5) throw new ArgumentException("Usage: --benchmark file.wav data-directory fp32|int8 threads");
    var directory = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(directory);
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    var config = new SpeechConfiguration(args[3], int.Parse(args[4]));
    using var transcriber = new ParakeetTranscriber(new ModelManager(http, Path.Combine(directory, "models")), config);
    transcriber.Diagnostic += (_, text) => Console.WriteLine(text);
    await transcriber.InitializeAsync(new Progress<ModelProgress>(p => Console.WriteLine(p.Message)), default);
    var samples = WaveFileSamples.Read(args[1]);
    var results = new List<object>();
    for (var i = 0; i < 3; i++)
    {
        var watch = Stopwatch.StartNew();
        var text = await transcriber.TranscribeAsync(samples, new("en"), default);
        watch.Stop();
        if (string.IsNullOrWhiteSpace(text)) throw new Exception("Expected a transcript from the supplied speech clip.");
        var result = new { model = transcriber.Model, runtime = transcriber.Runtime, threads = config.Threads, duration_ms = samples.Length / 16.0, stt_ms = watch.Elapsed.TotalMilliseconds, text };
        results.Add(result);
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
    if (await transcriber.TranscribeAsync(new float[16000], new("en"), default) != "") throw new Exception("Silence produced a transcript.");
    using var canceled = new CancellationTokenSource(); canceled.Cancel();
    try { await transcriber.TranscribeAsync(samples, new("en"), canceled.Token); throw new Exception("Cancellation was ignored."); }
    catch (OperationCanceledException) { }
    await File.WriteAllTextAsync(Path.Combine(directory, $"benchmark-{Path.GetFileNameWithoutExtension(args[1])}-{config.Precision}-{config.Threads}.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("PASS: native speech inference, silence and cancellation checks.");
    return;
}

if (args.Length > 0 && args[0] == "--throttle-benchmark")
{
    // --throttle-benchmark file.wav data-directory runs default|off|on output.json
    if (args.Length != 6) throw new ArgumentException("Usage: --throttle-benchmark file.wav data-directory runs default|off|on output.json");
    var mode = args[4] switch { "off" => ProcessPower.Throttling.Off, "on" => ProcessPower.Throttling.On, _ => ProcessPower.Throttling.SystemDefault };
    ProcessPower.SetExecutionSpeedThrottling(mode);
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    using var transcriber = new ParakeetTranscriber(new ModelManager(http, Path.Combine(Path.GetFullPath(args[2]), "models")), new SpeechConfiguration());
    await transcriber.InitializeAsync(new Progress<ModelProgress>(), default);
    var samples = WaveFileSamples.Read(args[1]);
    var times = new List<double>();
    for (var i = 0; i < int.Parse(args[3]); i++)
    {
        var watch = Stopwatch.StartNew();
        await transcriber.TranscribeAsync(samples, new("en"), default);
        times.Add(Math.Round(watch.Elapsed.TotalMilliseconds));
        await Task.Delay(500); // idle gap, as between real dictations
    }
    var sorted = times.Order().ToList();
    await File.WriteAllTextAsync(args[5], JsonSerializer.Serialize(new { mode = args[4], power = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus.ToString(), runs = times.Count, median_ms = sorted[sorted.Count / 2], p90_ms = sorted[(int)(sorted.Count * 0.9)], min_ms = sorted[0], max_ms = sorted[^1], times }));
    return;
}
if (args.Length > 0 && args[0] == "--boundary-benchmark")
{
    // --boundary-benchmark file.wav reference.txt data-directory: fixed windows vs cuts at pauses.
    if (args.Length != 4) throw new ArgumentException("Usage: --boundary-benchmark file.wav reference.txt data-directory");
    var directory = Path.GetFullPath(args[3]);
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    var samples = WaveFileSamples.Read(args[1]);
    var reference = Words(await File.ReadAllTextAsync(args[2]));
    foreach (var pauses in new[] { false, true })
    {
        using var transcriber = new ParakeetTranscriber(new ModelManager(http, Path.Combine(directory, "models")), new SpeechConfiguration("int8", 4, pauses));
        await transcriber.InitializeAsync(new Progress<ModelProgress>(), default);
        var text = await transcriber.TranscribeAsync(samples, new("en"), default);
        var (inserted, deleted, substituted) = Align(reference, Words(text));
        Console.WriteLine(JsonSerializer.Serialize(new { split = pauses ? "pauses" : "fixed", file = Path.GetFileName(args[1]), duration_s = samples.Length / 16000.0, reference_words = reference.Length, inserted, deleted, substituted, text }));
    }
    return;
}
static string[] Words(string text) => text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
    .Select(w => new string(w.Where(c => char.IsLetterOrDigit(c) || c == '\'').ToArray())).Where(w => w.Length > 0).ToArray();
// Word-level edit distance with backtrace: inserted ≈ duplicated words, deleted ≈ missing words.
static (int Inserted, int Deleted, int Substituted) Align(string[] reference, string[] hypothesis)
{
    var cost = new int[reference.Length + 1, hypothesis.Length + 1];
    for (var i = 0; i <= reference.Length; i++) cost[i, 0] = i;
    for (var j = 0; j <= hypothesis.Length; j++) cost[0, j] = j;
    for (var i = 1; i <= reference.Length; i++)
        for (var j = 1; j <= hypothesis.Length; j++)
            cost[i, j] = Math.Min(Math.Min(cost[i - 1, j] + 1, cost[i, j - 1] + 1), cost[i - 1, j - 1] + (reference[i - 1] == hypothesis[j - 1] ? 0 : 1));
    int a = reference.Length, b = hypothesis.Length, inserted = 0, deleted = 0, substituted = 0;
    while (a > 0 || b > 0)
    {
        if (a > 0 && b > 0 && cost[a, b] == cost[a - 1, b - 1] + (reference[a - 1] == hypothesis[b - 1] ? 0 : 1)) { if (reference[a - 1] != hypothesis[b - 1]) substituted++; a--; b--; }
        else if (b > 0 && cost[a, b] == cost[a, b - 1] + 1) { inserted++; b--; }
        else { deleted++; a--; }
    }
    return (inserted, deleted, substituted);
}

var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
var testFolder = Path.Combine(Path.GetTempPath(), "hushtype-model-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testFolder);
try
{
    var payload = new byte[] { 1, 2, 3, 4, 5 };
    var hash = Convert.ToHexString(SHA256.HashData(payload));
    var spec = new SpeechModelSpec("test-model", "fp32", "test/repo", "pinned-revision", [new("encoder.onnx", 5, hash)], "encoder.onnx", "decoder.onnx", "joiner.onnx");
    var handler = new FakeHandler(payload);
    using var http = new HttpClient(handler);
    var manager = new ModelManager(http, testFolder);
    var progress = new Progress<ModelProgress>();
    var folder = await manager.EnsureAsync(spec, progress, default);
    var path = Path.Combine(folder, "encoder.onnx");
    Check(File.ReadAllBytes(path).SequenceEqual(payload), "Valid download accepted");
    Check(handler.LastUri!.Contains("/resolve/pinned-revision/encoder.onnx"), "Download revision pinned");
    var hashedAfterDownload = manager.FilesHashed;
    await manager.EnsureAsync(spec, progress, default);
    Check(handler.Calls == 1, "Valid offline cache avoids HTTP");
    Check(manager.FilesHashed == hashedAfterDownload, "Unchanged verified file is not re-hashed");
    var restarted = new ModelManager(http, testFolder); await restarted.EnsureAsync(spec, progress, default);
    Check(restarted.FilesHashed == 0, "Restarted manager trusts the marker");
    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));
    await restarted.EnsureAsync(spec, progress, default);
    Check(restarted.FilesHashed == 1 && handler.Calls == 1, "Changed timestamp forces a re-hash, not a download");
    File.WriteAllBytes(path, [5,4,3,2,1]);
    await manager.EnsureAsync(spec, progress, default);
    Check(handler.Calls == 2 && File.ReadAllBytes(path).SequenceEqual(payload), "Corrupt cache replaced after verification");
    var bad = spec with { Name = "bad-model", Files = [new("encoder.onnx", 5, new string('0',64))] };
    try { await manager.EnsureAsync(bad, progress, default); throw new Exception("Bad checksum accepted"); }
    catch (InvalidDataException) { }
    Check(!File.Exists(Path.Combine(testFolder, "bad-model", "encoder.onnx")), "Bad hash never promoted");
    Check(Directory.GetFiles(Path.Combine(testFolder, "bad-model"), "*.partial").Length == 0, "Failed partial removed");
    var shortSpec = spec with { Name = "short-model", Files = [new("encoder.onnx", 4, hash)] };
    try { await manager.EnsureAsync(shortSpec, progress, default); throw new Exception("Oversized download accepted"); }
    catch (InvalidDataException) { }
    Check(!File.Exists(Path.Combine(testFolder, "short-model", "encoder.onnx")), "Unexpected size rejected");
    using var cts = new CancellationTokenSource(); cts.Cancel();
    var callsBefore = handler.Calls;
    try { await manager.EnsureAsync(spec, progress, cts.Token); throw new Exception("Canceled setup accepted"); }
    catch (OperationCanceledException) { }
    Check(handler.Calls == callsBefore, "Cancellation does not start HTTP");
    Check(ModelCatalog.Get("fp32").Files.Any(f => f.Name == "encoder.weights"), "External FP32 weights included");
    Check(ModelCatalog.Get("int8").Files.All(f => f.Size > 0 && f.Sha256.Length == 64), "INT8 manifest verified");
    try { ModelCatalog.Get("unknown"); throw new Exception("Invalid model accepted"); } catch (ArgumentException) { checks++; }
    using var concurrentHttp = new HttpClient(new FakeHandler(payload));
    var concurrent = new ModelManager(concurrentHttp, Path.Combine(testFolder, "parallel"));
    await Task.WhenAll(concurrent.EnsureAsync(spec, progress, default), concurrent.EnsureAsync(spec, progress, default));
    Check(File.ReadAllBytes(Path.Combine(testFolder, "parallel", "test-model", "encoder.onnx")).SequenceEqual(payload), "Concurrent setup serialized safely");
    Console.WriteLine($"PASS: {checks} speech model checks.");
}
finally
{
    // Only delete the uniquely-created temporary test directory.
    if (Path.GetFileName(testFolder).StartsWith("hushtype-model-tests-")) Directory.Delete(testFolder, true);
}

sealed class FakeHandler(byte[] bytes) : HttpMessageHandler
{
    public int Calls;
    public string? LastUri;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls++;
        LastUri = request.RequestUri!.ToString();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
}
