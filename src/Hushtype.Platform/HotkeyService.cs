using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Hushtype.Core;

namespace Hushtype.Platform;

public sealed class HotkeyService(Dispatcher dispatcher) : IHotkeyService
{
    // The hook runs on its own thread: if the UI thread stalls past LowLevelHooksTimeout,
    // Windows would otherwise silently remove the hook.
    private Thread? thread;
    private uint threadId;
    private volatile bool running, held;
    private volatile int key = AppSettings.DefaultHotkey, generation;
    private NativeMethods.HookProc? callback;
    private nint hook, watchedWindow;
    private bool watchedElevated;
    internal volatile bool AcceptDiagnosticInput, IgnoreForDiagnostics;
    public int Key => key;
    // Right Ctrl types nothing and opens no menu, so it is observed rather than swallowed;
    // that keeps the target app's modifier state truthful and lets the watchdog read it.
    public static bool Swallows(int key) => key != 0xA3;
    public void Configure(int key)
    {
        dispatcher.VerifyAccess();
        if (!AppSettings.Hotkeys.ContainsKey(key)) throw new ArgumentException("Unsupported hotkey.");
        if (held) throw new InvalidOperationException("Release the dictation key before changing it.");
        generation++; this.key = key;
    }
    public event EventHandler? PressStarted;
    public event EventHandler? PressEnded;
    public void Start()
    {
        dispatcher.VerifyAccess();
        if (running) return;
        Exception? failure = null;
        using var installed = new ManualResetEventSlim();
        running = true;
        thread = new Thread(() => Run(installed, ref failure)) { IsBackground = true, Name = "Hushtype keyboard hook", Priority = ThreadPriority.AboveNormal };
        thread.Start();
        installed.Wait();
        if (failure is not null) { running = false; thread.Join(); thread = null; throw failure; }
    }
    public void Stop()
    {
        dispatcher.VerifyAccess();
        generation++;
        if (thread is null) return;
        running = false;
        NativeMethods.PostThreadMessage(threadId, 0x12, 0, 0); // WM_QUIT
        thread.Join();
        thread = null;
        held = false;
    }
    private void Run(ManualResetEventSlim installed, ref Exception? failure)
    {
        threadId = NativeMethods.GetCurrentThreadId();
        callback = OnKey;
        hook = NativeMethods.SetWindowsHookEx(13, callback, NativeMethods.GetModuleHandle(null), 0);
        if (hook == 0) { failure = new Win32Exception(Marshal.GetLastWin32Error()); installed.Set(); return; }
        var timer = NativeMethods.SetTimer(0, 0, 250, 0);
        installed.Set();
        try
        {
            while (NativeMethods.GetMessage(out var message, 0, 0, 0) > 0)
                if (message.Message == 0x113) Watch(); // WM_TIMER
        }
        finally
        {
            NativeMethods.KillTimer(0, timer);
            NativeMethods.UnhookWindowsHookEx(hook);
            hook = 0;
            GC.KeepAlive(callback);
        }
    }
    private nint OnKey(int code, nint message, nint data)
    {
        if (code < 0) return NativeMethods.CallNextHookEx(hook, code, message, data);
        var input = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(data);
        // AltGr's companion left Ctrl is never treated as the dictation key.
        // Ignore synthetic input so our own Ctrl+V cannot trigger the pipeline.
        var synthetic = (input.Flags & 0x10) != 0 && !(AcceptDiagnosticInput && input.Extra == InputTags.Diagnostic);
        if (input.Key != key || synthetic || IgnoreForDiagnostics)
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
        return (down || up) && Swallows(key) ? 1 : NativeMethods.CallNextHookEx(hook, code, message, data);
    }
    // A missed key-up (Win+L, UAC, focus moving to an elevated window) would otherwise
    // keep recording until the five-minute cap.
    private void Watch()
    {
        if (!held) return;
        var reason = KeyReleaseMissed();
        if (reason is null) return;
        held = false;
        Queue(false);
        Released?.Invoke(this, reason);
    }
    private string? KeyReleaseMissed()
    {
        if (!Swallows(key) && (NativeMethods.GetAsyncKeyState(key) & 0x8000) == 0) return "key is up";
        var desktop = NativeMethods.OpenInputDesktop(0, false, 0x0001); // DESKTOP_READOBJECTS
        if (desktop == 0) return "input desktop changed";
        NativeMethods.CloseDesktop(desktop);
        var window = NativeMethods.GetForegroundWindow();
        if (window != watchedWindow)
        {
            watchedWindow = window;
            NativeMethods.GetWindowThreadProcessId(window, out var id);
            watchedElevated = window != 0 && ForegroundAppProvider.IsElevated(id) && !ForegroundAppProvider.IsElevated((uint)Environment.ProcessId);
        }
        return watchedElevated ? "administrator window focused" : null;
    }
    // Raised on the hook thread when the watchdog ends a press; the reason contains no user text.
    public event EventHandler<string>? Released;
    private void Queue(bool down)
    {
        var version = generation;
        dispatcher.BeginInvoke(() =>
        {
            if (version != generation || !running) return;
            if (down) PressStarted?.Invoke(this, EventArgs.Empty);
            else PressEnded?.Invoke(this, EventArgs.Empty);
        }, DispatcherPriority.Input);
    }
    public void Dispose() => Stop();
}
