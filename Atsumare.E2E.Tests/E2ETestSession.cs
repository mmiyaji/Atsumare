using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace Atsumare.E2E.Tests;

internal sealed class E2ETestSession : IDisposable
{
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly string _settingsPath;
    private readonly string _logDirectory;
    private readonly HashSet<int> _trackedProcessIds = new();

    private Process? _process;

    public E2ETestSession()
    {
        TestRootDirectory = Path.Combine(Path.GetTempPath(), "Atsumare.E2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TestRootDirectory);
        _settingsPath = Path.Combine(TestRootDirectory, "settings.json");
        _logDirectory = Path.Combine(TestRootDirectory, "logs");
        Directory.CreateDirectory(_logDirectory);
        Automation = new UIA3Automation();
    }

    public UIA3Automation Automation { get; }

    public string TestRootDirectory { get; }

    public string SettingsPath => _settingsPath;

    public string LogDirectory => _logDirectory;

    public string LogPath => Path.Combine(_logDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

    public string AppExePath => ResolveAppExePath();

    public void SeedSettings(object settings)
    {
        var json = JsonSerializer.Serialize(settings);
        File.WriteAllText(_settingsPath, json);
    }

    public void Launch(params string[] args)
    {
        DisposeProcess();
        var existingProcessIds = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppExePath))
            .Select(x => x.Id)
            .ToHashSet();

        var psi = new ProcessStartInfo
        {
            FileName = AppExePath,
            WorkingDirectory = Path.GetDirectoryName(AppExePath)!,
            UseShellExecute = false,
        };
        psi.Environment["ATSUMARE_E2E"] = "1";
        psi.Environment["ATSUMARE_E2E_INSTANCE_ID"] = _instanceId;
        psi.Environment["ATSUMARE_SETTINGS_PATH"] = _settingsPath;
        psi.Environment["ATSUMARE_LOG_DIR"] = _logDirectory;
        psi.ArgumentList.Add("--e2e");
        psi.ArgumentList.Add("--e2e-instance-id");
        psi.ArgumentList.Add(_instanceId);
        psi.ArgumentList.Add("--e2e-settings-path");
        psi.ArgumentList.Add(_settingsPath);
        psi.ArgumentList.Add("--e2e-log-dir");
        psi.ArgumentList.Add(_logDirectory);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var bootstrapProcess = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start Atsumare.");
        _process = ResolveLaunchedProcess(bootstrapProcess, existingProcessIds);
    }

    public Window WaitForWindow(string? title = null)
    {
        var result = Retry.WhileNull(
            () => FindVisibleWindow(title),
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(200));

        return result.Result?.AsWindow()
            ?? throw new InvalidOperationException($"Visible window '{title ?? "<any>"}' did not appear.");
    }

    public Window WaitForWindowContaining(string automationId)
    {
        var result = Retry.WhileNull(
            () =>
            {
                foreach (var element in Automation.GetDesktop().FindAllChildren())
                {
                    try
                    {
                        if (element.ControlType != ControlType.Window)
                            continue;

                        var bounds = element.BoundingRectangle;
                        if (bounds.Width <= 0 || bounds.Height <= 0)
                            continue;

                        if (element.FindFirstDescendant(Automation.ConditionFactory.ByAutomationId(automationId)) != null)
                            return element.AsWindow();
                    }
                    catch
                    {
                    }
                }

                return null;
            },
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(100));

        return result.Result
            ?? throw new InvalidOperationException($"Visible window containing AutomationId '{automationId}' did not appear.");
    }

    public AutomationElement FindById(AutomationElement root, string automationId)
    {
        var cf = Automation.ConditionFactory;
        var result = Retry.WhileNull(
            () => root.FindFirstDescendant(cf.ByAutomationId(automationId)),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(100));

        return result.Result
            ?? throw new InvalidOperationException($"AutomationId '{automationId}' was not found.");
    }

