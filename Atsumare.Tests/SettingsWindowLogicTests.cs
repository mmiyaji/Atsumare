using Xunit;

namespace Atsumare.Tests;

public sealed class SettingsWindowLogicTests
{
    [Fact]
    public void NormalizeExcludeCsv_TrimsAndDeduplicates()
    {
        var actual = SettingsWindowLogic.NormalizeExcludeCsv(" explorer, obs64 ; Explorer \n slack ");
        Assert.Equal("explorer, obs64, slack", actual);
    }

    [Fact]
    public void EnsureSelfInExcludeCsv_FallsBackToSelfProcess()
    {
        var actual = SettingsWindowLogic.EnsureSelfInExcludeCsv("  ", "Atsumare");
        Assert.Equal("Atsumare", actual);
    }

    [Fact]
    public void NormalizeHotkeyModifiers_UsesDefaultWhenEmpty()
    {
        var actual = SettingsWindowLogic.NormalizeHotkeyModifiers(0);
        Assert.Equal(SettingsWindowLogic.DefaultHotkeyModifiers, actual);
    }

    [Fact]
    public void BuildHotkeyPreview_FormatsModifierSequence()
    {
        var actual = SettingsWindowLogic.BuildHotkeyPreview(0x0002 | 0x0001 | 0x0004, "F2");
        Assert.Equal("Ctrl + Alt + Shift + F2", actual);
    }

    [Fact]
    public void TryValidateHotkeySelection_RejectsModifierOnlyKey()
    {
        var ok = SettingsWindowLogic.TryValidateHotkeySelection(0x0002, 0x11, out var message);
        Assert.False(ok);
        Assert.Equal("SettingsWindowLogic.ModifierOnly", message);
    }

    [Fact]
    public void TryValidateHotkeySelection_RejectsNoModifier()
    {
        var ok = SettingsWindowLogic.TryValidateHotkeySelection(0, 0x71, out var message);
        Assert.False(ok);
        Assert.Equal("SettingsWindowLogic.RequireModifier", message);
    }
}
