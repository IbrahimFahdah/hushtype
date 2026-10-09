using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hushtype.Audio;
using Hushtype.Core;
using Hushtype.Platform;
using Hushtype.Data;
using Serilog;

namespace Hushtype.App;

public partial class App
{
    private async Task VerifySettingsAsync()
    {
        var previous = settings;
        var productionStore = localStore;
        localStore = new LocalStore(Path.Combine(dataDirectory, "feature-diagnostic-" + Guid.NewGuid().ToString("N") + ".db"));
        await localStore.InitializeAsync();
        enabled = false;
        await Task.Run(() => recorder!.SelectMicrophone(previous.Microphone));
        ready = true;
        var microphones = await Task.Run(AudioRecorder.Microphones);
        var selected = microphones.Last().Id;
        try
        {
            await ApplySettingsAsync(previous with { Hotkey = 0x78, Microphone = selected, Sounds = false });
            if (((HotkeyService)hotkey!).Key != 0x78 || recorder!.SelectedMicrophone != selected || cues.Enabled)
                throw new InvalidOperationException("Live settings did not apply.");
            hotkey.Start(); hotkey.Stop();
            var saved = await settingsStore!.LoadAsync();
            if (saved.Hotkey != 0x78 || saved.Microphone != selected) throw new InvalidOperationException("Settings not persisted.");
            var window = new SettingsWindow(settings, "CPU · Parakeet v2 INT8", microphones, false, ApplySettingsAsync);
            window.Show(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            SaveWindowSnapshot(window, Path.Combine(dataDirectory, "settings.png")); window.Close();
            statusWindow!.Update("Ready · English dictation is available. Hushtype stays in your tray.");
            statusWindow.Show(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            SaveWindowSnapshot(statusWindow, Path.Combine(dataDirectory, "welcome.png")); statusWindow.Hide();
            await localStore!.SaveCorrectionAsync(new("cube cuddle", "kubectl"));
            await RefreshCorrectionsAsync();
            var corrected = await personalCleaner.ProcessAsync("use cube cuddle", new("diagnostic", "", 0), default);
            if (corrected != "Use kubectl.") throw new InvalidOperationException("Saved dictionary correction not applied.");
            await localStore.AddHistoryAsync(new(DateTimeOffset.UtcNow, "diagnostic", "use cube cuddle", corrected, 1000, 50, 1));
            var library = new LibraryWindow(localStore, RefreshCorrectionsAsync, _ => Task.CompletedTask, false);
            library.Show(); await Task.Delay(300);
            SaveWindowSnapshot(library, Path.Combine(dataDirectory, "history.png")); library.Close();
            library = new LibraryWindow(localStore, RefreshCorrectionsAsync, _ => Task.CompletedTask, true);
            library.Show(); await Task.Delay(300);
            SaveWindowSnapshot(library, Path.Combine(dataDirectory, "dictionary.png")); library.Close();
            await localStore.DeleteCorrectionAsync("cube cuddle"); await localStore.DeleteHistoryAsync(); await RefreshCorrectionsAsync();
            Log.Information("Settings diagnostic passed: hotkey changed to F9, microphone switched to {Device}, capture reopened, JSON reloaded and settings rendered.", selected);
        }
        finally { localStore = productionStore; await RefreshCorrectionsAsync(); await ApplySettingsAsync(previous); ready = false; }
    }

    private static void SaveWindowSnapshot(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
