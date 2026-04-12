using System;
using System.Collections.Generic;
using System.Linq;

namespace Atsumare;

internal static class SettingsWindowLogic
{
    internal const int DefaultHotkeyModifiers = 0x0002 | 0x0004; // Ctrl + Shift
    internal const int DefaultHotkeyVirtualKey = 0x71; // F2

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
        return NormalizeCsv(csv);
    }

    internal static string EnsureSelfInExcludeCsv(string? csv, string selfProcessName)
    {
        var parts = ParseCsv(csv);

        if (!parts.Contains(selfProcessName, StringComparer.OrdinalIgnoreCase))
            parts.Add(selfProcessName);

        return string.Join(", ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    internal static List<string> ParseCsv(string? csv) =>
        (csv ?? "")
            .Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();

    internal static string NormalizeCsv(string? csv) =>
        string.Join(", ", ParseCsv(csv).Distinct(StringComparer.OrdinalIgnoreCase));

    internal static string AddCsvValue(string? csv, string value)
    {
        var parts = ParseCsv(csv);
        if (!parts.Contains(value, StringComparer.OrdinalIgnoreCase))
            parts.Add(value);

        return string.Join(", ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    internal static string RemoveCsvValue(string? csv, string value)
    {
        var parts = ParseCsv(csv)
            .Where(x => !string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
        return string.Join(", ", parts);
    }

    internal static string TouchRecentKeyCsv(string? csv, string value, int maxItems = 12)
    {
        var parts = ParseCsv(csv)
            .Where(x => !string.Equals(x, value, StringComparison.OrdinalIgnoreCase))
            .ToList();
        parts.Insert(0, value);
        return string.Join(", ", parts.Take(maxItems));
    }

    internal static string NormalizeMonitorSelection(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "current";

        var normalized = key.Trim().ToLowerInvariant();
        if (normalized == "current" || normalized == "primary")
            return normalized;

        if (normalized.StartsWith("display:", StringComparison.Ordinal))
            return normalized;

        return "current";
    }

    internal static AtsumareRulesFile CreateRulesFile(AtsumareSettings settings) => new()
    {
        ExportedAtUtc = DateTime.UtcNow.ToString("O"),
        DefaultTargetMonitorKey = NormalizeMonitorSelection(settings.DefaultTargetMonitorKey),
        PreserveMaximizedOnMove = settings.PreserveMaximizedOnMove,
        FocusMovedAppAfterMove = settings.FocusMovedAppAfterMove,
        AutoPinMovedApps = settings.AutoPinMovedApps,
        DisableRecentSorting = settings.DisableRecentSorting,
        ExcludeProcessNamesCsv = NormalizeExcludeCsv(settings.ExcludeProcessNamesCsv),
        PinnedAppKeysCsv = NormalizeCsv(settings.PinnedAppKeysCsv),
        RecentAppKeysCsv = NormalizeCsv(settings.RecentAppKeysCsv)
    };

    internal static void ApplyRulesFile(AtsumareSettings settings, AtsumareRulesFile rulesFile, string selfProcessName)
    {
        settings.DefaultTargetMonitorKey = NormalizeMonitorSelection(rulesFile.DefaultTargetMonitorKey);
        settings.PreserveMaximizedOnMove = rulesFile.PreserveMaximizedOnMove;
        settings.FocusMovedAppAfterMove = rulesFile.FocusMovedAppAfterMove;
        settings.AutoPinMovedApps = rulesFile.AutoPinMovedApps;
        settings.DisableRecentSorting = rulesFile.DisableRecentSorting;
        settings.ExcludeProcessNamesCsv = EnsureSelfInExcludeCsv(rulesFile.ExcludeProcessNamesCsv, selfProcessName);
        settings.PinnedAppKeysCsv = NormalizeCsv(rulesFile.PinnedAppKeysCsv);
        settings.RecentAppKeysCsv = NormalizeCsv(rulesFile.RecentAppKeysCsv);
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
