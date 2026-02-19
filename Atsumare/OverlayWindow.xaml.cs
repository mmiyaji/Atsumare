using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
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

        // 先頭の1枚を前面へ（なければ何もしない）
        if (hwnds.Count > 0)
        {
            WindowMover.ActivateWindow(hwnds[0]);
        }

        // 全オーバーレイを閉じる
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
    }

    private void MoveToMonitorAndFullscreen(MonitorEnumerator.MonitorRect m)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var winId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(winId);

        appWindow.MoveAndResize(new RectInt32(m.Left, m.Top, m.Width, m.Height));
    }
}
