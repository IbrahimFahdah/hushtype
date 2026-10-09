using Microsoft.Win32;
using System.IO;

namespace Hushtype.Platform;

public static class StartupRegistration
{
    public static void RefreshIfEnabled(string executable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key?.GetValue("Hushtype") is not string command || !command.StartsWith('"')) return;
        var end = command.IndexOf('"', 1);
        if (end > 1) key.SetValue("Hushtype", "\"" + executable + "\"" + command[(end + 1)..]);
    }
    public static void Set(bool enabled, string executable, string? dataDirectory)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (!enabled) { key.DeleteValue("Hushtype", throwOnMissingValue: false); return; }
        if (!File.Exists(executable) || executable.Contains('"') || dataDirectory?.Contains('"') == true) throw new ArgumentException("Invalid startup path.");
        var command = "\"" + executable + "\" --background";
        if (dataDirectory is not null) command += " --data-dir \"" + dataDirectory.TrimEnd('\\') + "\"";
        key.SetValue("Hushtype", command, RegistryValueKind.String);
    }
}
