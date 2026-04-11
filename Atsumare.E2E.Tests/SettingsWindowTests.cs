using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
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
            ShowMoveOverlay = false,
            ExcludeProcessNamesCsv = "Atsumare",
            EnableVerboseLog = false,
        });

        session.Launch("--settings");

        SetToggle(session.FindByIdAnywhere("SwStartMinToTray"), true);
        SetToggle(session.FindByIdAnywhere("SwCloseMinToTray"), true);

        session.FindByIdAnywhere("NavMove").Patterns.Invoke.Pattern.Invoke();
        SetToggle(session.FindByIdAnywhere("SwShowOverlay"), true);

        session.FindByIdAnywhere("NavLog").Patterns.Invoke.Pattern.Invoke();
        SetToggle(session.FindByIdAnywhere("SwVerboseLog"), true);

        session.FindByIdAnywhere("NavGeneral").Patterns.Invoke.Pattern.Invoke();
        SetCheckBox(session.FindByIdAnywhere("CbModAlt"), false);
        SetCheckBox(session.FindByIdAnywhere("CbModCtrl"), true);
        SetCheckBox(session.FindByIdAnywhere("CbModShift"), true);
        session.FindByIdAnywhere("CbHotkeyKey").AsComboBox().Select("F2");

        session.WaitForSettingsValue(json =>
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return root.GetProperty("StartMinimizedToTray").GetBoolean()
                && root.GetProperty("CloseButtonMinimizesToTray").GetBoolean()
                && root.GetProperty("ShowMoveOverlay").GetBoolean()
                && root.GetProperty("EnableVerboseLog").GetBoolean()
                && root.GetProperty("HotkeyModifiers").GetInt32() == (0x0002 | 0x0004)
                && root.GetProperty("HotkeyVirtualKey").GetInt32() == 0x71;
        });
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
}
