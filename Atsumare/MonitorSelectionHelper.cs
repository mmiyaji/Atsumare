using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Atsumare;

internal sealed record MonitorSelectionOption(string Key, string Label, IntPtr Handle, bool IsPrimary);

internal static class MonitorSelectionHelper
{
    private const uint MONITORINFOF_PRIMARY = 0x00000001;

    internal static IReadOnlyList<MonitorSelectionOption> GetPhysicalMonitorOptions()
    {
        return MonitorUtil.GetAllMonitors()
            .Select((handle, index) => CreateOption(handle, index))
            .Where(x => x != null)
            .Cast<MonitorSelectionOption>()
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => GetMonitorRect(x.Handle).Left)
            .ThenBy(x => GetMonitorRect(x.Handle).Top)
            .Select((x, index) => x with
            {
                Key = $"display:{index + 1}",
                Label = x.IsPrimary ? $"Monitor {index + 1} (Primary)" : $"Monitor {index + 1}"
            })
            .ToArray();
    }

    internal static IntPtr ResolveMonitorHandle(string? key, IntPtr currentMonitor)
    {
        var normalized = SettingsWindowLogic.NormalizeMonitorSelection(key);
        if (normalized == "current")
            return currentMonitor;

        var options = GetPhysicalMonitorOptions();
        if (normalized == "primary")
            return options.FirstOrDefault(x => x.IsPrimary)?.Handle ?? currentMonitor;

        var selected = options.FirstOrDefault(x => string.Equals(x.Key, normalized, StringComparison.OrdinalIgnoreCase));
        return selected?.Handle ?? currentMonitor;
    }

    private static MonitorSelectionOption? CreateOption(IntPtr handle, int index)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(handle, ref info))
            return null;

        return new MonitorSelectionOption(
            $"display:{index + 1}",
            ((info.dwFlags & MONITORINFOF_PRIMARY) != 0) ? $"Monitor {index + 1} (Primary)" : $"Monitor {index + 1}",
            handle,
            (info.dwFlags & MONITORINFOF_PRIMARY) != 0);
    }

    private static RECT GetMonitorRect(IntPtr handle)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(handle, ref info))
            return default;

        return info.rcWork;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
}
