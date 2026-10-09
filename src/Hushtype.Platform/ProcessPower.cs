using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Hushtype.Platform;

// Windows may run a background process's threads on efficiency cores (EcoQoS).
public static class ProcessPower
{
    public enum Throttling { SystemDefault, Off, On }
    public static void SetExecutionSpeedThrottling(Throttling mode)
    {
        // PROCESS_POWER_THROTTLING_EXECUTION_SPEED; ControlMask 0 returns the decision to Windows.
        var state = new NativeMethods.PowerThrottlingState { Version = 1, ControlMask = mode == Throttling.SystemDefault ? 0u : 1u, StateMask = mode == Throttling.On ? 1u : 0u };
        if (!NativeMethods.SetProcessInformation(NativeMethods.GetCurrentProcess(), 4, ref state, Marshal.SizeOf<NativeMethods.PowerThrottlingState>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
