using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Hushtype.App;

public sealed class StatusWindow : Window
{
    private readonly TextBlock status, tutorial;
    private readonly ProgressBar progress;
    private readonly Button finish;
    public event EventHandler? RetryRequested;
    public event EventHandler? ReadyDismissRequested;
    public StatusWindow()
    {
        WindowTheme.Apply(this, "Hushtype · Welcome", 550, 570);
        var panel = new StackPanel { Margin = new Thickness(32) };
        panel.Children.Add(new Image { Source = Brand.Icon, Width = 58, Height = 58, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,0,0,20) });
        panel.Children.Add(WindowTheme.Text("Your voice, right where you work.", 25));
        panel.Children.Add(WindowTheme.Text("English dictation that runs on your computer. After the speech model is downloaded, no internet connection is needed."));
        tutorial = WindowTheme.Text("1. Click a text field.\n2. Hold Right Ctrl and speak.\n3. Release to paste.", 16); panel.Children.Add(tutorial);
        status = WindowTheme.Text("Preparing Hushtype…"); status.MinHeight = 48; panel.Children.Add(status);
        progress = new ProgressBar { Height = 5, Minimum = 0, Maximum = 1, IsIndeterminate = true, Margin = new Thickness(0,0,0,20) }; panel.Children.Add(progress);
        var buttons = new WrapPanel(); panel.Children.Add(buttons);
        finish = new Button { Content = "Start dictating", IsEnabled = false };
        finish.Click += (_, _) => ReadyDismissRequested?.Invoke(this, EventArgs.Empty); buttons.Children.Add(finish);
        var retry = new Button { Content = "Retry setup" }; retry.Click += (_, _) => RetryRequested?.Invoke(this, EventArgs.Empty); buttons.Children.Add(retry);
        var permissions = new Button { Content = "Microphone access", Margin = new Thickness(0,12,0,0), HorizontalAlignment = HorizontalAlignment.Left };
        permissions.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true }); } catch (Exception ex) { status.Text = ex.Message; } }; panel.Children.Add(permissions);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }
    public void Update(string message, double? fraction = null, bool busy = false)
    {
        status.Text = message; progress.IsIndeterminate = busy && fraction is null;
        progress.Value = fraction ?? (busy ? 0 : 1); finish.IsEnabled = !busy && message.StartsWith("Ready");
    }
    public void SetHotkey(string name) => tutorial.Text = $"1. Click a text field.\n2. Hold {name} and speak.\n3. Release to paste.";
}