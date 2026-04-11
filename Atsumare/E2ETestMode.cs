using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Atsumare;

internal static class E2ETestMode
{
    private const string EnabledEnvVar = "ATSUMARE_E2E";
    private const string SettingsPathEnvVar = "ATSUMARE_SETTINGS_PATH";
    private const string InstanceIdEnvVar = "ATSUMARE_E2E_INSTANCE_ID";
    private const string LogDirEnvVar = "ATSUMARE_LOG_DIR";
    private const string EnabledArg = "--e2e";
    private const string SettingsPathArg = "--e2e-settings-path";
    private const string InstanceIdArg = "--e2e-instance-id";
    private const string LogDirArg = "--e2e-log-dir";

    internal static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnabledEnvVar), "1", StringComparison.Ordinal)
        || HasCommandLineArg(EnabledArg);

    internal static string? GetSettingsPathOverride()
    {
        var path = Environment.GetEnvironmentVariable(SettingsPathEnvVar);
        if (string.IsNullOrWhiteSpace(path))
            path = GetCommandLineArgValue(SettingsPathArg);

        if (string.IsNullOrWhiteSpace(path))
            return null;

        return Path.GetFullPath(path);
    }

    internal static string? GetLogDirOverride()
    {
        var path = Environment.GetEnvironmentVariable(LogDirEnvVar);
        if (string.IsNullOrWhiteSpace(path))
            path = GetCommandLineArgValue(LogDirArg);

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
        return settings;
    }

    internal static string GetScopedKernelObjectName(string baseName)
    {
        if (!IsEnabled)
            return baseName;

        var instanceId = Environment.GetEnvironmentVariable(InstanceIdEnvVar);
        if (string.IsNullOrWhiteSpace(instanceId))
            instanceId = GetCommandLineArgValue(InstanceIdArg);

        if (string.IsNullOrWhiteSpace(instanceId))
            instanceId = Process.GetCurrentProcess().Id.ToString();

        return $"{baseName}_{instanceId}";
    }

    private static bool HasCommandLineArg(string argName) =>
        Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, argName, StringComparison.OrdinalIgnoreCase));

    private static string? GetCommandLineArgValue(string argName)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], argName, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
