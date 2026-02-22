using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Atsumare;

public static class WindowMover
{
    public static void MoveWindowsToMonitor(IReadOnlyList<IntPtr> hwnds, MonitorEnumerator.MonitorRect target)
    {
        int margin = 24;

        foreach (var h in hwnds)
        {
            if (h == IntPtr.Zero) continue;

            if (!GetWindowRect(h, out var r))
                continue;

            int x = target.Left + margin;
            int y = target.Top + margin;

            //// 何も余計なことをしない
            //SetWindowPos(h, IntPtr.Zero, x, y, 0, 0,
            //    SWP_NOZORDER | SWP_NOSIZE);
            //// 移動後
            //SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, SWP_NOZORDER | SWP_NOSIZE);

            // “揺らし”で合成を促す
            SetWindowPos(h, IntPtr.Zero, x + 100, y, 0, 0, SWP_NOZORDER | SWP_NOSIZE);
            //SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, SWP_NOZORDER | SWP_NOSIZE);

        }
    }

    private static void MoveMaximizedByWindowPlacement(IntPtr h, int x, int y, RECT work)
    {
        var wp = new WINDOWPLACEMENT();
        wp.length = Marshal.SizeOf<WINDOWPLACEMENT>();

        if (!GetWindowPlacement(h, ref wp))
            return;

        // rcNormalPosition は “最大化解除したときの通常位置” を表す
        // ここをターゲット側に寄せておくと、最大化状態の再配置にも反映されやすい
        int w = Math.Min(1200, Math.Max(300, work.Right - work.Left - 80));
        int hgt = Math.Min(800, Math.Max(200, work.Bottom - work.Top - 80));

        wp.rcNormalPosition.Left = x;
        wp.rcNormalPosition.Top = y;
        wp.rcNormalPosition.Right = x + w;
        wp.rcNormalPosition.Bottom = y + hgt;

        // 最大化維持
        wp.showCmd = SW_SHOWMAXIMIZED;

        SetWindowPlacement(h, ref wp);

        // 念のため合成反映（軽い）
        DwmFlush();
    }
    private static RECT GetMonitorWorkAreaFromTargetRect(MonitorEnumerator.MonitorRect target)
    {
        // target 内の点から HMONITOR を引く
        var pt = new POINT { X = target.Left + 1, Y = target.Top + 1 };
        var hMon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);

        var mi = new MONITORINFO();
        mi.cbSize = (uint)Marshal.SizeOf<MONITORINFO>();
        if (hMon != IntPtr.Zero && GetMonitorInfo(hMon, ref mi))
            return mi.rcWork;

        // 取れなければ target を work とみなす
        return new RECT { Left = target.Left, Top = target.Top, Right = target.Right, Bottom = target.Bottom };
    }

    public static void ForceForeground(IntPtr h)
    {
        if (h == IntPtr.Zero) return;
        SetForegroundWindow(h);
        DwmFlush();
    }


    private static bool IsAlreadyOnTarget(RECT w, MonitorEnumerator.MonitorRect target)
    {
        // 中心点がターゲット内なら「そのモニター上」とみなす
        int cx = (w.Left + w.Right) / 2;
        int cy = (w.Top + w.Bottom) / 2;

        return cx >= target.Left && cx < target.Right
            && cy >= target.Top && cy < target.Bottom;
    }


    public static void BringAllToFront(IReadOnlyList<IntPtr> hwnds)
    {
        foreach (var h in hwnds)
        {
            if (h == IntPtr.Zero) continue;

            // 最小化だけ復元（最大化は維持）
            if (IsIconic(h))
                ShowWindow(h, SW_RESTORE);

            // 位置・サイズは変えずに前面へ（Z順のみ）
            SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        //// 最後の1つだけフォーカス（Windows仕様）
        //if (hwnds.Count > 0)
        //    SetForegroundWindow(hwnds[hwnds.Count - 1]);
    }

    [DllImport("user32.dll")]
    static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_UPDATENOW = 0x0100;
    private const uint RDW_ALLCHILDREN = 0x0080;

    private static bool IsOnTargetMonitor(IntPtr hwnd, MonitorEnumerator.MonitorRect target)
    {
        if (!GetWindowRect(hwnd, out var r)) return false;

        int cx = (r.Left + r.Right) / 2;
        int cy = (r.Top + r.Bottom) / 2;

        return cx >= target.Left && cx < target.Right
            && cy >= target.Top && cy < target.Bottom;
    }

    // ---- Win32 ----
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;

    private const int SW_RESTORE = 9;

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private const int SW_MAXIMIZE = 3;
    private const int SW_SHOWMAXIMIZED = 3;

    private const uint SWP_ASYNCWINDOWPOS = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();



    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);


    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MAXIMIZE = 0xF030;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);


    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

}