    public AutomationElement FindByIdInProcess(string automationId)
    {
        var result = Retry.WhileNull(
            () =>
            {
                var window = FindVisibleWindow(null);
                if (window == null)
                    return null;

                try
                {
                    return window.FindFirstDescendant(Automation.ConditionFactory.ByAutomationId(automationId));
                }
                catch
                {
                    return null;
                }
            },
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(100));

        return result.Result
            ?? throw new InvalidOperationException($"AutomationId '{automationId}' was not found in the Atsumare process.");
    }

    public AutomationElement FindByIdAnywhere(string automationId)
    {
        var result = Retry.WhileNull(
            () =>
            {
                try
                {
                    return Automation.GetDesktop()
                        .FindAllDescendants()
                        .FirstOrDefault(x => string.Equals(x.AutomationId, automationId, StringComparison.Ordinal));
                }
                catch
                {
                    return null;
                }
            },
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(100));

        return result.Result
            ?? throw new InvalidOperationException($"AutomationId '{automationId}' was not found on the desktop.");
    }

    public void WaitForSettingsValue(Func<string, bool> predicate)
    {
        var result = Retry.WhileFalse(
            () => File.Exists(_settingsPath) && predicate(File.ReadAllText(_settingsPath)),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(100));

        if (!result.Success)
            throw new InvalidOperationException("Timed out waiting for settings.json to update.");
    }

    public void WaitForLogEntry(Func<string, bool> predicate)
    {
        var result = Retry.WhileFalse(
            () => File.Exists(LogPath) && predicate(File.ReadAllText(LogPath)),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(100));

        if (!result.Success)
            throw new InvalidOperationException("Timed out waiting for application log output.");
    }

    public void Dispose()
    {
        DisposeProcess();
        Automation.Dispose();

        try
        {
            if (Directory.Exists(TestRootDirectory))
                Directory.Delete(TestRootDirectory, recursive: true);
        }
        catch
        {
        }
    }

    private void DisposeProcess()
    {
        foreach (var processId in _trackedProcessIds.ToArray())
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }

        _process = null;
        _trackedProcessIds.Clear();
    }

    private AutomationElement? FindVisibleWindow(string? title)
    {
        if (_process == null)
            return null;

        foreach (var element in Automation.GetDesktop().FindAllChildren())
        {
            try
            {
                if (element.ControlType != ControlType.Window)
                    continue;

                if (!element.Properties.ProcessId.TryGetValue(out var pid) || pid != _process.Id)
                    continue;

                if (!string.IsNullOrWhiteSpace(title) && !string.Equals(element.Name, title, StringComparison.Ordinal))
                    continue;

                var bounds = element.BoundingRectangle;
                if (bounds.Width <= 0 || bounds.Height <= 0)
                    continue;

                return element;
            }
            catch
            {
                // Window tree changes frequently while the app is starting up.
            }
        }

        return null;
    }

    private Process ResolveLaunchedProcess(Process bootstrapProcess, HashSet<int> existingProcessIds)
    {
        var processName = Path.GetFileNameWithoutExtension(AppExePath);
        var result = Retry.WhileNull(
            () =>
            {
                bootstrapProcess.Refresh();
                if (!bootstrapProcess.HasExited)
                {
                    _trackedProcessIds.Add(bootstrapProcess.Id);
                    return bootstrapProcess;
                }

                var candidates = Process.GetProcessesByName(processName)
                    .Where(x => !existingProcessIds.Contains(x.Id))
                    .OrderByDescending(GetSafeStartTimeUtc)
                    .ToArray();

                if (candidates.Length > 0)
                {
                    foreach (var candidate in candidates)
                        _trackedProcessIds.Add(candidate.Id);

                    return candidates[0];
                }

                return null;
            },
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(100));

        return result.Result ?? throw new InvalidOperationException("Failed to locate the launched Atsumare process.");
    }

    private static DateTime GetSafeStartTimeUtc(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static string ResolveAppExePath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("ATSUMARE_APP_EXE");
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return explicitPath;

        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "Atsumare.sln")))
            {
                return Path.Combine(
                    dir,
                    "Atsumare",
                    "bin",
                    "x64",
                    "Debug",
                    "net8.0-windows10.0.19041.0",
                    "win-x64",
                    "Atsumare.exe");
            }

            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Could not locate Atsumare.exe. Set ATSUMARE_APP_EXE if needed.");
    }
}
