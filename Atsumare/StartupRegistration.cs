using Microsoft.Win32;
using System;
using System.IO;

namespace Atsumare;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "Atsumare";

    internal static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(EntryName) as string;
            return string.Equals(value, BuildCommand(), StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "StartupRegistration.IsEnabled");
            return false;
        }
    }

    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Failed to open startup registration key.");

        if (enabled)
        {
            key.SetValue(EntryName, BuildCommand(), RegistryValueKind.String);
            return;
        }

        if (key.GetValue(EntryName) != null)
            key.DeleteValue(EntryName, throwOnMissingValue: false);
    }

    private static string BuildCommand()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
            exePath = Path.Combine(AppContext.BaseDirectory, "Atsumare.exe");

        return $"\"{exePath}\"";
    }
}
