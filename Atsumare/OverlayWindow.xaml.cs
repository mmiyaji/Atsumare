using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace Atsumare;

public sealed partial class OverlayWindow : Window
{
    private readonly MonitorEnumerator.MonitorRect _monitor;
    private List<WindowCatalog.AppGroup> _groups = new();
    public event Action? RequestCloseAll;

    public OverlayWindow(MonitorEnumerator.MonitorRect monitor)
    {
        _monitor = monitor;

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        ConfigureAsOverlay();
        MoveToMonitorAndFullscreen(_monitor);

        Refresh();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void CloseAll_Click(object sender, RoutedEventArgs e) => RequestCloseAll?.Invoke();
    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        _groups = WindowCatalog.GetAppGroups();

        TitleText.Text = $"Atsumare ({_monitor.Left},{_monitor.Top})";

        var items = _groups.Select(g => new AppItemVm
        {
            Key = g.Key,
            Name = g.DisplayName,
            Count = g.Windows.Count,
            Icon = IconUtil.TryGetIcon(g.ExePath),
        }).ToList();

        AppsGrid.ItemsSource = items;
    }

    private void AppsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AppItemVm vm) return;

        var group = _groups.FirstOrDefault(x => x.Key == vm.Key);
        if (group == null) return;

        var hwnds = group.Windows.Select(w => w.Hwnd).ToList();
        WindowMover.MoveWindowsToMonitor(hwnds, _monitor);
        WindowMover.BringAllToFront(hwnds);

        if (hwnds.Count > 0)
            WindowMover.ForceForeground(hwnds[0]); // 代表をどれにするかは好みで

        RequestCloseAll?.Invoke();
    }

    private void ConfigureAsOverlay()
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

        // --- DWM (Win11向け). 失敗しても無視して続行 ---
        TryDisableDwmBorder(hwnd);

        // --- Win32: フレームを確実に剥がす（これが本命） ---
        StripWindowFrameStyles(hwnd);

        // 反映
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    private static void TryDisableDwmBorder(IntPtr hwnd)
    {
        // 枠線色を「なし」に
        const int DWMWA_BORDER_COLOR = 34;
        uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

        int hr1 = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref DWMWA_COLOR_NONE, sizeof(uint));
        System.Diagnostics.Debug.WriteLine($"DWM BORDER_COLOR hr=0x{hr1:X8}");

        // 可視フレーム境界線の太さを 0 に
        const int DWMWA_VISIBLE_FRAME_BORDER_THICKNESS = 37;
        uint thickness = 0;

        int hr2 = DwmSetWindowAttribute(hwnd, DWMWA_VISIBLE_FRAME_BORDER_THICKNESS, ref thickness, sizeof(uint));
        System.Diagnostics.Debug.WriteLine($"DWM VISIBLE_FRAME_BORDER_THICKNESS hr=0x{hr2:X8}");
    }

    private static void StripWindowFrameStyles(IntPtr hwnd)
    {
        const int GWL_STYLE = -16;

        const long WS_CAPTION = 0x00C00000L;
        const long WS_THICKFRAME = 0x00040000L;
        const long WS_SYSMENU = 0x00080000L;
        const long WS_MAXIMIZEBOX = 0x00010000L;
        const long WS_MINIMIZEBOX = 0x00020000L;

        long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();

        style &= ~WS_CAPTION;
        style &= ~WS_THICKFRAME;
        style &= ~WS_SYSMENU;
        style &= ~WS_MAXIMIZEBOX;
        style &= ~WS_MINIMIZEBOX;

        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref uint pvAttribute, int cbAttribute);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;


    private void MoveToMonitorAndFullscreen(MonitorEnumerator.MonitorRect m)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var winId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(winId);

        appWindow.MoveAndResize(new RectInt32(m.Left, m.Top, m.Width, m.Height));
    }
}
