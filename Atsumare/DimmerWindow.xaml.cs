using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace Atsumare;

public sealed partial class DimmerWindow : Window
{
    private readonly MonitorEnumerator.MonitorRect _monitor;

    public DimmerWindow(MonitorEnumerator.MonitorRect monitor, byte alpha /* 0-255 */)
    {
        _monitor = monitor;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        ConfigureAsDimmer(alpha);
        MoveToMonitorAndFullscreen(_monitor);
    }

    private void ConfigureAsDimmer(byte alpha)
    {
        var hwnd = WindowNative.GetWindowHandle(this);

        var winId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(winId);

        if (appWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false);
            p.IsAlwaysOnTop = true;
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
        }

        // ツールウィンドウ + Layered + クリック透過
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));

        // 暗幕の濃さ（0=透明, 255=真っ黒）
        SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);

        // 反映を安定させる
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_TRANSPARENT = 0x00000020L;

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);


    private void MoveToMonitorAndFullscreen(MonitorEnumerator.MonitorRect m)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var winId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(winId);

        appWindow.MoveAndResize(new RectInt32(m.Left, m.Top, m.Width, m.Height));
    }

    // --- Win32 ---

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_LAYERED = 0x00080000L;
    private const uint LWA_ALPHA = 0x00000002;

}
