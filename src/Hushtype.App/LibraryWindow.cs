using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Hushtype.Cleanup;
using Hushtype.Core;
using Hushtype.Data;

namespace Hushtype.App;

public sealed class LibraryWindow : Window
{
    private readonly TextBlock status = WindowTheme.Text("");
    public LibraryWindow(LocalStore store, Func<Task> refreshCorrections, Func<string, Task> paste, bool dictionary)
    {
        WindowTheme.Apply(this, "Hushtype · History & dictionary", 820, 680);
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
        // Queries run off the UI thread in LocalStore; typing is debounced.
        TextBox SearchBox(DockPanel panel, Func<Task> reload)
        {
            var box = new TextBox { Margin = new Thickness(0, 0, 0, 12), ToolTip = "Search" };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += async (_, _) => { timer.Stop(); try { await reload(); } catch (Exception ex) { status.Text = ex.Message; } };
            box.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
            var label = WindowTheme.Text("Search"); label.Margin = new Thickness(0, 0, 0, 4);
            DockPanel.SetDock(label, Dock.Top); panel.Children.Add(label);
            DockPanel.SetDock(box, Dock.Top); panel.Children.Add(box);
            return box;
        }
        // The grid and an empty-state message share one cell; only one is visible.
        Grid WithEmptyState(DataGrid grid, out TextBlock empty)
        {
            empty = WindowTheme.Text(""); empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center; empty.Foreground = Brushes.Gray;
            var cell = new Grid(); cell.Children.Add(grid); cell.Children.Add(empty); return cell;
        }
        static void ShowEmpty(TextBlock empty, int count, string search) =>
            (empty.Text, empty.Visibility) = (string.IsNullOrWhiteSpace(search) ? "Nothing here yet" : "No matches", count == 0 ? Visibility.Visible : Visibility.Collapsed);

        var history = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, CanUserAddRows = false, MinHeight = 220,
            RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.Visible, RowDetailsTemplate = BadgeTemplate() };
        history.Columns.Add(new DataGridTextColumn { Header = "When", Binding = new Binding("Record.CreatedAt.LocalDateTime") { StringFormat = "g" }, Width = 150 });
        history.Columns.Add(new DataGridTextColumn { Header = "App", Binding = new Binding("Record.App"), Width = 110 });
        history.Columns.Add(new DataGridTextColumn { Header = "Text", Binding = new Binding("Record.FinalText"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var historyPanel = new DockPanel { Margin = new Thickness(12) };
        var historyHelp = WindowTheme.Text("Saved locally when history is enabled in Settings. Includes processed dictation even if the target refused the paste. Badges show which dictionary corrections were applied."); DockPanel.SetDock(historyHelp, Dock.Top); historyPanel.Children.Add(historyHelp);
        TextBox? historySearch = null;
        var historyButtons = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) };
        var details = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        history.SelectionChanged += (_, _) => { if (history.SelectedItem is HistoryEntry entry) details.Text = $"Original: {entry.Record.RawText}\nFinal: {entry.Record.FinalText}"; };
        HistoryEntry Selected() => history.SelectedItem as HistoryEntry ?? throw new InvalidOperationException("Select a history entry.");
        var historyCell = WithEmptyState(history, out var historyEmpty);
        async Task ReloadHistory()
        {
            var search = historySearch?.Text ?? "";
            var items = await store.HistoryAsync(search);
            history.ItemsSource = items; details.Clear(); ShowEmpty(historyEmpty, items.Count, search);
        }
        historySearch = SearchBox(historyPanel, ReloadHistory);
        DockPanel.SetDock(historyButtons, Dock.Bottom); historyPanel.Children.Add(historyButtons);
        DockPanel.SetDock(details, Dock.Bottom); historyPanel.Children.Add(details); historyPanel.Children.Add(historyCell);
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
        edit.Children.Add(WindowTheme.Text("Correct names and technical terms after recognition. Match whole words or phrases; longer matches win. Multi-word phrases also match when run together or hyphenated."));
        var heard = new TextBox { MaxLength = 200 }; var replacement = new TextBox { MaxLength = 200 };
        WindowTheme.Field(edit, "When Hushtype writes", heard); WindowTheme.Field(edit, "Replace it with", replacement);
        var correctionButtons = new WrapPanel(); edit.Children.Add(correctionButtons);
        var entries = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single };
        TextBox? dictionarySearch = null;
        var entriesCell = WithEmptyState(entries, out var dictionaryEmpty);
        async Task ReloadDictionary()
        {
            var search = dictionarySearch?.Text ?? "";
            var items = await store.DictionaryAsync(search);
            entries.ItemsSource = items; ShowEmpty(dictionaryEmpty, items.Count, search); await refreshCorrections();
        }
        dictionarySearch = SearchBox(dictionaryPanel, ReloadDictionary);
        var toggle = new FrameworkElementFactory(typeof(CheckBox));
        toggle.SetBinding(CheckBox.IsCheckedProperty, new Binding("Enabled") { Mode = BindingMode.OneWay });
        toggle.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        toggle.SetValue(ToolTipProperty, "Turn this correction on or off");
        toggle.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler(async (sender, _) =>
        {
            var box = (CheckBox)sender;
            if (box.DataContext is not DictionaryCorrection entry) return;
            await Work(async () => { await store.SetCorrectionEnabledAsync(entry.Heard, box.IsChecked == true); await ReloadDictionary(); status.Text = box.IsChecked == true ? "Correction on." : "Correction off; it stays listed."; });
        }));
        entries.Columns.Add(new DataGridTemplateColumn { Header = "On", CellTemplate = new DataTemplate { VisualTree = toggle }, Width = 44 });
        entries.Columns.Add(new DataGridTextColumn { Header = "Recognized phrase", Binding = new Binding("Heard"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        entries.Columns.Add(new DataGridTextColumn { Header = "Replacement", Binding = new Binding("Replacement"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dictionaryPanel.Children.Add(entriesCell);
        entries.SelectionChanged += (_, _) => { if (entries.SelectedItem is DictionaryCorrection entry) { heard.Text = entry.Heard; replacement.Text = entry.Replacement; } };
        Button(correctionButtons, "Save correction", async () =>
        {
            var phrase = heard.Text.Trim();
            // Editing an existing entry keeps its on/off state.
            var existing = (await store.DictionaryAsync()).FirstOrDefault(x => string.Equals(x.Heard, phrase, StringComparison.OrdinalIgnoreCase));
            await store.SaveCorrectionAsync(new(phrase, replacement.Text.Trim(), existing?.Enabled ?? true)); await ReloadDictionary();
            var warnings = CorrectionWarnings.For(phrase, replacement.Text);
            status.Text = "Correction saved and active for the next dictation." + (warnings.Count > 0 ? " Note: " + string.Join(" ", warnings) : "");
        });
        Button(correctionButtons, "Remove selected", async () => { if (entries.SelectedItem is not DictionaryCorrection entry) throw new InvalidOperationException("Select a correction."); await store.DeleteCorrectionAsync(entry.Heard); await ReloadDictionary(); });
        Button(correctionButtons, "Import…", async () =>
        {
            var dialog = new OpenFileDialog { Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*", Title = "Import dictionary" };
            if (dialog.ShowDialog(this) != true) return;
            var imported = DictionaryText.Parse(await File.ReadAllTextAsync(dialog.FileName));
            await store.SaveCorrectionsAsync(imported); await ReloadDictionary();
            status.Text = $"Imported {imported.Count} correction(s). Existing phrases were updated.";
        });
        Button(correctionButtons, "Export…", async () =>
        {
            var dialog = new SaveFileDialog { Filter = "Text files (*.txt)|*.txt", FileName = "hushtype-dictionary.txt", Title = "Export dictionary" };
            if (dialog.ShowDialog(this) != true) return;
            var all = await store.DictionaryAsync();
            await File.WriteAllTextAsync(dialog.FileName, DictionaryText.Format(all));
            status.Text = $"Exported {all.Count} correction(s).";
        });
        tabs.Items.Add(new TabItem { Header = "Dictionary", Content = dictionaryPanel }); tabs.SelectedIndex = dictionary ? 1 : 0;
        Loaded += async (_, _) => await Work(async () => { await ReloadHistory(); await ReloadDictionary(); });
    }
    // Small badges listing the corrections applied to a history entry; nothing when none fired.
    private static DataTemplate BadgeTemplate()
    {
        var list = new FrameworkElementFactory(typeof(ItemsControl));
        list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Record.Corrections"));
        list.SetValue(MarginProperty, new Thickness(260, 0, 0, 4)); // under the Text column
        var wrap = new FrameworkElementFactory(typeof(WrapPanel));
        list.SetValue(ItemsControl.ItemsPanelProperty, new ItemsPanelTemplate(wrap));
        var badge = new FrameworkElementFactory(typeof(Border));
        badge.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(226, 238, 232)));
        badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        badge.SetValue(Border.PaddingProperty, new Thickness(7, 1, 7, 1));
        badge.SetValue(MarginProperty, new Thickness(0, 0, 6, 0));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding());
        text.SetValue(TextBlock.FontSizeProperty, 11.0);
        badge.AppendChild(text);
        list.SetValue(ItemsControl.ItemTemplateProperty, new DataTemplate { VisualTree = badge });
        return new DataTemplate { VisualTree = list };
    }
}
