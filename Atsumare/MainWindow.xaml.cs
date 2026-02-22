using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using WinRT.Interop;
using static Atsumare.App;

namespace Atsumare;

public sealed partial class MainWindow : Window
{
    public ObservableCollection<AppGroupItem> AllItems { get; } = new();
    public ObservableCollection<AppGroupItem> FilteredItems { get; } = new();

    private bool _focusedOnce;

    // ★このウィンドウが担当するターゲットモニター（クリック時にここへ寄せる）
    private IntPtr _targetMonitorForThisWindow = IntPtr.Zero;

    private double _tileWidth = 180;
    public double TileWidth
    {
        get => _tileWidth;
        set
        {
            if (Math.Abs(_tileWidth - value) < 0.5) return;
            _tileWidth = value;
            Bindings.Update();
        }
    }

    #region Win32

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder exeName, ref int size);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    private const uint GA_ROOTOWNER = 3;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private const int WM_GETICON = 0x007F;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const int ICON_SMALL2 = 2;

    private const int GCLP_HICON = -14;
    private const int GCLP_HICONSM = -34;

    #endregion

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSENDCHANGING = 0x0400;

    private const int SW_RESTORE = 9;
    private const int SW_SHOWNORMAL = 1;
    private const int SW_SHOWMAXIMIZED = 3;
    private const int SW_MAXIMIZE = 3;
    private bool _topMostOnce;
    private AppWindow? _cachedAppWindow;

    public MainWindow()
    {
        InitializeComponent();

        ConfigureWindow();
        ConfigureTitleBarColors();
        SetWindowSize(820, 540);

        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
            SystemBackdrop = null;
        }

        // Escで閉じる
        this.Content.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                CloseAllAtsumareWindows();
            }
        };

        // 起動時はフィルターにフォーカス
        this.Activated += (_, _) => FilterBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);

        ApplyFilter("");
        this.Activated += MainWindow_Activated;
        _ = DispatcherQueue.TryEnqueue(async () => await ReloadRunningWindowsAsync());

    }
    internal void InitializeForMonitor(IntPtr targetMonitor)
    {
        _targetMonitorForThisWindow = targetMonitor;
    }
    private AppWindow GetAppWindow()
    {
        if (_cachedAppWindow != null) return _cachedAppWindow;

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _cachedAppWindow = AppWindow.GetFromWindowId(windowId);
        return _cachedAppWindow;
    }

    private void ConfigureWindow()
    {
        var appWindow = GetAppWindow();
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
        }
    }
    private void ConfigureTitleBarColors()
    {
        var appWindow = GetAppWindow();
        var titleBar = appWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;

        var root = this.Content as FrameworkElement;
        var isDark = root?.ActualTheme == ElementTheme.Dark;

        var bg = isDark
            ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
            : Windows.UI.Color.FromArgb(255, 245, 245, 245);

        var fg = isDark
            ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
            : Windows.UI.Color.FromArgb(255, 0, 0, 0);

        titleBar.BackgroundColor = bg;
        titleBar.ForegroundColor = fg;
        titleBar.ButtonBackgroundColor = bg;
        titleBar.ButtonForegroundColor = fg;

        titleBar.ButtonHoverBackgroundColor =
            isDark
                ? Windows.UI.Color.FromArgb(255, 45, 45, 45)
                : Windows.UI.Color.FromArgb(255, 230, 230, 230);

        titleBar.ButtonPressedBackgroundColor =
            isDark
                ? Windows.UI.Color.FromArgb(255, 60, 60, 60)
                : Windows.UI.Color.FromArgb(255, 210, 210, 210);

        titleBar.InactiveBackgroundColor = bg;
        titleBar.ButtonInactiveBackgroundColor = bg;
    }
    public void PrePositionToMonitorCenter(IntPtr hMon, int width, int height)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero) return;

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMon, ref mi)) return;

        var work = mi.rcWork;
        int x = work.Left + (work.Right - work.Left - width) / 2;
        int y = work.Top + (work.Bottom - work.Top - height) / 2;

        // ここで初期位置を確定（表示前に効く）
        SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSENDCHANGING);
    }
    private void SetWindowSize(int width, int height)
        => GetAppWindow().Resize(new SizeInt32(width, height));

    public void MoveToMonitorCenter(IntPtr hMon, int width, int height, int retry = 10)
    {
        if (_closing) return;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_closing) return;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(hMon, ref mi)) return;

            var work = mi.rcWork;
            int x = work.Left + (work.Right - work.Left - width) / 2;
            int y = work.Top + (work.Bottom - work.Top - height) / 2;

            try
            {
                var aw = GetAppWindow();
                if (aw?.Presenter is not OverlappedPresenter)
                {
                    if (retry > 0 && !_closing)
                    {
                        Debug.WriteLine("Presenter not ready. Retry Move/Resize...");
                        // 少し後に再試行
                        _ = DispatcherQueue.TryEnqueue(() => MoveToMonitorCenter(hMon, width, height, retry - 1));
                    }
                    return;
                }

                aw.Resize(new SizeInt32(width, height));
                aw.Move(new PointInt32(x, y));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("MoveToMonitorCenter failed: " + ex);
            }
        });
    }
    private void MakeTopMost()
    {
        var appWindow = GetAppWindow();
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
        }
    }
    private static bool IsForegroundOurProcess()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;

        GetWindowThreadProcessId(fg, out uint fgPid);
        return fgPid == GetCurrentProcessId();
    }
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _deactivateCloseTimer;
    private bool _closing;
    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyFilter(FilterBox.Text);

    private void ApplyFilter(string text)
    {
        var q = (text ?? "").Trim().ToLowerInvariant();

        var items = string.IsNullOrEmpty(q)
            ? AllItems
            : new ObservableCollection<AppGroupItem>(
                AllItems.Where(x =>
                    (x.AppName ?? "").ToLowerInvariant().Contains(q) ||
                    (x.Description ?? "").ToLowerInvariant().Contains(q)));

        FilteredItems.Clear();
        foreach (var it in items) FilteredItems.Add(it);
    }

    // ★クリック：このウィンドウがある（=担当する）モニターに寄せる → 全Atsumareを閉じる
    private void GridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AppGroupItem item) return;

        // ターゲット：このウィンドウに割り当てられたモニター（なければ自分のモニター）
        var myHwnd = WindowNative.GetWindowHandle(this);
        var targetMon = _targetMonitorForThisWindow != IntPtr.Zero
            ? _targetMonitorForThisWindow
            : MonitorFromWindow(myHwnd, MONITOR_DEFAULTTONEAREST);

        // 寄せ
        MoveAllWindowsOfProcessToMonitor(item.Pid, targetMon);

        // 閉じる（操作後に即消える）
        DispatcherQueue.TryEnqueue(CloseAllAtsumareWindows);
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            if (_closing) return;

            // ★起動中は自動クローズしない
            if (AppState.Bootstrapping)
                return;

            // ★即閉じせず、少し待ってから「本当に他アプリか」判定
            _deactivateCloseTimer ??= DispatcherQueue.CreateTimer();
            _deactivateCloseTimer.Stop();
            _deactivateCloseTimer.Interval = TimeSpan.FromMilliseconds(150);
            _deactivateCloseTimer.IsRepeating = false;
            _deactivateCloseTimer.Tick += (_, __) =>
            {
                _deactivateCloseTimer?.Stop();

                if (_closing) return;

                // 前面が自プロセスなら（Atsumare同士の切替等）閉じない
                if (IsForegroundOurProcess())
                    return;

                CloseAllAtsumareWindows();
            };
            _deactivateCloseTimer.Start();

            return;
        }

        // アクティブになったら「閉じ予約」をキャンセル
        _deactivateCloseTimer?.Stop();

        // 初回だけTopMost
        if (!_topMostOnce)
        {
            _topMostOnce = true;
            MakeTopMost();
        }

        // 初回だけフォーカス
        if (_focusedOnce) return;
        _focusedOnce = true;

        DispatcherQueue.TryEnqueue(() =>
        {
            FilterBox?.Focus(FocusState.Programmatic);
            FilterBox?.SelectAll();
        });
    }

    private void SearchIcon_Click(object sender, RoutedEventArgs e)
    {
        FilterBox.Focus(FocusState.Programmatic);
    }

    private void CloseAllAtsumareWindows()
    {
        if (_closing) return;
        _closing = true;

        Debug.WriteLine("CloseAllAtsumareWindows called");

        var list = App.OpenWindows.ToList();
        foreach (var w in list)
        {
            try { w._closing = true; w.Close(); } catch { }
        }
        App.OpenWindows.Clear();
    }

    // =========================
    // ここが「指定アプリ(PID)の全ウィンドウを指定モニターへ寄せる」本体
    //  - 最大化/スナップ（矩形）を維持
    //  - 既に同じモニターならスキップ
    // =========================

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPlacement(IntPtr hWnd, [In] ref WINDOWPLACEMENT lpwndpl);

    private static RECT MapRectByWorkArea(RECT r, RECT srcWork, RECT dstWork)
    {
        int sw = Math.Max(1, srcWork.Right - srcWork.Left);
        int sh = Math.Max(1, srcWork.Bottom - srcWork.Top);

        int dw = Math.Max(1, dstWork.Right - dstWork.Left);
        int dh = Math.Max(1, dstWork.Bottom - dstWork.Top);

        double rx = (double)(r.Left - srcWork.Left) / sw;
        double ry = (double)(r.Top - srcWork.Top) / sh;
        double rw = (double)(r.Right - r.Left) / sw;
        double rh = (double)(r.Bottom - r.Top) / sh;

        int left = dstWork.Left + (int)Math.Round(rx * dw);
        int top = dstWork.Top + (int)Math.Round(ry * dh);
        int w = (int)Math.Round(rw * dw);
        int h = (int)Math.Round(rh * dh);

        w = Math.Max(50, w);
        h = Math.Max(50, h);

        if (left + w > dstWork.Right) left = dstWork.Right - w;
        if (top + h > dstWork.Bottom) top = dstWork.Bottom - h;
        if (left < dstWork.Left) left = dstWork.Left;
        if (top < dstWork.Top) top = dstWork.Top;

        return new RECT { Left = left, Top = top, Right = left + w, Bottom = top + h };
    }

    private void MoveAllWindowsOfProcessToMonitor(uint pid, IntPtr targetMonitor)
    {
        if (pid == 0 || targetMonitor == IntPtr.Zero) return;

        var tmi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(targetMonitor, ref tmi)) return;
        var tWork = tmi.rcWork;

        var hwnds = new List<IntPtr>();

        EnumWindows((hWnd, _) =>
        {
            if (hWnd == IntPtr.Zero) return true;
            if (!IsWindowVisible(hWnd)) return true;

            GetWindowThreadProcessId(hWnd, out uint wpid);
            if (wpid != pid) return true;

            var exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

            if (IsCloaked(hWnd)) return true;

            hwnds.Add(hWnd);
            return true;
        }, IntPtr.Zero);

        foreach (var hWnd in hwnds)
        {
            // ★既に同じディスプレイならスキップ
            var currentMon = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
            if (currentMon == targetMonitor)
                continue;

            // 元モニター work area
            var smi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(currentMon, ref smi)) continue;
            var sWork = smi.rcWork;

            // 状態
            var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            bool hasWp = GetWindowPlacement(hWnd, ref wp);
            int show = hasWp ? wp.showCmd : SW_SHOWNORMAL;

            bool wasMin = IsIconic(hWnd);
            bool wasMax = show == SW_SHOWMAXIMIZED;

            if (wasMin)
                ShowWindow(hWnd, SW_RESTORE);

            // 現在矩形（スナップはここが効く）
            if (!GetWindowRect(hWnd, out var curRect))
                continue;

            // 最大化は一旦通常へ（rcNormalPosition を基準に動かす）
            if (wasMax && hasWp)
            {
                wp.showCmd = SW_SHOWNORMAL;
                SetWindowPlacement(hWnd, ref wp);

                curRect = wp.rcNormalPosition;
            }

            // ターゲットへ写像
            var mapped = MapRectByWorkArea(curRect, sWork, tWork);

            bool ok = SetWindowPos(
                hWnd,
                IntPtr.Zero,
                mapped.Left,
                mapped.Top,
                mapped.Right - mapped.Left,
                mapped.Bottom - mapped.Top,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSENDCHANGING);

            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                Debug.WriteLine($"SetWindowPos FAILED err={err} hwnd=0x{hWnd.ToInt64():X}");
                continue;
            }

            // 最大化復帰（rcNormalPosition も更新しておくと安定）
            if (hasWp)
            {
                wp.rcNormalPosition = mapped;

                if (wasMax)
                {
                    wp.showCmd = SW_SHOWMAXIMIZED;
                    SetWindowPlacement(hWnd, ref wp);
                    ShowWindow(hWnd, SW_MAXIMIZE);
                }
                else
                {
                    wp.showCmd = SW_SHOWNORMAL;
                    SetWindowPlacement(hWnd, ref wp);
                }
            }
            else
            {
                if (wasMax)
                    ShowWindow(hWnd, SW_MAXIMIZE);
            }
        }
    }

    // =========================
    // 起動中ウィンドウ一覧作成（従来）
    // =========================

    private async Task ReloadRunningWindowsAsync()
    {
        var windows = new List<(IntPtr hWnd, string title, uint pid)>();

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsAltTabWindow(hWnd))
                return true;

            int length = GetWindowTextLength(hWnd);
            if (length <= 0) return true;

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(title)) return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            windows.Add((hWnd, title, pid));
            return true;
        }, IntPtr.Zero);

        var fg = GetForegroundWindow();

        var groups = windows
            .GroupBy(w => w.pid)
            .Select(g =>
            {
                var fgItem = g.FirstOrDefault(x => x.hWnd == fg);
                if (fgItem.hWnd != IntPtr.Zero)
                    return fgItem;

                (IntPtr hWnd, string title, uint pid) best = default;
                long bestArea = -1;

                foreach (var x in g)
                {
                    if (!GetWindowRect(x.hWnd, out var r))
                        continue;

                    long w = r.Right - r.Left;
                    long h = r.Bottom - r.Top;
                    if (w <= 0 || h <= 0)
                        continue;

                    long area = w * h;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = x;
                    }
                }

                if (best.hWnd != IntPtr.Zero)
                    return best;

                return g.First();
            })
            .ToList();

        AllItems.Clear();

        foreach (var w in groups)
        {
            var icon = await GetWindowIconAsync(w.hWnd);
            var appName = GetAppDisplayName(w.pid, w.title);

            AllItems.Add(new AppGroupItem
            {
                AppName = appName,
                WindowTitle = w.title,
                Icon = icon,
                Pid = w.pid,
                Hwnd = w.hWnd,
                Description = $"PID: {w.pid}"
            });
        }

        ApplyFilter(FilterBox.Text);
    }

    // =========================
    // Icon / 判定系（従来）
    // =========================

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const int BI_RGB = 0;

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out BITMAP lpvObject);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr hdc,
        IntPtr hbmp,
        uint uStartScan,
        uint cScanLines,
        byte[] lpvBits,
        ref BITMAPINFO lpbmi,
        uint uUsage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    private const int DWMWA_CLOAKED = 14;

    [DllImport("user32.dll")]
    private static extern IntPtr GetLastActivePopup(IntPtr hWnd);

    private async Task<ImageSource?> GetWindowIconAsync(IntPtr hWnd)
    {
        IntPtr hIcon = SendMessage(hWnd, WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero);

        if (hIcon == IntPtr.Zero)
            hIcon = SendMessage(hWnd, WM_GETICON, (IntPtr)ICON_SMALL2, IntPtr.Zero);

        if (hIcon == IntPtr.Zero)
            hIcon = SendMessage(hWnd, WM_GETICON, (IntPtr)ICON_SMALL, IntPtr.Zero);

        if (hIcon == IntPtr.Zero)
            hIcon = GetClassLongPtr(hWnd, GCLP_HICON);

        if (hIcon == IntPtr.Zero)
            hIcon = GetClassLongPtr(hWnd, GCLP_HICONSM);

        if (hIcon == IntPtr.Zero)
            return null;

        return await HiconToImageSourceAsync(hIcon);
    }

    private static async Task<ImageSource?> HiconToImageSourceAsync(IntPtr hIcon)
    {
        if (!GetIconInfo(hIcon, out var ii))
            return null;

        try
        {
            var hbmp = ii.hbmColor != IntPtr.Zero ? ii.hbmColor : ii.hbmMask;
            if (hbmp == IntPtr.Zero)
                return null;

            if (GetObject(hbmp, Marshal.SizeOf<BITMAP>(), out var bmp) == 0)
                return null;

            int width = bmp.bmWidth;
            int height = Math.Abs(bmp.bmHeight);
            if (width <= 0 || height <= 0)
                return null;

            var bi = new BITMAPINFO();
            bi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
            bi.bmiHeader.biWidth = width;
            bi.bmiHeader.biHeight = -height;
            bi.bmiHeader.biPlanes = 1;
            bi.bmiHeader.biBitCount = 32;
            bi.bmiHeader.biCompression = BI_RGB;

            var pixels = new byte[width * height * 4];

            var hdc = GetDC(IntPtr.Zero);
            try
            {
                int scan = GetDIBits(hdc, hbmp, 0, (uint)height, pixels, ref bi, 0);
                if (scan == 0)
                    return null;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }

            var sb = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
            sb.CopyFromBuffer(pixels.AsBuffer());

            var src = new SoftwareBitmapSource();
            await src.SetBitmapAsync(sb);
            return src;
        }
        finally
        {
            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
            if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
        }
    }

    private static bool IsAltTabWindow(IntPtr hWnd)
    {
        if (!IsWindowVisible(hWnd))
            return false;

        if (IsCloaked(hWnd))
            return false;

        var root = GetAncestor(hWnd, GA_ROOTOWNER);
        var last = GetLastActivePopup(root);
        if (last != hWnd)
            return false;

        var exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
        if ((exStyle & WS_EX_TOOLWINDOW) != 0)
            return false;

        if (GetWindowTextLength(hWnd) <= 0)
            return false;

        return true;
    }

    private static bool IsCloaked(IntPtr hWnd)
    {
        if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) != 0)
            return false;

        return cloaked != 0;
    }

    private void AppsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var w = e.NewSize.Width;
        if (w <= 0) return;

        const double itemMarginLR = 12;
        const double baseTile = 140;

        var columns = Math.Max(1, (int)Math.Floor(w / (baseTile + itemMarginLR)));
        var tile = (w / columns) - itemMarginLR;
        tile = Math.Max(140, Math.Min(200, tile));

        if (AppsGrid.ItemsPanelRoot is ItemsWrapGrid panel)
        {
            panel.ItemWidth = tile;
        }
    }

    private static string? TryGetExePath(uint pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;

        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            if (!QueryFullProcessImageName(h, 0, sb, ref size))
                return null;

            return sb.ToString();
        }
        finally
        {
            CloseHandle(h);
        }
    }

    private static string GetAppDisplayName(uint pid, string fallback)
    {
        var exe = TryGetExePath(pid);
        if (string.IsNullOrEmpty(exe))
            return fallback;

        try
        {
            var v = FileVersionInfo.GetVersionInfo(exe);
            var name =
                v.FileDescription ??
                v.ProductName ??
                Path.GetFileNameWithoutExtension(exe);

            name = (name ?? "").Trim();
            return string.IsNullOrEmpty(name) ? fallback : name;
        }
        catch
        {
            return fallback;
        }
    }
}

public sealed class AppGroupItem
{
    public string AppName { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public ImageSource? Icon { get; set; }
    public IntPtr Hwnd { get; set; }
    public uint Pid { get; set; }
    public string Description { get; set; } = "";
}