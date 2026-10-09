using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Hushtype.Platform;

// All coordinates passed to Win32 are physical pixels. WPF owns the DIP layout.
public static class OverlayWindowBehavior
{
    private const long RequiredStyles = 0x08000000 | 0x00000080 | 0x00000020; // NOACTIVATE, TOOLWINDOW, TRANSPARENT
    public static nint ForegroundWindow => NativeMethods.GetForegroundWindow();

    public static void Attach(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var styles = NativeMethods.GetWindowLongPtr(handle, -20).ToInt64();
        Marshal.SetLastPInvokeError(0);
        var previous = NativeMethods.SetWindowLongPtr(handle, -20, (nint)(styles | RequiredStyles));
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception();
        HwndSource.FromHwnd(handle).AddHook(FilterMessages);
    }

    private static nint FilterMessages(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return 3; } // WM_MOUSEACTIVATE: MA_NOACTIVATE
        if (message == 0x0084) { handled = true; return -1; } // WM_NCHITTEST: HTTRANSPARENT
        return 0;
    }

    public static void Position(Window window, nint target)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var monitor = NativeMethods.MonitorFromWindow(target, 2);
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) throw new Win32Exception();
        // First move selects the monitor; the second pass uses the overlay's actual DPI.
        var dpi = NativeMethods.GetDpiForWindow(target);
        for (var pass = 0; pass < 2; pass++)
        {
            var scale = (dpi == 0 ? 96 : dpi) / 96.0;
            var width = Math.Min(info.Work.Right - info.Work.Left, (int)Math.Round(window.Width * scale));
            var height = Math.Min(info.Work.Bottom - info.Work.Top, (int)Math.Round(window.Height * scale));
            var x = info.Work.Left + (info.Work.Right - info.Work.Left - width) / 2;
            var y = Math.Max(info.Work.Top, info.Work.Bottom - height - (int)Math.Round(24 * scale));
            if (!NativeMethods.SetWindowPos(handle, -1, x, y, width, height, 0x0010 | 0x0200)) throw new Win32Exception();
            dpi = NativeMethods.GetDpiForWindow(handle);
        }
    }

    public static bool HasRequiredStyles(Window window) =>
        (NativeMethods.GetWindowLongPtr(new WindowInteropHelper(window).Handle, -20).ToInt64() & RequiredStyles) == RequiredStyles;

    public static bool IsInsideTargetWorkArea(Window window, nint target)
    {
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(target, 2), ref info) ||
            !NativeMethods.GetWindowRect(new WindowInteropHelper(window).Handle, out var rect)) return false;
        return rect.Left >= info.Work.Left && rect.Right <= info.Work.Right && rect.Top >= info.Work.Top && rect.Bottom <= info.Work.Bottom;
    }

    // Used only by the explicit desktop diagnostic, and only while its own editor is foreground.
    public static void TypeDiagnosticCharacter(nint expectedWindow, char character)
    {
        if (ForegroundWindow != expectedWindow) throw new InvalidOperationException("Diagnostic editor lost focus; no input sent.");
        NativeMethods.Input[] inputs = [
            new() { Type = 1, Data = new() { Keyboard = new() { Scan = character, Flags = 0x0004 } } },
            new() { Type = 1, Data = new() { Keyboard = new() { Scan = character, Flags = 0x0004 | 0x0002 } } }
        ];
        if (NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.Input>()) != 2) throw new Win32Exception();
    }
}
