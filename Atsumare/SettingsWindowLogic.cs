using System;
using System.Collections.Generic;
using System.Linq;

namespace Atsumare;

internal static class SettingsWindowLogic
{
    internal const int DefaultHotkeyModifiers = 0x0002 | 0x0001;

    internal static string BuildHotkeyPreview(int modifiers, string? keyLabel)
    {
        var parts = new List<string>();
        if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x0001) != 0) parts.Add("Alt");
        if ((modifiers & 0x0004) != 0) parts.Add("Shift");

        if (!string.IsNullOrWhiteSpace(keyLabel))
            parts.Add(keyLabel);

        return parts.Count > 0 ? string.Join(" + ", parts) : "";
    }

    internal static string NormalizeExcludeCsv(string? csv)
    {
        var parts = (csv ?? "")
            .Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return string.Join(", ", parts);
    }

    internal static string EnsureSelfInExcludeCsv(string? csv, string selfProcessName)
    {
        var normalized = NormalizeExcludeCsv(csv);
        return !string.IsNullOrWhiteSpace(normalized) ? normalized : selfProcessName;
    }

    internal static int NormalizeHotkeyModifiers(int modifiers) =>
        modifiers == 0 ? DefaultHotkeyModifiers : modifiers;

    internal static bool TryValidateHotkeySelection(int modifiers, int virtualKey, out string message)
    {
        if (virtualKey <= 0)
        {
            message = "SettingsWindowLogic.SelectKey";
            return false;
        }

        if (IsModifierKey(virtualKey))
        {
            message = "SettingsWindowLogic.ModifierOnly";
            return false;
        }

        if (modifiers == 0)
        {
            message = "SettingsWindowLogic.RequireModifier";
            return false;
        }

        if ((modifiers & 0x0008) != 0 && virtualKey == 0x20)
        {
            message = "SettingsWindowLogic.WinSpaceReserved";
            return false;
        }

        message = "";
        return true;
    }

    internal static bool IsModifierKey(int virtualKey) =>
        virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C;

    internal static string GetVirtualKeyLabel(int virtualKey)
    {
        if (virtualKey is >= 0x70 and <= 0x7B)
            return $"F{virtualKey - 0x6F}";

        if (virtualKey is >= 'A' and <= 'Z')
            return ((char)virtualKey).ToString();

        if (virtualKey is >= '0' and <= '9')
            return ((char)virtualKey).ToString();

        return virtualKey switch
        {
            0x20 => "Space",
            0x0D => "Enter",
            0x09 => "Tab",
            0x1B => "Esc",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            _ => $"VK_{virtualKey:X2}"
        };
    }
}
