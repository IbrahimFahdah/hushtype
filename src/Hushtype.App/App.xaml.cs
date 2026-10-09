using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Hushtype.Audio;
using Hushtype.Cleanup;
using Hushtype.Core;
using Hushtype.Data;
using Hushtype.Platform;
using Hushtype.Speech;
using Serilog;

namespace Hushtype.App;

public partial class App : Application
{
    private IHost? host;
    private TaskbarIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private StatusWindow? statusWindow;
    private OverlayWindow? overlay;
    private readonly AudioCues cues = new();
    private IHotkeyService? hotkey;
    private DictationPipeline? pipeline;
    private AudioRecorder? recorder;
    private ParakeetTranscriber? speech;
    private readonly CancellationTokenSource lifetime = new();
    private Task initialization = Task.CompletedTask;
    private Task operation = Task.CompletedTask;
    private CancellationTokenSource? utteranceCancellation;
    private DispatcherTimer? capTimer;
    private DispatcherTimer? deviceTimer;
    private MenuItem? toggle;
    private bool ready, enabled = true, quitting, checkingDevice;
    private Mutex? instance;
    private int exitCode;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, "Local\\Hushtype.Dictation", out var first);
        if (!first) { Shutdown(); return; }
        try
        {
            var index = Array.IndexOf(e.Args, "--data-dir");
            var directory = index >= 0 && index + 1 < e.Args.Length ? Path.GetFullPath(e.Args[index + 1])
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hushtype");
            Directory.CreateDirectory(directory);
            dataDirectory = directory;
            customDataDirectory = index >= 0;
            settingsStore = new SettingsStore(Path.Combine(customDataDirectory ? directory : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hushtype"), "settings.json"));
            Log.Logger = new LoggerConfiguration().WriteTo.File(Path.Combine(directory, "logs", "hushtype-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7).CreateLogger();
            try
            {
                settings = await settingsStore.LoadAsync(Path.Combine(directory, "ui-preferences.json"));
            }
            catch (Exception ex) { Log.Warning(ex, "Could not read settings; using defaults for this session"); }
            cues.Enabled = settings.Sounds;
            if (e.Args.Contains("--no-sounds")) cues.Enabled = false;
            cues.Failed += (_, ex) => Log.Warning(ex, "Sound cue unavailable; dictation continues");
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
            builder.Services.AddSerilog();
            builder.Services.AddSingleton(Dispatcher);
            builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(30) });
            builder.Services.AddSingleton(sp => new ModelManager(sp.GetRequiredService<HttpClient>(), Path.Combine(directory, "models")));
            var precisionIndex = Array.IndexOf(e.Args, "--speech-precision");
            if (precisionIndex >= 0 && (precisionIndex + 1 >= e.Args.Length || e.Args[precisionIndex + 1].StartsWith("--"))) throw new ArgumentException("--speech-precision needs fp32 or int8.");
            var precision = precisionIndex >= 0 ? e.Args[precisionIndex + 1] : settings.Precision;
            ModelCatalog.Get(precision);
            var threadsIndex = Array.IndexOf(e.Args, "--speech-threads");
            if (threadsIndex >= 0 && (threadsIndex + 1 >= e.Args.Length || e.Args[threadsIndex + 1].StartsWith("--"))) throw new ArgumentException("--speech-threads needs a number from 1 to 20.");
            var threads = threadsIndex >= 0 ? int.Parse(e.Args[threadsIndex + 1]) : settings.Threads;
            if (threads is < 1 or > 20) throw new ArgumentException("--speech-threads must be between 1 and 20.");
            builder.Services.AddSingleton(new SpeechConfiguration(precision, threads));
            builder.Services.AddSingleton<ParakeetTranscriber>();
            builder.Services.AddSingleton<ITranscriber>(sp => sp.GetRequiredService<ParakeetTranscriber>());
            builder.Services.AddSingleton<AudioRecorder>();
            builder.Services.AddSingleton<IAudioRecorder>(sp => sp.GetRequiredService<AudioRecorder>());
            builder.Services.AddSingleton<IHotkeyService, HotkeyService>();
            builder.Services.AddSingleton<IForegroundAppProvider, ForegroundAppProvider>();
            personalCleaner.UseRules = settings.Cleanup;
            localStore = new LocalStore(Path.Combine(directory, "hushtype.db"));
            await localStore.InitializeAsync();
            await RefreshCorrectionsAsync();
            builder.Services.AddSingleton<ITextPostProcessor>(personalCleaner);
            builder.Services.AddSingleton<ClipboardTextInserter>();
            builder.Services.AddSingleton<ITextInserter>(sp => sp.GetRequiredService<ClipboardTextInserter>());
            builder.Services.AddSingleton<DictationPipeline>();
            host = builder.Build();
            await host.StartAsync(lifetime.Token);
            hotkey = host.Services.GetRequiredService<IHotkeyService>();
            ((HotkeyService)hotkey).Configure(settings.Hotkey);
            ((HotkeyService)hotkey).Released += (_, reason) => Log.Warning("Hotkey release missed ({Reason}); recording ended", reason);
            pipeline = host.Services.GetRequiredService<DictationPipeline>();
            recorder = host.Services.GetRequiredService<AudioRecorder>();
            speech = host.Services.GetRequiredService<ParakeetTranscriber>();
            speech.Diagnostic += (_, text) => Log.Information("{Diagnostic}", text);
            host.Services.GetRequiredService<ClipboardTextInserter>().Warning += (_, text) => Notify(text);
            statusWindow = new StatusWindow();
            statusWindow.SetHotkey(AppSettings.Hotkeys[settings.Hotkey]);
            statusWindow.ReadyDismissRequested += async (_, _) =>
            {
                try { settings = settings with { OnboardingComplete = true }; await settingsStore.SaveAsync(settings); statusWindow.Hide(); }
                catch (Exception ex) { Report(ex); }
            };
            overlay = new OverlayWindow();
            recorder.LevelChanged += (_, level) => overlay.SetLevel(level);
            statusWindow.RetryRequested += (_, _) => { if (initialization.IsCompleted && !ready) initialization = InitializeAsync(); };
            BuildTray();
            pipeline.StateChanged += (_, state) =>
            {
                tray!.ToolTipText = "Hushtype — " + state;
                if (state == DictationState.Recording) capTimer!.Start(); else capTimer!.Stop();
                switch (state)
                {
                    case DictationState.Recording: overlay.Listen(); cues.Play(start: true); break;
                    case DictationState.Transcribing: overlay.Transcribe(); break;
                    case DictationState.Inserting: overlay.Inserting(); break;
                    case DictationState.Idle: overlay.EndActivity(); break;
                }
            };
            pipeline.CaptureStopped += (_, _) => { if (!quitting && enabled) cues.Play(start: false); };
            pipeline.Completed += (_, _) => { if (!quitting && enabled) overlay.Done(); };
            pipeline.TranscriptReady += (_, record) => { if (settings.SaveHistory) historyWrites = SaveHistoryAsync(historyWrites, record); };
            pipeline.Completed += (_, metrics) => Log.Information("runtime={Runtime} audio_ms={AudioMs:F0} stt_ms={SttMs:F0} cleanup_ms={CleanupMs:F0} insert_ms={InsertMs:F0} release_to_paste_ms={TotalMs:F0}", speech.Runtime, metrics.AudioMs, metrics.SttMs, metrics.CleanupMs, metrics.InsertMs, metrics.ReleaseToPasteMs);
            hotkey.PressStarted += (_, _) => { if (!ready || !enabled || quitting || checkingDevice || changingSettings || pipeline.State != DictationState.Idle) return; try { utteranceCancellation?.Dispose(); utteranceCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); pipeline.Begin(); } catch (Exception ex) { Report(ex); } };
            hotkey.PressEnded += (_, _) => { if (!quitting && pipeline.State == DictationState.Recording) operation = FinishAsync(); };
            capTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            capTimer.Tick += (_, _) => { capTimer.Stop(); operation = FinishAsync(); };
            deviceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            deviceTimer.Tick += async (_, _) =>
            {
                if (!ready || !enabled || quitting || checkingDevice || changingSettings || pipeline.State != DictationState.Idle) return;
                checkingDevice = true;
                try { await Task.Run(recorder.RefreshDefaultDevice); }
                catch (Exception ex) { ready = false; hotkey.Stop(); Report(ex); }
                finally { checkingDevice = false; }
            };
            if (e.Args.Contains("--verify-overlay"))
            {
                await OverlayDiagnostics.RunAsync(overlay, directory);
                Exception? soundFailure = null;
                cues.Failed += (_, ex) => soundFailure = ex;
                await cues.PlayAsync(start: true);
                await cues.PlayAsync(start: false);
                if (soundFailure is not null) throw new InvalidOperationException("Sound playback diagnostic failed.", soundFailure);
                Log.Information("Sound diagnostic passed; enabled={Enabled}.", cues.Enabled);
                Log.Information("Overlay diagnostic passed: native styles, placement, state transitions, keyboard focus and rendered snapshots.");
                await QuitAsync();
                return;
            }
            if (e.Args.Contains("--verify-settings"))
            {
                await VerifySettingsAsync(); await QuitAsync(); return;
            }
            if (e.Args.Contains("--verify-hotkey"))
            {
                Log.Information("Hotkey diagnostic passed: {Result}", await HotkeyDiagnostics.RunAsync((HotkeyService)hotkey, Dispatcher));
                await QuitAsync();
                return;
            }
            if (e.Args.Contains("--smoke-test"))
            {
                hotkey.Start();
                hotkey.Stop();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Log.Information("Smoke test passed: WPF startup, DI graph, tray creation and keyboard hook lifecycle.");
                await QuitAsync();
                return;
            }
            if (e.Args.Contains("--probe-hardware"))
            {
                await Task.Run(recorder.Open);
                Log.Information("Microphone opened successfully at 16 kHz mono PCM16.");
                Log.Information("{Probe}", await Task.Run(ParakeetTranscriber.ProbeRuntime));
                await QuitAsync();
                return;
            }
            var verifyIndex = Array.IndexOf(e.Args, "--verify-speech");
            if (verifyIndex >= 0)
            {
                if (verifyIndex + 1 >= e.Args.Length) throw new ArgumentException("--verify-speech needs a WAV file path.");
                var progress = new Progress<ModelProgress>(p => Log.Information("{SetupProgress}", p.Message));
                await speech.InitializeAsync(progress, lifetime.Token);
                var samples = await Task.Run(() => WaveFileSamples.Read(e.Args[verifyIndex + 1]));
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var text = await speech.TranscribeAsync(samples, new("en"), lifetime.Token);
                Log.Information("Speech verification runtime={Runtime} duration_ms={Duration} stt_ms={Elapsed} text={Text}", speech.Runtime, samples.Length / 16.0, stopwatch.Elapsed.TotalMilliseconds, text);
                if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Speech verification produced no transcript.");
                await QuitAsync();
                return;
            }
            if (!e.Args.Contains("--background") || !settings.OnboardingComplete) statusWindow.Show();
            initialization = InitializeAsync();
            await initialization;
            if (ready && settings.AutomaticUpdates) updateTask = CheckUpdatesAsync(manual: false);
        }
        catch (Exception ex)
        {
            exitCode = 1;
            Log.Error(ex, "Startup failed");
            if (!e.Args.Any(arg => arg.StartsWith("--verify-") || arg is "--smoke-test" or "--probe-hardware")) MessageBox.Show(ex.Message, "Hushtype could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            await QuitAsync();
        }
    }
    private void BuildTray()
    {
        var menu = new ContextMenu();
        toggle = new MenuItem { Header = "Enabled", IsCheckable = true, IsChecked = true };
        toggle.Click += (_, _) =>
        {
            enabled = toggle.IsChecked;
            if (enabled && ready) hotkey!.Start();
            else { hotkey!.Stop(); utteranceCancellation?.Cancel(); overlay?.Dismiss(); try { pipeline!.CancelRecording(); } catch (Exception ex) { Report(ex); } }
            tray!.ToolTipText = enabled ? "Hushtype — ready" : "Hushtype — disabled";
        };
        menu.Items.Add(toggle);
        sounds = new MenuItem { Header = "Sounds", IsCheckable = true, IsChecked = cues.Enabled };
        sounds.Click += async (_, _) =>
        {
            cues.Enabled = sounds.IsChecked;
            sounds.IsEnabled = false;
            try
            {
                settings = settings with { Sounds = cues.Enabled };
                await settingsStore!.SaveAsync(settings);
            }
            catch (Exception ex) { Log.Warning(ex, "Sound preference could not be saved"); Notify("Sound preference applies for this session but could not be saved."); }
            finally { sounds.IsEnabled = true; }
        };
        menu.Items.Add(sounds);
        AddFeatureMenus(menu);
        var status = new MenuItem { Header = "Status / setup" };
        status.Click += (_, _) => { statusWindow!.Show(); statusWindow.Activate(); };
        menu.Items.Add(status);
        var quit = new MenuItem { Header = "Quit" };
        quit.Click += async (_, _) => await QuitAsync();
        menu.Items.Add(quit);
        trayIcon = Brand.TrayIcon();
        tray = new TaskbarIcon { ToolTipText = "Hushtype — starting", Icon = trayIcon, ContextMenu = menu };
        tray.ForceCreate(false);
    }
    private async Task InitializeAsync()
    {
        try
        {
            statusWindow!.Update("Opening microphone…", busy: true);
            await Task.Run(() => recorder!.SelectMicrophone(settings.Microphone), lifetime.Token);
            var progress = new Progress<ModelProgress>(p => statusWindow.Update(p.Message, p.Fraction, true));
            await speech!.InitializeAsync(progress, lifetime.Token);
            ready = true;
            statusWindow.Update($"Ready · {speech.Runtime.ToUpperInvariant()} · {speech.Model}\nYou can close this window. Hushtype stays in the tray.");
            tray!.ToolTipText = "Hushtype — hold " + AppSettings.Hotkeys[settings.Hotkey] + " to speak";
            if (enabled) hotkey!.Start();
            deviceTimer!.Start();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { ready = false; recorder?.Dispose(); Report(ex); }
    }
    private async Task FinishAsync()
    {
        try { await pipeline!.EndAsync(utteranceCancellation?.Token ?? lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested || utteranceCancellation?.IsCancellationRequested == true) { }
        catch (Exception ex) { Report(ex); }
    }
    private void Report(Exception ex)
    {
        Log.Error(ex, "Dictation error");
        statusWindow?.Update(ex.Message);
        Notify(ex.Message);
        if (!quitting) overlay?.Error(ex.Message);
    }
    private void Notify(string message)
    {
        Log.Warning("{Message}", message);
        tray?.ShowNotification("Hushtype", message, H.NotifyIcon.Core.NotificationIcon.Warning);
    }
    private async Task QuitAsync()
    {
        if (quitting) return;
        quitting = true;
        ready = false;
        capTimer?.Stop(); deviceTimer?.Stop(); hotkey?.Stop();
        overlay?.Dismiss(); cues.Dispose();
        lifetime.Cancel();
        try { pipeline?.CancelRecording(); } catch (Exception ex) { Log.Warning(ex, "Capture shutdown failed"); }
        await Task.WhenAll(initialization, operation);
        await historyWrites;
        await updateTask;
        while (checkingDevice || changingSettings) await Task.Delay(20);
        tray?.Dispose();
        trayIcon?.Dispose();
        overlay?.Close();
        if (host is not null) { await host.StopAsync(); host.Dispose(); }
        Log.CloseAndFlush();
        instance?.Dispose();
        Shutdown(exitCode);
    }
}
