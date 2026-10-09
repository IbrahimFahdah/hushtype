using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hushtype.Audio;
using Hushtype.Cleanup;
using Hushtype.Core;
using Hushtype.Data;
using Hushtype.Platform;
using Serilog;

namespace Hushtype.App;

public partial class App
{
    private AppSettings settings = new();
    private SettingsStore? settingsStore;
    private LocalStore? localStore;
    private readonly PersonalPostProcessor personalCleaner = new();
    private string dataDirectory = "";
    private bool customDataDirectory, changingSettings;
    private Task historyWrites = Task.CompletedTask;
    private MenuItem? sounds;
    private SettingsWindow? settingsWindow;
    private LibraryWindow? libraryWindow;

    private Task RefreshCorrectionsAsync() => RefreshCore();
    private async Task RefreshCore() => personalCleaner.SetCorrections(await localStore!.DictionaryAsync());
    private async Task SaveHistoryAsync(Task previous, DictationRecord record)
    {
        await previous;
        try { await localStore!.AddHistoryAsync(record); }
        catch (Exception ex) { Log.Warning(ex, "Could not save history"); Notify("Dictation worked, but this entry could not be saved to history."); }
    }

    private void AddFeatureMenus(ContextMenu menu)
    {
        var preferences = new MenuItem { Header = "Settings" };
        preferences.Click += async (_, _) =>
        {
            try
            {
                if (settingsWindow is not null) { settingsWindow.Activate(); return; }
                var devices = await Task.Run(AudioRecorder.Microphones);
                settingsWindow = new SettingsWindow(settings, $"CPU · {speech?.Model ?? "loading"}", devices, updateSource is not null, ApplySettingsAsync);
                settingsWindow.Closed += (_, _) => settingsWindow = null;
                settingsWindow.Show();
            }
            catch (Exception ex) { Report(ex); }
        };
        menu.Items.Add(preferences);
        foreach (var dictionary in new[] { false, true })
        {
            var item = new MenuItem { Header = dictionary ? "Dictionary" : "History" };
            item.Click += async (_, _) =>
            {
                if (libraryWindow is not null) { libraryWindow.Activate(); return; }
                await historyWrites;
                libraryWindow = new LibraryWindow(localStore!, RefreshCorrectionsAsync, PasteHistoryAsync, dictionary);
                libraryWindow.Closed += (_, _) => libraryWindow = null;
                libraryWindow.Show();
            };
            menu.Items.Add(item);
        }
        ConfigureUpdates(menu);
    }

    private async Task ApplySettingsAsync(AppSettings next)
    {
        next.Validate();
        if (quitting || changingSettings || checkingDevice || !initialization.IsCompleted || pipeline?.State != DictationState.Idle)
            throw new InvalidOperationException("Finish the current recording or setup before changing settings.");
        changingSettings = true;
        var previous = settings;
        hotkey!.Stop();
        try
        {
            if (next.Microphone != previous.Microphone || !ready) await Task.Run(() => recorder!.SelectMicrophone(next.Microphone));
            ((HotkeyService)hotkey).Configure(next.Hotkey);
            if (next.LaunchAtStartup != previous.LaunchAtStartup)
                StartupRegistration.Set(next.LaunchAtStartup, Environment.ProcessPath!, customDataDirectory ? dataDirectory : null);
            await settingsStore!.SaveAsync(next);
            settings = next; cues.Enabled = next.Sounds; inserter!.TypeShortText = next.InsertMode == "type"; sounds!.IsChecked = next.Sounds; personalCleaner.UseRules = next.Cleanup;
            statusWindow!.SetHotkey(AppSettings.Hotkeys[next.Hotkey]);
            tray!.ToolTipText = "Hushtype — hold " + AppSettings.Hotkeys[next.Hotkey] + " to speak";
            if (!ready && speech?.Runtime == "cpu") { ready = true; deviceTimer?.Start(); }
        }
        catch
        {
            ((HotkeyService)hotkey).Configure(previous.Hotkey);
            if (next.Microphone != previous.Microphone)
                try { await Task.Run(() => recorder!.SelectMicrophone(previous.Microphone)); } catch { ready = false; }
            if (next.LaunchAtStartup != previous.LaunchAtStartup)
                try { StartupRegistration.Set(previous.LaunchAtStartup, Environment.ProcessPath!, customDataDirectory ? dataDirectory : null); } catch (Exception ex) { Log.Warning(ex, "Startup preference rollback failed"); }
            throw;
        }
        finally { changingSettings = false; if (enabled && ready && !quitting) hotkey.Start(); }
    }

    private async Task PasteHistoryAsync(string text)
    {
        if (pipeline?.State != DictationState.Idle || changingSettings) throw new InvalidOperationException("Finish dictation before pasting history.");
        changingSettings = true; hotkey!.Stop();
        try
        {
            Notify("Focus the target text field. Saved text will paste in three seconds.");
            await Task.Delay(TimeSpan.FromSeconds(3), lifetime.Token);
            var target = host!.Services.GetService(typeof(IForegroundAppProvider)) as IForegroundAppProvider;
            var context = target!.GetCurrent();
            await ((ITextInserter)host.Services.GetService(typeof(ITextInserter))!).InsertAsync(text, context, lifetime.Token);
        }
        finally { changingSettings = false; if (enabled && ready && !quitting) hotkey.Start(); }
    }
}
