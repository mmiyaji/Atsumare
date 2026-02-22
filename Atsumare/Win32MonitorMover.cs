using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Atsumare;

internal static class Win32MonitorMover
{
    public static bool MoveWindowToNextMonitor(IntPtr hWnd)
    {
        var monitors = EnumerateMonitors();
        if (monitors.Count <= 1) return false;

        IntPtr curMon = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
        int curIdx = monitors.FindIndex(m => m.HMonitor == curMon);
        if (curIdx < 0) curIdx = 0;

        int nextIdx = (curIdx + 1) % monitors.Count;
        var target = monitors[nextIdx].Work; // 作業領域（タスクバー除外）

        // 位置だけ移動（サイズ変更なし）
        int x = target.Left + 40;
        int y = target.Top + 40;

        SetWindowPos(hWnd, IntPtr.Zero, x, y, 0, 0,
            SWP_NOZORDER | SWP_NOSIZE | SWP_NOACTIVATE);

        return true;
    }

    // 同一モニター内での切り分け用：少しだけ動かす
    public static void NudgeWindow(IntPtr hWnd, int dx, int dy)
    {
        if (!GetWindowRect(hWnd, out var r)) return;

        int x = r.Left + dx;
        int y = r.Top + dy;

        SetWindowPos(hWnd, IntPtr.Zero, x, y, 0, 0,
            SWP_NOZORDER | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private static List<Mon> EnumerateMonitors()
    {
        var list = new List<Mon>();
        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (IntPtr hMon, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                var mi = new MONITORINFO
                {
                    cbSize = (uint)Marshal.SizeOf<MONITORINFO>()
                };

                if (GetMonitorInfo(hMon, ref mi))
                {
                    list.Add(new Mon(hMon, mi.rcMonitor, mi.rcWork));
                }

                return true;
            },
            IntPtr.Zero);
        return list;
    }

    private readonly record struct Mon(IntPtr HMonitor, RECT Monitor, RECT Work);

    // --- Win32 ---
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
}
