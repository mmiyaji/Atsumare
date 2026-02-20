using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT;
using WinRT.Interop;
using Microsoft.UI.Dispatching;

namespace Atsumare;

public sealed partial class OverlayWindow : Window
{
    private readonly MonitorEnumerator.MonitorRect _monitor;
    private List<WindowCatalog.AppGroup> _groups = new();
    public event Action? RequestCloseAll;
    private DesktopAcrylicController? _acrylic;
    private SystemBackdropConfiguration? _backdropConfig;
    private DispatcherQueue _dq => DispatcherQueue.GetForCurrentThread();

    public OverlayWindow(MonitorEnumerator.MonitorRect monitor)
    {
        _monitor = monitor;

        InitializeComponent();
        TryEnableBackdrop();

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
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
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

        var margins = new MARGINS
        {
            cxLeftWidth = -1
        };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }
    private void TryEnableBackdrop()
    {
        if (!DesktopAcrylicController.IsSupported())
            return;

        _backdropConfig = new SystemBackdropConfiguration
        {
            IsInputActive = true
        };

        _acrylic = new DesktopAcrylicController();
        _acrylic.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
        _acrylic.SetSystemBackdropConfiguration(_backdropConfig);

        this.Activated += (_, e) =>
        {
            if (_backdropConfig != null)
                _backdropConfig.IsInputActive = e.WindowActivationState != WindowActivationState.Deactivated;
        };

        this.Closed += (_, __) =>
        {
            _acrylic?.Dispose();
            _acrylic = null;
        };
    }

    private void MoveToMonitorAndFullscreen(MonitorEnumerator.MonitorRect m)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var winId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(winId);

        appWindow.MoveAndResize(new RectInt32(m.Left, m.Top, m.Width, m.Height));
    }
    private void Root_Loaded(object sender, RoutedEventArgs e)
    {
        // 初回描画・レイアウト後に当て直す（1回では足りないことがあるので複数回）
        //ReapplyLayeredLater(0);
        //ReapplyLayeredLater(16);
        //ReapplyLayeredLater(100);
    }

    private void ReapplyLayeredLater(int delayMs)
    {
        _dq.TryEnqueue(async () =>
        {
            if (delayMs > 0)
                await System.Threading.Tasks.Task.Delay(delayMs);

            ApplyLayeredAlpha(160); // ←暗さ（0透明〜255不透明）。好みで調整
        });
    }

    private void ApplyLayeredAlpha(byte alpha)
    {
        var hwnd = WindowNative.GetWindowHandle(this);

        // EXSTYLE に WS_EX_LAYERED を必ず付け直す
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_LAYERED;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));

        // 不透明度を再設定
        SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);

        // フレーム変更を通知（効きが良くなることがある）
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_LAYERED = 0x00080000L;
    private const uint LWA_ALPHA = 0x00000002;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
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


    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(
        IntPtr hWnd,
        ref MARGINS pMarInset);

}
