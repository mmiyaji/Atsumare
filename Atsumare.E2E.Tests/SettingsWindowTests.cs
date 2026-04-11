using System.Text.Json;
using Xunit;

namespace Atsumare.E2E.Tests;

public sealed class SettingsWindowTests
{
    [SkippableFact]
    public void MainWindow_CanOpenSettingsWindow()
    {
        Skip.If(Environment.GetEnvironmentVariable("ATSUMARE_RUN_E2E") != "1",
            "Set ATSUMARE_RUN_E2E=1 in an interactive Windows session to run E2E UI tests.");

        using var session = new E2ETestSession();
        session.SeedSettings(new
        {
            StartMinimizedToTray = false,
            LaunchAtStartup = false,
            CloseButtonMinimizesToTray = false,
            HotkeyModifiers = 3,
            HotkeyVirtualKey = 32,
            ShowMoveOverlay = false,
            ExcludeProcessNamesCsv = "Atsumare",
            EnableVerboseLog = false,
        });

        session.Launch();
        var settingsButton = session.FindByIdAnywhere("SettingsButton");
        settingsButton.Patterns.Invoke.Pattern.Invoke();

        Assert.NotNull(session.FindByIdAnywhere("SwStartMinToTray"));
    }

    [SkippableFact]
    public void SettingsWindow_CanPersistExcludeProcessNames()
    {
        Skip.If(Environment.GetEnvironmentVariable("ATSUMARE_RUN_E2E") != "1",
            "Set ATSUMARE_RUN_E2E=1 in an interactive Windows session to run E2E UI tests.");

        using var session = new E2ETestSession();
        session.SeedSettings(new
        {
            StartMinimizedToTray = false,
            LaunchAtStartup = false,
            CloseButtonMinimizesToTray = false,
            HotkeyModifiers = 3,
            HotkeyVirtualKey = 32,
            ShowMoveOverlay = false,
            ExcludeProcessNamesCsv = "Atsumare",
            EnableVerboseLog = false,
        });

        session.Launch("--settings");
        session.FindByIdAnywhere("NavAppList").Patterns.Invoke.Pattern.Invoke();

        var excludeTextBox = session.FindByIdAnywhere("TbExcludeCsv");
        excludeTextBox.Patterns.Value.Pattern.SetValue("explorer, obs64");

        session.WaitForSettingsValue(json =>
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("ExcludeProcessNamesCsv", out var value)
                && value.GetString() == "explorer, obs64";
        });
    }
}
