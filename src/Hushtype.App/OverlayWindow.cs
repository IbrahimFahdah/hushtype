using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Hushtype.Platform;

namespace Hushtype.App;

public sealed class OverlayWindow : Window
{
    private readonly Border pill;
    private readonly TextBlock label;
    private readonly TextBlock symbol;
    private readonly StackPanel waveform;
    private readonly Rectangle[] bars;
    private readonly DispatcherTimer animation;
    private readonly DispatcherTimer dismiss;
    private readonly float[] history = new float[19];
    private float latestLevel;
    private int frame;
    private bool listening, processing, feedback;
    private nint target;
    private static readonly Brush Mint = new SolidColorBrush(Color.FromRgb(89, 218, 173));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(255, 142, 150));

    public OverlayWindow()
    {
        Width = 300; Height = 68;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowActivated = false; ShowInTaskbar = false; Focusable = false;
        IsHitTestVisible = false; UseLayoutRounding = true;
        var content = new Grid { Margin = new Thickness(18, 0, 18, 0) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        symbol = new TextBlock { Text = "●", FontSize = 19, Foreground = Mint, VerticalAlignment = VerticalAlignment.Center };
        label = new TextBlock { Text = "Listening", FontSize = 13, FontWeight = FontWeights.Medium, Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(label, 1);
        waveform = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        bars = Enumerable.Range(0, history.Length).Select(_ => new Rectangle { Width = 3, Height = 3, RadiusX = 1.5, RadiusY = 1.5,
            Fill = Mint, Margin = new Thickness(1, 0, 1, 0), VerticalAlignment = VerticalAlignment.Center }).ToArray();
        foreach (var bar in bars) waveform.Children.Add(bar);
        Grid.SetColumn(waveform, 2);
        content.Children.Add(symbol); content.Children.Add(label); content.Children.Add(waveform);
        pill = new Border { Background = new SolidColorBrush(Color.FromRgb(25, 29, 36)), BorderBrush = new SolidColorBrush(Color.FromRgb(62, 72, 81)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(26), Margin = new Thickness(2, 8, 2, 8), Child = content };
        Content = pill;
        animation = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        animation.Tick += (_, _) => Animate();
        dismiss = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        dismiss.Tick += (_, _) => Dismiss();
        SourceInitialized += (_, _) => OverlayWindowBehavior.Attach(this);
        Closed += (_, _) => { animation.Stop(); dismiss.Stop(); };
    }

    // Audio callback only writes a single latest value; the UI samples it at 30 Hz.
    public void SetLevel(float level) => Volatile.Write(ref latestLevel, float.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0);

    public void Listen()
    {
        target = OverlayWindowBehavior.ForegroundWindow;
        Array.Clear(history); SetLevel(0);
        listening = true; processing = false; feedback = false;
        Present("Listening", "●", Mint, true);
    }

    public void Transcribe()
    {
        listening = false; processing = true; feedback = false;
        Present("Transcribing", "◌", Mint, false);
    }

    public void Inserting() { label.Text = "Pasting"; }

    public void Done()
    {
        listening = processing = false; feedback = true;
        Present("Done", "✓", Mint, false);
        dismiss.Interval = TimeSpan.FromMilliseconds(900); dismiss.Start();
    }

    public void Error(string message)
    {
        if (!IsVisible) target = OverlayWindowBehavior.ForegroundWindow;
        listening = processing = false; feedback = true;
        Present("Dictation error — see tray", "!", Red, false);
        label.ToolTip = message; // Full details are also available in the tray notification/status window.
        dismiss.Interval = TimeSpan.FromSeconds(3); dismiss.Start();
    }

    public void EndActivity() { if (!feedback) Dismiss(); }
    public void Dismiss()
    {
        dismiss.Stop(); animation.Stop(); listening = processing = feedback = false;
        Hide();
    }

    private void Present(string text, string glyph, Brush accent, bool showWaveform)
    {
        dismiss.Stop(); label.Text = text; label.ToolTip = null; symbol.Text = glyph; symbol.Foreground = accent;
        symbol.Opacity = 1; pill.BorderBrush = accent;
        waveform.Visibility = showWaveform ? Visibility.Visible : Visibility.Collapsed;
        OverlayWindowBehavior.Position(this, target);
        if (!IsVisible)
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(100)));
            Show();
        }
        if (listening || processing) animation.Start(); else animation.Stop();
    }

    private void Animate()
    {
        if (listening)
        {
            Array.Copy(history, 1, history, 0, history.Length - 1);
            history[^1] = Math.Clamp(Volatile.Read(ref latestLevel) * 9, 0, 1);
            for (var i = 0; i < bars.Length; i++) bars[i].Height = 3 + 25 * Math.Sqrt(history[i]);
        }
        else if (processing) symbol.Opacity = 0.45 + 0.55 * (1 + Math.Sin(++frame * 0.16)) / 2;
    }
}
