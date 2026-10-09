using System.Windows;
using System.Windows.Controls;
using Hushtype.Audio;
using Hushtype.Core;

namespace Hushtype.App;

public sealed class SettingsWindow : Window
{
    public SettingsWindow(AppSettings current, string runtime, IReadOnlyList<AudioRecorder.MicrophoneChoice> microphones, bool updateConfigured,
        Func<AppSettings, Task> apply)
    {
        WindowTheme.Apply(this, "Hushtype · Settings", 590, 760);
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(WindowTheme.Text("Make it yours", 28));
        panel.Children.Add(WindowTheme.Text(runtime + " · English only"));
        var hotkey = new ComboBox { ItemsSource = AppSettings.Hotkeys, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = current.Hotkey };
        WindowTheme.Field(panel, "Hold to dictate", hotkey);
        var altGr = WindowTheme.Text("Right Alt is AltGr on German, Polish, UK, Nordic and many other layouts. Hushtype blocks it while running, so characters such as @, €, \\ and | can't be typed. Right Ctrl is recommended.");
        altGr.Margin = new Thickness(0, -6, 0, 14);
        void ShowAltGr() => altGr.Visibility = hotkey.SelectedValue is 0xA5 ? Visibility.Visible : Visibility.Collapsed;
        hotkey.SelectionChanged += (_, _) => ShowAltGr(); ShowAltGr();
        panel.Children.Add(altGr);
        var microphone = new ComboBox { ItemsSource = microphones, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = current.Microphone };
        WindowTheme.Field(panel, "Microphone", microphone);
        var insertModes = new Dictionary<string, string> { ["paste"] = "Paste (default)", ["type"] = "Type short text, paste longer text" };
        var insert = new ComboBox { ItemsSource = insertModes, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = current.InsertMode };
        WindowTheme.Field(panel, "Insert text by", insert);
        var precision = new ComboBox { ItemsSource = new[] { "int8", "fp32" }, SelectedItem = current.Precision };
        WindowTheme.Field(panel, "Speech model precision (applies next launch)", precision);
        var threads = new TextBox { Text = current.Threads.ToString() };
        WindowTheme.Field(panel, "CPU threads, 1–20 (applies next launch)", threads);
        CheckBox Choice(string text, bool value) { var check = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 0, 0, 14) }; panel.Children.Add(check); return check; }
        var sounds = Choice("Play start and stop sounds", current.Sounds);
        var cleanup = Choice("Remove fillers and tidy punctuation", current.Cleanup);
        var history = Choice("Save dictation history on this computer (latest 500)", current.SaveHistory);
        var startup = Choice("Launch when I sign in to Windows", current.LaunchAtStartup);
        var updates = Choice("Check for app updates automatically", current.AutomaticUpdates); updates.IsEnabled = updateConfigured;
        if (!updateConfigured) panel.Children.Add(WindowTheme.Text("App update hosting is not configured for this build."));
        panel.Children.Add(WindowTheme.Text("Typing sends up to 200 characters of single-line text as keystrokes and leaves the clipboard untouched; longer text is pasted. Hotkey, microphone, insert mode, cleanup and sound changes apply immediately. Dictionary corrections stay active when cleanup is off."));
        var result = WindowTheme.Text(""); panel.Children.Add(result);
        var save = new Button { Content = "Save settings", HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                if (!int.TryParse(threads.Text, out var count)) throw new ArgumentException("Enter a whole number for CPU threads.");
                if (microphone.SelectedValue is not string mic) throw new ArgumentException("Select an available microphone.");
                var next = current with { Hotkey = (int)hotkey.SelectedValue, Microphone = mic, Precision = (string)precision.SelectedItem,
                    Threads = count, Sounds = sounds.IsChecked == true, Cleanup = cleanup.IsChecked == true, SaveHistory = history.IsChecked == true,
                    LaunchAtStartup = startup.IsChecked == true, InsertMode = (string)insert.SelectedValue, AutomaticUpdates = updateConfigured && updates.IsChecked == true };
                next.Validate(); await apply(next); current = next;
                result.Text = "Saved. Hotkey and microphone are ready now. Model changes apply next launch.";
            }
            catch (Exception ex) { result.Text = ex.Message; }
            finally { save.IsEnabled = true; }
        };
        panel.Children.Add(save);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
