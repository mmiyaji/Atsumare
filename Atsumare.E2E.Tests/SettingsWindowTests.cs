using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
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
        ReplaceText(excludeTextBox, "explorer, obs64");

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

        session.WaitForSettingsValue(json =>
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return root.GetProperty("StartMinimizedToTray").GetBoolean()
                && root.GetProperty("CloseButtonMinimizesToTray").GetBoolean()
                && root.GetProperty("EnableVerboseLog").GetBoolean();
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

    private static void ReplaceText(AutomationElement element, string value)
    {
        element.Focus();
        using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
            Keyboard.Type(VirtualKeyShort.KEY_A);
        Keyboard.Type(VirtualKeyShort.BACK);
        Keyboard.Type(value);
    }
}
