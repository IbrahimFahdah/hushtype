using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Hushtype.Core;
using Hushtype.Data;

namespace Hushtype.App;

public sealed class LibraryWindow : Window
{
    private readonly TextBlock status = WindowTheme.Text("");
    public LibraryWindow(LocalStore store, Func<Task> refreshCorrections, Func<string, Task> paste, bool dictionary)
    {
        WindowTheme.Apply(this, "Hushtype · History & dictionary", 820, 640);
        var root = new DockPanel { Margin = new Thickness(24) };
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var tabs = new TabControl(); root.Children.Add(tabs); Content = root;
        async Task Work(Func<Task> action)
        {
            tabs.IsEnabled = false;
            try { await action(); }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { tabs.IsEnabled = true; }
        }
        var history = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, CanUserAddRows = false, MinHeight = 220 };
        history.Columns.Add(new DataGridTextColumn { Header = "When", Binding = new Binding("Record.CreatedAt.LocalDateTime") { StringFormat = "g" }, Width = 150 });
        history.Columns.Add(new DataGridTextColumn { Header = "App", Binding = new Binding("Record.App"), Width = 110 });
        history.Columns.Add(new DataGridTextColumn { Header = "Text", Binding = new Binding("Record.FinalText"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var historyPanel = new DockPanel { Margin = new Thickness(12) };
        var historyHelp = WindowTheme.Text("Saved locally when history is enabled in Settings. Includes processed dictation even if the target refused the paste."); DockPanel.SetDock(historyHelp, Dock.Top); historyPanel.Children.Add(historyHelp);
        var historyButtons = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) }; DockPanel.SetDock(historyButtons, Dock.Bottom); historyPanel.Children.Add(historyButtons);
        var details = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(details, Dock.Bottom); historyPanel.Children.Add(details); historyPanel.Children.Add(history);
        history.SelectionChanged += (_, _) => { if (history.SelectedItem is HistoryEntry entry) details.Text = $"Original: {entry.Record.RawText}\nFinal: {entry.Record.FinalText}"; };
        HistoryEntry Selected() => history.SelectedItem as HistoryEntry ?? throw new InvalidOperationException("Select a history entry.");
        async Task ReloadHistory() { history.ItemsSource = await store.HistoryAsync(); details.Clear(); }
        void Button(Panel panel, string title, Func<Task> action)
        { var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 8) }; button.Click += async (_, _) => await Work(action); panel.Children.Add(button); }
        Button(historyButtons, "Refresh", ReloadHistory);
        Button(historyButtons, "Copy", () => { Clipboard.SetText(Selected().Record.FinalText); status.Text = "Copied."; return Task.CompletedTask; });
        Button(historyButtons, "Paste in 3 seconds", async () => { var text = Selected().Record.FinalText; Hide(); try { await paste(text); } finally { Show(); } });
        Button(historyButtons, "Delete selected", async () => { await store.DeleteHistoryAsync(Selected().Id); await ReloadHistory(); });
        Button(historyButtons, "Clear history", async () => { if (MessageBox.Show(this, "Delete all saved dictation?", "Clear history", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { await store.DeleteHistoryAsync(); await ReloadHistory(); } });
        tabs.Items.Add(new TabItem { Header = "History", Content = historyPanel });

        var dictionaryPanel = new DockPanel { Margin = new Thickness(12) };
        var edit = new StackPanel(); DockPanel.SetDock(edit, Dock.Top); dictionaryPanel.Children.Add(edit);
        edit.Children.Add(WindowTheme.Text("Correct names and technical terms after recognition. Match whole words or phrases; longer matches win."));
        var heard = new TextBox { MaxLength = 200 }; var replacement = new TextBox { MaxLength = 200 };
        WindowTheme.Field(edit, "When Hushtype writes", heard); WindowTheme.Field(edit, "Replace it with", replacement);
        var correctionButtons = new WrapPanel(); edit.Children.Add(correctionButtons);
        var entries = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single };
        entries.Columns.Add(new DataGridTextColumn { Header = "Recognized phrase", Binding = new Binding("Heard"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        entries.Columns.Add(new DataGridTextColumn { Header = "Replacement", Binding = new Binding("Replacement"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dictionaryPanel.Children.Add(entries);
        entries.SelectionChanged += (_, _) => { if (entries.SelectedItem is DictionaryCorrection entry) { heard.Text = entry.Heard; replacement.Text = entry.Replacement; } };
        async Task ReloadDictionary() { entries.ItemsSource = await store.DictionaryAsync(); await refreshCorrections(); }
        Button(correctionButtons, "Save correction", async () => { await store.SaveCorrectionAsync(new(heard.Text.Trim(), replacement.Text.Trim())); await ReloadDictionary(); status.Text = "Correction saved and active for the next dictation."; });
        Button(correctionButtons, "Remove selected", async () => { if (entries.SelectedItem is not DictionaryCorrection entry) throw new InvalidOperationException("Select a correction."); await store.DeleteCorrectionAsync(entry.Heard); await ReloadDictionary(); });
        tabs.Items.Add(new TabItem { Header = "Dictionary", Content = dictionaryPanel }); tabs.SelectedIndex = dictionary ? 1 : 0;
        Loaded += async (_, _) => await Work(async () => { await ReloadHistory(); await ReloadDictionary(); });
    }
}
