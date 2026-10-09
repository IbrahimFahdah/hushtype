using System.Windows;
using System.Windows.Media.Imaging;

namespace Hushtype.App;

internal static class Brand
{
    public static readonly Uri IconUri = new("pack://application:,,,/Assets/hushtype.ico");
    public static BitmapFrame Icon { get; } = BitmapDecoder.Create(IconUri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad)
        .Frames.OrderByDescending(frame => frame.PixelWidth).First();
    public static System.Drawing.Icon TrayIcon()
    {
        using var stream = Application.GetResourceStream(IconUri)!.Stream;
        using var icon = new System.Drawing.Icon(stream);
        return (System.Drawing.Icon)icon.Clone();
    }
}
