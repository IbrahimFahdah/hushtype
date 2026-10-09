using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hushtype.Core;

namespace Hushtype.Platform;

public sealed class ClipboardTextInserter(Dispatcher dispatcher) : ITextInserter
{
    private const int ClipboardBusy = unchecked((int)0x800401D0); // CLIPBRD_E_CANT_OPEN
    private const string CopiedInstead = " The text was copied to the clipboard instead.";
    public event EventHandler<string>? Warning;
    // Typing leaves the clipboard untouched and helps apps that block or mishandle paste.
    public bool TypeShortText { get; set; }
    public async Task InsertAsync(string text, TargetApp target, CancellationToken ct)
    {
        // WPF dispatcher is STA and stays alive through retries and restoration.
        if (!dispatcher.CheckAccess())
        {
            await await dispatcher.InvokeAsync(() => InsertAsync(text, target, ct));
            return;
        }
        DataObject? saved = null;
        uint ownedSequence = 0;
        bool changed = false, canRestore = true, keepText = false;
        try
        {
            ForegroundAppProvider.EnsureCanInsert(target);
            for (var i = 0; ModifiersDown() && i < 25; i++) await Task.Delay(20, ct);
            if (ModifiersDown()) throw new PasteRefusedException("Release Ctrl, Alt, Shift and Windows keys before dictation is pasted.");
            ct.ThrowIfCancellationRequested();
            if (TypeShortText && TypingPlan.ShouldType(text)) { await TypeAsync(text, target, ct); return; }
            // Restoring the clipboard is best effort; never let it block the paste.
            try { await RetryAsync(() => saved = Snapshot(), ct); }
            catch (ExternalException)
            {
                canRestore = false;
                Warning?.Invoke(this, "The clipboard could not be saved, so the dictated text will remain on it.");
            }
            ForegroundAppProvider.EnsureCanInsert(target);
            await RetryAsync(() => Clipboard.SetText(text), ct);
            changed = true;
            ownedSequence = NativeMethods.GetClipboardSequenceNumber();
            ct.ThrowIfCancellationRequested();
            ForegroundAppProvider.EnsureCanInsert(target);
            var inputs = new[] { Key(0x11, false), Key(0x56, false), Key(0x56, true), Key(0x11, true) };
            if (NativeMethods.SendInput(4, inputs, Marshal.SizeOf<NativeMethods.Input>()) != 4)
            {
                NativeMethods.SendInput(2, [Key(0x56, true), Key(0x11, true)], Marshal.SizeOf<NativeMethods.Input>());
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not send the paste shortcut.");
            }
            // Restoration must run even when the app is quitting or cancellation arrives.
            await Task.Delay(150, CancellationToken.None);
        }
        catch (PasteRefusedException e)
        {
            // No history yet, so a refused paste must not lose the dictation.
            keepText = true;
            if (changed) throw new InvalidOperationException(e.Message + CopiedInstead);
            try { await RetryAsync(() => Clipboard.SetText(text), CancellationToken.None); }
            catch (ExternalException) { throw new InvalidOperationException(e.Message); }
            throw new InvalidOperationException(e.Message + CopiedInstead);
        }
        finally
        {
            if (changed && canRestore && !keepText)
            {
                try
                {
                    await RetryAsync(() =>
                    {
                        // Preserve any newer clipboard copy made during the paste delay.
                        if (NativeMethods.GetClipboardSequenceNumber() != ownedSequence) return;
                        if (saved is null) Clipboard.Clear();
                        else Clipboard.SetDataObject(saved, true);
                    }, CancellationToken.None);
                }
                catch (ExternalException) { Warning?.Invoke(this, "Text was pasted, but another app prevented clipboard restoration."); }
            }
        }
    }
    private DataObject? Snapshot()
    {
        var original = Clipboard.GetDataObject();
        if (original is null) return null;
        var copy = new DataObject();
        var skipped = new List<string>();
        foreach (var format in original.GetFormats(false))
        {
            try
            {
                var value = original.GetData(format, false);
                if (value is MemoryStream stream) value = new MemoryStream(stream.ToArray());
                else if (value is BitmapSource bitmap) { var cloned = bitmap.Clone(); cloned.Freeze(); value = cloned; }
                else if (value is string[] paths) value = paths.Clone();
                if (value is not null) copy.SetData(format, value, false);
            }
            // A busy clipboard is retried as a whole; any other per-format failure
            // (e.g. delayed rendering from an app that has since closed) skips that format.
            catch (ExternalException e) when (e.ErrorCode == ClipboardBusy) { throw; }
            catch (Exception e) when (e is ExternalException or NotSupportedException or ArgumentException) { skipped.Add(format); }
        }
        if (skipped.Count > 0) Warning?.Invoke(this, "Some clipboard formats could not be preserved: " + string.Join(", ", skipped));
        return copy;
    }
    private static async Task RetryAsync(Action action, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { action(); return; }
            catch (ExternalException e) when (e.ErrorCode == ClipboardBusy && attempt < 4)
            { await Task.Delay(20, ct); }
        }
    }
    private static async Task TypeAsync(string text, TargetApp target, CancellationToken ct)
    {
        foreach (var part in TypingPlan.Split(text))
        {
            ct.ThrowIfCancellationRequested();
            ForegroundAppProvider.EnsureCanInsert(target);
            var inputs = part == TypingPlan.Enter ? [Key(0x0D, false), Key(0x0D, true)] : part.SelectMany(c => new[] { Unicode(c, false), Unicode(c, true) }).ToArray();
            if (NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>()) != inputs.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not type the dictated text.");
            await Task.Delay(10, ct);
        }
    }
    private static bool ModifiersDown() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(key => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0);
    private static NativeMethods.Input Key(ushort key, bool up) => new() { Type = 1, Data = new() { Keyboard = new() { Key = key, Flags = up ? 2u : 0u, Extra = InputTags.Hushtype } } };
    // KEYEVENTF_UNICODE: the character travels in the scan code, independent of keyboard layout.
    private static NativeMethods.Input Unicode(char c, bool up) => new() { Type = 1, Data = new() { Keyboard = new() { Scan = c, Flags = 4u | (up ? 2u : 0u), Extra = InputTags.Hushtype } } };
}
