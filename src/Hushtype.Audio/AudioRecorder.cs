using Hushtype.Core;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace Hushtype.Audio;

public sealed class AudioRecorder : IAudioRecorder
{
    private readonly object gate = new();
    private readonly SampleRingBuffer preRoll = new(16000);
    private WaveInEvent? capture;
    private List<float>? utterance;
    private Exception? failure;
    private string? deviceId;
    public string SelectedMicrophone { get; private set; } = "default";
    public sealed record MicrophoneChoice(string Id, string Name);
    public static IReadOnlyList<MicrophoneChoice> Microphones()
    {
        var result = new List<MicrophoneChoice> { new("default", "Windows default microphone") };
        for (var i = 0; i < WaveIn.DeviceCount; i++)
        {
            var caps = WaveIn.GetCapabilities(i);
            result.Add(new($"{caps.NameGuid}:{caps.ProductName}:{i}", caps.ProductName));
        }
        return result;
    }
    public void SelectMicrophone(string microphone)
    {
        lock (gate) { if (utterance is not null) throw new InvalidOperationException("Finish recording before changing microphone."); }
        var previous = SelectedMicrophone;
        SelectedMicrophone = microphone;
        try { Open(); }
        catch { SelectedMicrophone = previous; try { Open(); } catch { } throw; }
    }
    public sealed record EndpointState(string Name, bool Muted, float Level)
    {
        public override string ToString() => $"name={Name}; muted={Muted}; level={Level:P0}";
    }
    // Name, mute and input level of the Windows default capture device; contains no audio.
    public static EndpointState DefaultEndpointState()
    {
        using var devices = new MMDeviceEnumerator();
        using var device = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        var volume = device.AudioEndpointVolume;
        return new(device.FriendlyName, volume.Mute, volume.MasterVolumeLevelScalar);
    }
    public event EventHandler<float>? LevelChanged;
    public event EventHandler<string>? DeviceChanged;
    public const int MaximumSamples = 16000 * 300;

    // Wave mapper accepts PCM16 mono at 16 kHz and performs device-rate conversion.
    // Opening is separate from Start: the device stays warm for 300 ms pre-roll.
    public void Open()
    {
        CloseDevice();
        using var devices = new MMDeviceEnumerator();
        using var device = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        deviceId = device.ID;
        var number = -1;
        if (SelectedMicrophone != "default")
        {
            var devicesList = Microphones();
            // Prefer the exact device, then resolve the same name/GUID after device reordering.
            var chosen = devicesList.Skip(1).FirstOrDefault(x => x.Id == SelectedMicrophone)
                ?? devicesList.Skip(1).FirstOrDefault(x => x.Id[..x.Id.LastIndexOf(':')] == SelectedMicrophone[..SelectedMicrophone.LastIndexOf(':')]);
            if (chosen is null) throw new IOException("Selected microphone is unavailable. Choose another microphone in Settings.");
            number = int.Parse(chosen.Id[(chosen.Id.LastIndexOf(':') + 1)..]);
        }
        var input = new WaveInEvent { DeviceNumber = number, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 30, NumberOfBuffers = 3 };
        input.DataAvailable += OnData;
        input.RecordingStopped += (sender, e) => { lock (gate) { if (ReferenceEquals(sender, capture)) failure = e.Exception ?? new IOException("Microphone capture stopped. Reconnect your microphone and retry."); } };
        capture = input;
        lock (gate) { failure = null; preRoll.Clear(); }
        try { input.StartRecording(); }
        catch { CloseDevice(); throw; }
    }
    // Called by the UI timer only while idle; never cut an utterance in half.
    public void RefreshDefaultDevice()
    {
        if (SelectedMicrophone != "default") { if (failure is not null) Open(); return; }
        using var devices = new MMDeviceEnumerator();
        using var device = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        if (device.ID == deviceId && failure is null) return;
        Open();
        DeviceChanged?.Invoke(this, device.FriendlyName);
    }
    public void Start()
    {
        lock (gate)
        {
            if (capture is null) throw new InvalidOperationException("Microphone is not ready.");
            if (failure is not null) throw new IOException("Microphone unavailable. Reconnect it and retry.", failure);
            if (utterance is not null) throw new InvalidOperationException("Already recording.");
            utterance = new List<float>(preRoll.Latest(4800));
        }
    }
    public float[] Stop()
    {
        lock (gate)
        {
            var result = utterance?.ToArray() ?? [];
            utterance = null;
            if (failure is not null) throw new IOException("Recording was interrupted by a microphone error.", failure);
            return result;
        }
    }
    private void OnData(object? sender, WaveInEventArgs e)
    {
        var samples = new float[e.BytesRecorded / 2];
        double sum = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
            sum += samples[i] * samples[i];
        }
        lock (gate)
        {
            if (!ReferenceEquals(sender, capture)) return;
            preRoll.Write(samples);
            if (utterance is not null)
            {
                var remaining = MaximumSamples - utterance.Count;
                for (var i = 0; i < Math.Min(samples.Length, remaining); i++) utterance.Add(samples[i]);
            }
        }
        if (samples.Length > 0) LevelChanged?.Invoke(this, (float)Math.Sqrt(sum / samples.Length));
    }
    private void CloseDevice()
    {
        var old = capture;
        capture = null;
        if (old is null) return;
        old.DataAvailable -= OnData;
        old.StopRecording();
        old.Dispose();
        lock (gate) { utterance = null; preRoll.Clear(); }
    }
    public void Dispose() => CloseDevice();
}
