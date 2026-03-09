using System;
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
    internal const string TaskId = "AtsumareStartup";

    internal static async Task<StartupRegistrationStatus> GetStatusAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            return Map(task.State);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "StartupRegistration.GetStatusAsync");
            return StartupRegistrationStatus.Unsupported;
        }
    }

    internal static async Task<StartupRegistrationStatus> SetEnabledAsync(bool enabled)
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
            CrashLog.Write(ex, "StartupRegistration.SetEnabledAsync");
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
}
