using System.Net.Http;
using System.Security.Cryptography;

namespace Hushtype.Speech;

public sealed class ModelManager(HttpClient http, string directory)
{
    private readonly SemaphoreSlim gate = new(1);
    // Full SHA-256 passes performed; cached verifications are not counted.
    public int FilesHashed { get; private set; }
    public async Task<string> EnsureAsync(SpeechModelSpec spec, IProgress<ModelProgress> progress, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var folder = Path.Combine(directory, spec.Name);
            Directory.CreateDirectory(folder);
            var total = spec.Files.Sum(f => f.Size);
            long completed = 0;
            foreach (var file in spec.Files)
            {
                ct.ThrowIfCancellationRequested();
                var path = Path.Combine(folder, file.Name);
                progress.Report(new("Verifying " + file.Name, (double)completed / total));
                if (!Verified(path, file) && !await ValidAsync(path, file, ct))
                {
                    var temporary = path + "." + Guid.NewGuid().ToString("N") + ".partial";
                    try
                    {
                        var url = $"https://huggingface.co/{spec.Repository}/resolve/{spec.Revision}/{file.Name}";
                        progress.Report(new("Downloading " + file.Name, (double)completed / total));
                        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                        response.EnsureSuccessStatusCode();
                        await using (var source = await response.Content.ReadAsStreamAsync(ct))
                        await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
                        {
                            var buffer = new byte[131072];
                            long received = 0;
                            var reportAt = DateTime.UtcNow;
                            int read;
                            while ((read = await source.ReadAsync(buffer, ct)) != 0)
                            {
                                received += read;
                                if (received > file.Size) throw new InvalidDataException("Model download exceeded its expected size.");
                                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                                if (DateTime.UtcNow >= reportAt)
                                {
                                    progress.Report(new($"Downloading {file.Name} — {(completed + received) / 1048576} / {total / 1048576} MB", (double)(completed + received) / total));
                                    reportAt = DateTime.UtcNow.AddMilliseconds(250);
                                }
                            }
                        }
                        progress.Report(new("Verifying " + file.Name, (double)completed / total));
                        if (!await ValidAsync(temporary, file, ct)) throw new InvalidDataException("Model checksum mismatch. Please retry setup.");
                        File.Move(temporary, path, true);
                        MarkVerified(path, file);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                completed += file.Size;
            }
            progress.Report(new("Model files verified", 1));
            return folder;
        }
        finally { gate.Release(); }
    }
    private async Task<bool> ValidAsync(string path, ModelFile spec, CancellationToken ct)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != spec.Size) return false;
        FilesHashed++;
        bool valid;
        await using (var stream = File.OpenRead(path))
            valid = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).Equals(spec.Sha256, StringComparison.OrdinalIgnoreCase);
        if (valid && !path.EndsWith(".partial")) MarkVerified(path, spec);
        return valid;
    }
    // A marker beside each verified file records size, last-write time and hash, so unchanged
    // files skip the full re-hash on later launches. Any change to the file forces a re-hash.
    private static string Marker(string path) => path + ".verified";
    private static string Stamp(string path, ModelFile spec)
    {
        var info = new FileInfo(path);
        return $"{info.Length}|{info.LastWriteTimeUtc.Ticks}|{spec.Sha256.ToUpperInvariant()}";
    }
    private static bool Verified(string path, ModelFile spec)
    {
        try { return File.Exists(path) && new FileInfo(path).Length == spec.Size && File.Exists(Marker(path)) && File.ReadAllText(Marker(path)) == Stamp(path, spec); }
        catch (IOException) { return false; }
    }
    private static void MarkVerified(string path, ModelFile spec)
    {
        try { File.WriteAllText(Marker(path), Stamp(path, spec)); }
        catch (IOException) { } // The cache is an optimization; verification already passed.
    }
}
