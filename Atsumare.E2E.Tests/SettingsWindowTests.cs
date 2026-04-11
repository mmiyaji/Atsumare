using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
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
            ExcludeProcessNamesCsv = "Atsumare",
            EnableVerboseLog = false,
        });

        session.Launch();
        var mainWindow = session.WaitForWindow();
        var settingsButton = session.FindById(mainWindow, "SettingsButton");
        settingsButton.Patterns.Invoke.Pattern.Invoke();

        var settingsWindow = session.WaitForWindowContaining("SwStartMinToTray");
        Assert.NotNull(session.FindById(settingsWindow, "SwStartMinToTray"));
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
            ExcludeProcessNamesCsv = "Atsumare",
            EnableVerboseLog = false,
        });

        session.Launch("--settings");
        var settingsWindow = session.WaitForWindowContaining("NavAppList");
        ActivateNavItem(session.FindById(settingsWindow, "NavAppList"));

        var excludeTextBox = session.FindById(settingsWindow, "TbExcludeCsv");
        excludeTextBox.Patterns.Value.Pattern.SetValue("explorer, obs64");

        session.WaitForSettingsValue(json =>
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("ExcludeProcessNamesCsv", out var value)
                && value.GetString() == "explorer, obs64";
        });
    }

    [SkippableFact]
    public void SettingsWindow_CanPersistFunctionalSettings()
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
            ExcludeProcessNamesCsv = "Atsumare",
            EnableVerboseLog = false,
        });

        session.Launch("--settings");
        var settingsWindow = session.WaitForWindowContaining("SwStartMinToTray");

        SetToggle(session.FindById(settingsWindow, "SwStartMinToTray"), true);
        SetToggle(session.FindById(settingsWindow, "SwCloseMinToTray"), true);

        ActivateNavItem(session.FindById(settingsWindow, "NavLog"));
        SetToggle(session.FindById(settingsWindow, "SwVerboseLog"), true);

        ActivateNavItem(session.FindById(settingsWindow, "NavGeneral"));
        SetCheckBox(session.FindById(settingsWindow, "CbModAlt"), false);
        SetCheckBox(session.FindById(settingsWindow, "CbModCtrl"), true);
        SetCheckBox(session.FindById(settingsWindow, "CbModShift"), true);
        session.FindById(settingsWindow, "CbHotkeyKey").AsComboBox().Select("F2");

        session.WaitForSettingsValue(json =>
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return root.GetProperty("StartMinimizedToTray").GetBoolean()
                && root.GetProperty("CloseButtonMinimizesToTray").GetBoolean()
                && root.GetProperty("EnableVerboseLog").GetBoolean()
                && root.GetProperty("HotkeyModifiers").GetInt32() == (0x0002 | 0x0004)
                && root.GetProperty("HotkeyVirtualKey").GetInt32() == 0x71;
        });
    }

    [SkippableFact]
    public void MainWindow_ShowsStartupSplashOnInitialLaunch()
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
            ExcludeProcessNamesCsv = "Atsumare",
            EnableVerboseLog = false,
        });

        session.Launch();
        session.WaitForLogEntry(log => log.Contains("[Splash] shown", StringComparison.Ordinal));
        session.WaitForLogEntry(log => log.Contains("[Splash] hidden", StringComparison.Ordinal));
    }

    private static void SetToggle(AutomationElement element, bool isOn)
    {
        var toggle = element.Patterns.Toggle.Pattern;
        var expectedState = isOn ? ToggleState.On : ToggleState.Off;
        while (toggle.ToggleState.Value != expectedState)
            toggle.Toggle();
    }

    private static void SetCheckBox(AutomationElement element, bool isChecked)
    {
        var toggle = element.Patterns.Toggle.Pattern;
        var expectedState = isChecked ? ToggleState.On : ToggleState.Off;
        while (toggle.ToggleState.Value != expectedState)
            toggle.Toggle();
    }

    private static void ActivateNavItem(AutomationElement element)
    {
        if (element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
            return;
        }

        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
            return;
        }

        throw new InvalidOperationException($"Navigation item '{element.AutomationId}' does not support Select or Invoke.");
    }
}
