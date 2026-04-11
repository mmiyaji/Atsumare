using System;
using System.Diagnostics;
using System.IO;

namespace Atsumare;

internal static class E2ETestMode
{
    private const string EnabledEnvVar = "ATSUMARE_E2E";
    private const string SettingsPathEnvVar = "ATSUMARE_SETTINGS_PATH";
    private const string InstanceIdEnvVar = "ATSUMARE_E2E_INSTANCE_ID";

    internal static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnabledEnvVar), "1", StringComparison.Ordinal);

    internal static string? GetSettingsPathOverride()
    {
        var path = Environment.GetEnvironmentVariable(SettingsPathEnvVar);
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return Path.GetFullPath(path);
    }

    internal static AtsumareSettings CreateDefaultSettings()
    {
        var settings = new AtsumareSettings();
        if (!IsEnabled)
            return settings;

        settings.StartMinimizedToTray = false;
        settings.CloseButtonMinimizesToTray = false;
        settings.ShowMoveOverlay = false;
        return settings;
    }

    internal static string GetScopedKernelObjectName(string baseName)
    {
        if (!IsEnabled)
            return baseName;

        var instanceId = Environment.GetEnvironmentVariable(InstanceIdEnvVar);
        if (string.IsNullOrWhiteSpace(instanceId))
            instanceId = Process.GetCurrentProcess().Id.ToString();

        return $"{baseName}_{instanceId}";
    }
}
