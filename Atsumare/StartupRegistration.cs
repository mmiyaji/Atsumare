using Microsoft.Win32;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace Atsumare;

internal enum StartupRegistrationStatus
{
    Unsupported,
    Disabled,
    DisabledByUser,
    DisabledByPolicy,
    Enabled,
}

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "Atsumare";

    internal const string TaskId = "AtsumareStartup";

    internal static async Task<StartupRegistrationStatus> GetStatusAsync()
    {
        if (IsPackaged())
            return await GetPackagedStatusAsync();

        return GetUnpackagedStatus();
    }

    internal static async Task<StartupRegistrationStatus> SetEnabledAsync(bool enabled)
    {
        if (IsPackaged())
            return await SetPackagedEnabledAsync(enabled);

        return SetUnpackagedEnabled(enabled);
    }

    private static async Task<StartupRegistrationStatus> GetPackagedStatusAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            return Map(task.State);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "StartupRegistration.GetPackagedStatusAsync");
            return StartupRegistrationStatus.Unsupported;
        }
    }

    private static async Task<StartupRegistrationStatus> SetPackagedEnabledAsync(bool enabled)
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);

            if (!enabled)
            {
                task.Disable();
                return Map(task.State);
            }

            var state = task.State;
            if (state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy)
                return Map(state);

            state = await task.RequestEnableAsync();
            return Map(state);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "StartupRegistration.SetPackagedEnabledAsync");
            return StartupRegistrationStatus.Unsupported;
        }
    }

    private static StartupRegistrationStatus GetUnpackagedStatus()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(EntryName) as string;
            return string.Equals(value, BuildCommand(), StringComparison.Ordinal)
                ? StartupRegistrationStatus.Enabled
                : StartupRegistrationStatus.Disabled;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "StartupRegistration.GetUnpackagedStatus");
            return StartupRegistrationStatus.Unsupported;
        }
    }

    private static StartupRegistrationStatus SetUnpackagedEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("Failed to open startup registration key.");

            if (!enabled)
            {
                if (key.GetValue(EntryName) != null)
                    key.DeleteValue(EntryName, throwOnMissingValue: false);

                return StartupRegistrationStatus.Disabled;
            }

            key.SetValue(EntryName, BuildCommand(), RegistryValueKind.String);
            return StartupRegistrationStatus.Enabled;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "StartupRegistration.SetUnpackagedEnabled");
            return StartupRegistrationStatus.Unsupported;
        }
    }

    private static StartupRegistrationStatus Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Disabled => StartupRegistrationStatus.Disabled,
        StartupTaskState.DisabledByUser => StartupRegistrationStatus.DisabledByUser,
        StartupTaskState.DisabledByPolicy => StartupRegistrationStatus.DisabledByPolicy,
        StartupTaskState.Enabled => StartupRegistrationStatus.Enabled,
        StartupTaskState.EnabledByPolicy => StartupRegistrationStatus.Enabled,
        _ => StartupRegistrationStatus.Unsupported,
    };

    private static bool IsPackaged()
    {
        try
        {
            _ = Package.Current;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildCommand()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
            exePath = Path.Combine(AppContext.BaseDirectory, "Atsumare.exe");

        return $"\"{exePath}\"";
    }
}
