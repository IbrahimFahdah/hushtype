using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Hushtype.Audio;
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
    await manager.EnsureAsync(spec, progress, default);
    Check(handler.Calls == 1, "Valid offline cache avoids HTTP");
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
