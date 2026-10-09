using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Hushtype.Platform;

// Drives the real hook with tagged injected input. Only F9 (swallowed, so the foreground
// app never sees it) and Right Ctrl (harmless on its own) are pressed.
public static class HotkeyDiagnostics
{
    public static async Task<string> RunAsync(HotkeyService service, Dispatcher dispatcher)
    {
        int started = 0, ended = 0;
        void OnStart(object? s, EventArgs e) => started++;
        void OnEnd(object? s, EventArgs e) => ended++;
        service.PressStarted += OnStart; service.PressEnded += OnEnd;
        var previous = service.Key;
        service.AcceptDiagnosticInput = true;
        try
        {
            service.Configure(0x78);
            service.Start();
            // 1. UI thread blocked for 2 s while the key is pressed and released.
            var typing = Task.Run(async () => { await Task.Delay(200); Send(0x78, false); await Task.Delay(300); Send(0x78, true); });
            Thread.Sleep(2000);
            await typing;
            await Settle(dispatcher);
            if (started != 1 || ended != 1) throw new InvalidOperationException($"Blocked UI thread: expected one press and release, got {started}/{ended}.");
            Send(0x78, false); Send(0x78, true); await Settle(dispatcher);
            if (started != 2 || ended != 2) throw new InvalidOperationException("Hook stopped delivering after the UI thread was blocked.");
            // 2. A swallowed key never reaches GetAsyncKeyState, so the watchdog must not end it early.
            Send(0x78, false); await Task.Delay(1000);
            var swallowedState = (NativeMethods.GetAsyncKeyState(0x78) & 0x8000) != 0;
            if (started != 3 || ended != 2) throw new InvalidOperationException("Watchdog ended a swallowed key that was still held.");
            Send(0x78, true); await Settle(dispatcher);
            if (ended != 3) throw new InvalidOperationException("Swallowed key release was not delivered.");
            service.Stop();
            // 3. Observed Right Ctrl whose key-up the hook misses: the watchdog ends the press.
            service.Configure(0xA3);
            service.Start();
            Send(0xA3, false); await Settle(dispatcher);
            if (started != 4) throw new InvalidOperationException("Right Ctrl press was not delivered.");
            service.IgnoreForDiagnostics = true;
            Send(0xA3, true);
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (ended != 4 && DateTime.UtcNow < deadline) await Task.Delay(50);
            service.IgnoreForDiagnostics = false;
            if (ended != 4) throw new InvalidOperationException("Watchdog did not end a press whose key-up was missed.");
            return $"hook thread survived a 2 s UI block; swallowed F9 held 1 s (async state down={swallowedState}); missed Right Ctrl release ended by watchdog";
        }
        finally
        {
            service.IgnoreForDiagnostics = false;
            service.Stop();
            service.AcceptDiagnosticInput = false;
            service.PressStarted -= OnStart; service.PressEnded -= OnEnd;
            service.Configure(previous);
        }
    }
    private static async Task Settle(Dispatcher dispatcher)
    {
        await Task.Delay(150);
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
    private static void Send(ushort key, bool up)
    {
        // Right Ctrl is an extended key; without the flag Windows reports it as left Ctrl.
        var flags = (up ? 2u : 0u) | (key == 0xA3 ? 1u : 0u);
        var input = new NativeMethods.Input { Type = 1, Data = new() { Keyboard = new() { Key = key, Flags = flags, Extra = InputTags.Diagnostic } } };
        if (NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.Input>()) != 1)
            throw new InvalidOperationException("SendInput failed for the hotkey diagnostic.");
    }
}
