using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Atsumare;

public static class WindowCatalog
{
    public sealed record WindowInfo(IntPtr Hwnd, int Pid, string Title);
    public sealed record AppGroup(string Key, string DisplayName, string? ExePath, List<WindowInfo> Windows);

    public static List<AppGroup> GetAppGroups()
    {
        var byKey = new Dictionary<string, AppGroup>(StringComparer.OrdinalIgnoreCase);

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true; // owned window除外
            if (IsToolWindow(hWnd)) return true;

            int len = GetWindowTextLength(hWnd);
            if (len <= 0) return true;

            var sb = new StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;

            GetWindowThreadProcessId(hWnd, out uint pidU);
            int pid = unchecked((int)pidU);

            string key;
            string displayName;
            string? exePath = null;

            try
            {
                var p = Process.GetProcessById(pid);
                key = p.ProcessName;

                // 表示名（FileDescriptionが取れれば優先）
                try
                {
                    exePath = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        var fvi = FileVersionInfo.GetVersionInfo(exePath);
                        displayName = string.IsNullOrWhiteSpace(fvi.FileDescription) ? key : fvi.FileDescription!;
                    }
                    else
                    {
                        displayName = key;
                    }
                }
                catch
                {
                    displayName = key;
                }
            }
            catch
            {
                return true;
            }

            if (!byKey.TryGetValue(key, out var g))
            {
                g = new AppGroup(key, displayName, exePath, new List<WindowInfo>());
                byKey[key] = g;
            }

            g.Windows.Add(new WindowInfo(hWnd, pid, title));
            return true;

        }, IntPtr.Zero);

        var list = new List<AppGroup>(byKey.Values);
        list.Sort((a, b) => b.Windows.Count.CompareTo(a.Windows.Count)); // 件数多い順
        return list;
    }

    private static bool IsToolWindow(IntPtr hWnd)
    {
        var ex = GetWindowLongPtr(hWnd, GWL_EXSTYLE);
        long v = ex.ToInt64();
        return (v & WS_EX_TOOLWINDOW) != 0;
    }

    // --- Win32 ---
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    private const uint GW_OWNER = 4;

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
}
