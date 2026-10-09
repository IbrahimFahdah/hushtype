using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Hushtype.Core;

namespace Hushtype.Platform;

public sealed class HotkeyService(Dispatcher dispatcher) : IHotkeyService
{
    private nint hook;
    private NativeMethods.HookProc? callback;
    private bool held;
    private int generation;
    public int Key { get; private set; } = 0xA5;
    public void Configure(int key)
    {
        dispatcher.VerifyAccess();
        if (!AppSettings.Hotkeys.ContainsKey(key)) throw new ArgumentException("Unsupported hotkey.");
        if (held) throw new InvalidOperationException("Release the dictation key before changing it.");
        generation++; Key = key;
    }
    public event EventHandler? PressStarted;
    public event EventHandler? PressEnded;
    public void Start()
    {
        dispatcher.VerifyAccess();
        if (hook != 0) return;
        callback = OnKey;
        hook = NativeMethods.SetWindowsHookEx(13, callback, NativeMethods.GetModuleHandle(null), 0);
        if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Stop()
    {
        dispatcher.VerifyAccess();
        generation++;
        if (hook != 0) NativeMethods.UnhookWindowsHookEx(hook);
        hook = 0;
        held = false;
    }
    private nint OnKey(int code, nint message, nint data)
    {
        if (code < 0) return NativeMethods.CallNextHookEx(hook, code, message, data);
        var key = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(data);
        // AltGr's companion left Ctrl is never treated as the dictation key.
        // Ignore synthetic input so our own Ctrl+V cannot trigger the pipeline.
        if (key.Key != Key || (key.Flags & 0x10) != 0)
            return NativeMethods.CallNextHookEx(hook, code, message, data);
        var down = message == 0x100 || message == 0x104;
        var up = message == 0x101 || message == 0x105;
        if (down && !held)
        {
            held = true;
            Queue(true);
        }
        else if (up && held)
        {
            held = false;
            Queue(false);
        }
        return down || up ? 1 : NativeMethods.CallNextHookEx(hook, code, message, data);
    }
    private void Queue(bool down)
    {
        var version = generation;
        dispatcher.BeginInvoke(() =>
        {
            if (version != generation || hook == 0) return;
            if (down) PressStarted?.Invoke(this, EventArgs.Empty);
            else PressEnded?.Invoke(this, EventArgs.Empty);
        }, DispatcherPriority.Input);
    }
    public void Dispose() => Stop();
}
