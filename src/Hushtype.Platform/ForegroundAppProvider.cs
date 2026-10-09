using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Hushtype.Core;

namespace Hushtype.Platform;

// The target cannot receive a paste right now; the caller keeps the text on the clipboard.
internal sealed class PasteRefusedException(string message) : InvalidOperationException(message);

public sealed class ForegroundAppProvider : IForegroundAppProvider
{
    public TargetApp GetCurrent()
    {
        var window = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(window, out var id);
        var title = new StringBuilder(1024);
        NativeMethods.GetWindowText(window, title, title.Capacity);
        using var process = Process.GetProcessById((int)id);
        return new(process.ProcessName + ".exe", title.ToString(), window);
    }
    internal static void EnsureCanInsert(TargetApp target)
    {
        if (target.WindowHandle == 0 || NativeMethods.GetForegroundWindow() != target.WindowHandle)
            throw new PasteRefusedException("The active window changed. Dictation was not pasted.");
        NativeMethods.GetWindowThreadProcessId(target.WindowHandle, out var id);
        if (IsElevated(id) && !IsElevated((uint)Environment.ProcessId))
            throw new PasteRefusedException("Windows blocks pasting into administrator windows. Use a non-administrator window.");
    }
    private static bool IsElevated(uint id)
    {
        var process = NativeMethods.OpenProcess(0x1000, false, id);
        if (process == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot inspect the target window's permissions.");
        try
        {
            if (!NativeMethods.OpenProcessToken(process, 8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!NativeMethods.GetTokenInformation(token, 20, out var elevated, sizeof(int), out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return elevated != 0;
            }
            finally { NativeMethods.CloseHandle(token); }
        }
        finally { NativeMethods.CloseHandle(process); }
    }
}
