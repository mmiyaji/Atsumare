using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Atsumare;

public static class WindowMover
{
    public static void MoveWindowsToMonitor(IReadOnlyList<IntPtr> hwnds, MonitorEnumerator.MonitorRect target)
    {
        int margin = 24;
        int step = 28; // 少しずらして重なりが分かるように（不要なら 0 でもOK）

        for (int i = 0; i < hwnds.Count; i++)
        {
            var h = hwnds[i];
            if (h == IntPtr.Zero) continue;

            // 最小化は戻す
            ShowWindow(h, SW_RESTORE);

            if (!GetWindowRect(h, out var r)) continue;

            int w = r.Right - r.Left;
            int hgt = r.Bottom - r.Top;

            // 位置だけ移動（サイズは維持）
            int x = target.Left + margin + (i * step);
            int y = target.Top + margin + (i * step);

            SetWindowPos(h, IntPtr.Zero, x, y, w, hgt, SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }
    public static void ActivateWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        // 最小化なら戻す
        ShowWindow(hwnd, SW_RESTORE);

        // 前面化（制限があるので2段構え）
        SetForegroundWindow(hwnd);
    }


    private const int SW_RESTORE = 9;

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
