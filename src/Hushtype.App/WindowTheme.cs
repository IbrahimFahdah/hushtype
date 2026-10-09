using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hushtype.App;

internal static class WindowTheme
{
    public static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(25, 29, 36));
    public static void Apply(Window window, string title, double width, double height)
    {
        window.Title = title; window.Width = width; window.Height = height;
        window.MinWidth = Math.Min(width, 540); window.MinHeight = Math.Min(height, 400);
        window.Background = new SolidColorBrush(Color.FromRgb(246, 248, 247)); window.Foreground = Ink;
        window.FontFamily = new FontFamily("Segoe UI"); window.FontSize = 14;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Icon = Brand.Icon;
        var button = new Style(typeof(Button)); button.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 7, 14, 7)));
        button.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0)));
        window.Resources.Add(typeof(Button), button);
        var box = new Style(typeof(TextBox)); box.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        window.Resources.Add(typeof(TextBox), box);
    }
    public static TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    public static void Field(Panel panel, string label, UIElement control)
    { panel.Children.Add(Text(label)); panel.Children.Add(control); panel.Children.Add(new Border { Height = 14 }); }
}
