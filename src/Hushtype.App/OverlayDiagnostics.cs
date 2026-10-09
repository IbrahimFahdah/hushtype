using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hushtype.Platform;

namespace Hushtype.App;

// Explicit desktop test: creates and types only into its own disposable editor.
internal static class OverlayDiagnostics
{
    public static async Task RunAsync(OverlayWindow overlay, string directory)
    {
        var editor = new TextBox { AcceptsReturn = true, FontSize = 20, Margin = new Thickness(24) };
        var target = new Window { Title = "Hushtype overlay focus diagnostic", Width = 640, Height = 300, Content = editor };
        try
        {
            target.Show(); target.Activate(); editor.Focus();
            await Task.Delay(250);
            var handle = new WindowInteropHelper(target).Handle;
            void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
            Check(OverlayWindowBehavior.ForegroundWindow == handle,
                $"Diagnostic editor could not acquire foreground before overlay test (editor={handle}, foreground={OverlayWindowBehavior.ForegroundWindow}).");
            async Task Type(char character)
            {
                Check(OverlayWindowBehavior.ForegroundWindow == handle, $"Foreground changed during overlay test (editor={handle}, foreground={OverlayWindowBehavior.ForegroundWindow}, overlay={new WindowInteropHelper(overlay).Handle}).");
                OverlayWindowBehavior.TypeDiagnosticCharacter(handle, character);
                await Task.Delay(100);
                Check(editor.Text.EndsWith(character), "Keyboard input did not reach the diagnostic editor.");
            }
            void Snapshot(string name)
            {
                overlay.UpdateLayout();
                var bitmap = new RenderTargetBitmap(300, 68, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(overlay);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, "overlay-" + name + ".png")); encoder.Save(file);
            }
            overlay.Listen(); overlay.SetLevel(0.05f);
            await Task.Delay(400);
            Check(overlay.IsVisible && OverlayWindowBehavior.HasRequiredStyles(overlay), "Overlay native styles missing.");
            Check(OverlayWindowBehavior.IsInsideTargetWorkArea(overlay, handle), "Overlay outside target monitor work area.");
            await Type('a'); Snapshot("listening");
            overlay.Transcribe(); await Type('b'); Snapshot("transcribing");
            overlay.Inserting(); await Type('c');
            overlay.Done(); overlay.EndActivity();
            Check(overlay.IsVisible, "Done feedback dismissed by Idle transition.");
            await Type('d'); Snapshot("done");
            overlay.Listen(); await Task.Delay(1050);
            Check(overlay.IsVisible, "Stale success timer hid new recording.");
            overlay.EndActivity(); Check(!overlay.IsVisible, "Empty/canceled recording did not hide overlay.");
            overlay.Error("Diagnostic error"); await Type('e'); Snapshot("error");
            overlay.Dismiss(); overlay.Listen(); await Type('f');
            overlay.Done(); await Task.Delay(1100);
            Check(!overlay.IsVisible, "Success feedback did not dismiss.");
            Check(editor.Text == "abcdef", "Diagnostic keyboard sequence changed.");
        }
        finally { overlay.Dismiss(); target.Close(); }
    }
}
