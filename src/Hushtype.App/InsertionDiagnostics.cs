using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Hushtype.Core;
using Hushtype.Platform;

namespace Hushtype.App;

// Explicit desktop test: types and pastes only into its own disposable editor.
internal static class InsertionDiagnostics
{
    public static async Task<string> RunAsync(ClipboardTextInserter inserter)
    {
        var editor = new TextBox { AcceptsReturn = true, FontSize = 20, Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap };
        var window = new Window { Title = "Hushtype insertion diagnostic", Width = 640, Height = 300, Content = editor };
        var previousMode = inserter.TypeShortText;
        var userText = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        const string sentinel = "hushtype-clipboard-sentinel";
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        try
        {
            window.Show(); window.Activate(); editor.Focus();
            await Task.Delay(250);
            var target = new TargetApp("diagnostic", window.Title, new WindowInteropHelper(window).Handle);
            Clipboard.SetText(sentinel);
            inserter.TypeShortText = true;
            const string typed = "Héllo wörld 😀, typed without the clipboard: 50% done & ok.";
            await inserter.InsertAsync(typed, target, default);
            await Task.Delay(200);
            Check(editor.Text == typed, $"Typed text mismatch: '{editor.Text}'.");
            Check(Clipboard.GetText() == sentinel, "Typing changed the clipboard.");
            editor.Clear();
            var longText = string.Join(" ", Enumerable.Repeat("Long dictation falls back to paste.", 8));
            Check(!TypingPlan.ShouldType(longText), "Long diagnostic text should paste.");
            await inserter.InsertAsync(longText, target, default);
            await Task.Delay(200);
            Check(editor.Text == longText, "Long text was not pasted.");
            Check(Clipboard.GetText() == sentinel, "Clipboard was not restored after paste.");
            return $"typed {typed.Length} UTF-16 units including a surrogate pair with the clipboard untouched; {longText.Length}-character text pasted and clipboard restored";
        }
        finally
        {
            inserter.TypeShortText = previousMode;
            window.Close();
            try { if (userText is not null) Clipboard.SetText(userText); else Clipboard.Clear(); } catch (System.Runtime.InteropServices.ExternalException) { }
        }
    }
}
