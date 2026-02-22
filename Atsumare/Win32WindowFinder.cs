using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Atsumare;

internal static class Win32WindowFinder
{
    public static IntPtr FindFirstTopLevelWindowByProcessName(IReadOnlyList<string> processNamesLower)
    {
        IntPtr found = IntPtr.Zero;

        EnumWindows((hWnd, lParam) =>
        {
            if (found != IntPtr.Zero) return false;

            if (!IsWindowVisible(hWnd)) return true;
            if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true;

            // タイトル無しは除外（ツール/裏窓が混ざるのを減らす）
            if (GetWindowTextLength(hWnd) <= 0) return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return true;

            try
            {
                using var p = Process.GetProcessById((int)pid);
                var name = (p.ProcessName ?? "").ToLowerInvariant();
                foreach (var n in processNamesLower)
                {
                    if (name == n)
                    {
                        found = hWnd;
                        return false;
                    }
                }
            }
            catch
            {
                // 権限等で取れないプロセスは無視
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    // --- Win32 ---
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const uint GW_OWNER = 4;

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
