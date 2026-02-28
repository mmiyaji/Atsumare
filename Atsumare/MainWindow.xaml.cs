using Microsoft.UI;
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
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using WinRT.Interop;

namespace Atsumare;

public sealed partial class MainWindow : Window
{
    // =========================
    // UI Bindings
    // =========================
    public ObservableCollection<AppGroupItem> AllItems { get; } = new();
    public ObservableCollection<AppGroupItem> FilteredItems { get; } = new();

    private bool _focusedOnce;
    private bool _topMostOnce;

    // このウィンドウを閉じる処理が進行中なら true（多重 Close 防止用）
    public bool IsClosing { get; internal set; }

    // この MainWindow が担当するターゲットモニター（クリック時にここへ寄せる）
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

    private AppWindow? _cachedAppWindow;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(uint pid, long startTicks), ImageSource?> _iconCache
        = new();

    private SettingsWindow? _settingsWindow;

    // ★追加：ウィンドウ寿命に紐づくキャンセル（起動中に閉じても落ちないようにする）
    private readonly CancellationTokenSource _lifetimeCts = new();

    private void OpenSettings()
    {
        // 既に開いている場合は前面へ
        if (_settingsWindow != null)
        {
            try
            {
                _settingsWindow.Activate();
                return;
            }
            catch { _settingsWindow = null; }
        }

        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, __) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    public MainWindow()
    {
        InitializeComponent();

        ConfigureWindow();
        ConfigureTitleBarColors();
        SetWindowSize(820, 540);

        try { SystemBackdrop = new MicaBackdrop(); }
        catch { SystemBackdrop = null; }

        // ★重要：×で閉じた時の挙動（トレイ運用/通常終了）をここで統一
        HookCloseBehavior();

        // Esc で閉じる（= 全ウィンドウを閉じる）
        this.Content.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                CloseAllAtsumareWindows();
                return;
            }

            // Ctrl + P で設定（※必要ならキーを変更してください）
            if (e.Key == Windows.System.VirtualKey.P)
            {
                var ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                            & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

                if (ctrl)
                {
                    e.Handled = true;
                    OpenSettings();
                    return;
                }
            }
        };

        // フォーカス関連
        this.Activated += MainWindow_Activated;

        // 初期状態は全件表示
        ApplyFilter("");

        // 起動時に実行中ウィンドウの一覧をロード（閉じられたらキャンセル）
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await ReloadRunningWindowsAsync(_lifetimeCts.Token);
            }
            catch (OperationCanceledException)
            {
                // 終了中の正常系
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ReloadRunningWindowsAsync failed: " + ex);
            }
        });

        // Closed：寿命終了（キャンセル→OpenWindowsから除去）
        this.Closed += (_, __) =>
        {
            try { _lifetimeCts.Cancel(); } catch { }
            try { _lifetimeCts.Dispose(); } catch { }
            try { App.OpenWindows.Remove(this); } catch { }
        };
    }

    // =========================
    // Close behavior (Tray / Exit)
    // =========================
    private void HookCloseBehavior()
    {
        // AppWindow.Closing はキャンセル可能（Window.Closed は不可）
        var appWindow = GetAppWindow();
        appWindow.Closing += (_, e) =>
        {
            try
            {
                // CloseAllAtsumareWindows() 経由の Close は通す
                if (IsClosing)
                    return;

                // ★トレイ運用（起動時トレイ最小化/閉じる→トレイ など）なら「閉じる＝隠す」
                if (ShouldCloseToTray())
                {
                    e.Cancel = true;

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        try { HideToTray(); }
                        catch (Exception ex) { Debug.WriteLine("HideToTray failed: " + ex); }
                    });

                    return;
                }

                // ★通常運用なら「全ウィンドウを閉じる」
                // ここでキャンセル＋CloseAll に寄せると、起動直後でも閉じ順が安定しやすい
                e.Cancel = true;

                // 起動中の非同期を先に止める（閉じる途中のUI更新を防止）
                try { _lifetimeCts.Cancel(); } catch { }

                // UIスレッドでまとめて閉じる
                if (!DispatcherQueue.TryEnqueue(() => CloseAllAtsumareWindows()))
                {
                    // 念のため直呼び（通常はここに来ません）
                    CloseAllAtsumareWindows();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("AppWindow.Closing handler failed: " + ex);
                // ここで再スローしない（JIT を避ける）
            }
        };
    }

    // 「起動時トレイ最小化」を含む“トレイ運用フラグ”を広めに拾う（プロパティ名が違っても落ちない）
    private static bool ShouldCloseToTray()
    {
        // あなたの SettingsStore の実プロパティ名に合わせて、必要なら候補を追加してください
        return GetBoolSettingAny(
            "MinimizeToTrayOnClose",
            "CloseToTray",
            "TrayOnClose",
            "MinimizeToTray",
            "EnableTrayMode",
            "TrayMode",
            "StartMinimizedToTray",
            "StartToTray",
            "TrayMinimizeOnStartup",
            "StartMinimizeToTray"
        );
    }

    private static bool GetBoolSettingAny(params string[] names)
    {
        object? current = null;
        try { current = SettingsStore.Current; } catch { return false; }
        if (current == null) return false;

        var t = current.GetType();
        foreach (var name in names)
        {
            try
            {
                var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (p == null) continue;
                if (p.PropertyType != typeof(bool)) continue;

                if (p.GetValue(current) is bool b)
                    return b;
            }
            catch { }
        }

        return false;
    }

    private void HideToTray()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero) return;

        // WinUI3のWindowに Hide() がないため Win32 で隠す
        ShowWindow(hwnd, SW_HIDE);
    }

    internal void InitializeForMonitor(IntPtr targetMonitor)
    {
        _targetMonitorForThisWindow = targetMonitor;
    }

    // =========================
    // Window / TitleBar
    // =========================
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

        titleBar.ButtonHoverBackgroundColor = isDark
            ? Windows.UI.Color.FromArgb(255, 45, 45, 45)
            : Windows.UI.Color.FromArgb(255, 230, 230, 230);

        titleBar.ButtonPressedBackgroundColor = isDark
            ? Windows.UI.Color.FromArgb(255, 60, 60, 60)
            : Windows.UI.Color.FromArgb(255, 210, 210, 210);

        titleBar.InactiveBackgroundColor = bg;
        titleBar.ButtonInactiveBackgroundColor = bg;
    }

    private void SetWindowSize(int width, int height)
        => GetAppWindow().Resize(new Windows.Graphics.SizeInt32(width, height));

    // 表示前に Win32 で位置を当てておく（ちらつき低減）
    public void PrePositionToMonitorCenter(IntPtr hMon, int width, int height)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero) return;

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMon, ref mi)) return;

        var work = mi.rcWork;

        int x = work.Left + (work.Right - work.Left - width) / 2;
        int y = work.Top + (work.Bottom - work.Top - height) / 2;

        SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSENDCHANGING);
    }

    private void MakeTopMostOnce()
    {
        if (_topMostOnce) return;
        _topMostOnce = true;

        var appWindow = GetAppWindow();
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
        }
    }

    // =========================
    // UI Events (Search / Activate / Click)
    // =========================
    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyFilter(FilterBox.Text);

    private void SearchIcon_Click(object sender, RoutedEventArgs e)
        => FilterBox.Focus(FocusState.Programmatic);

    private void SettingsButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // 例2：シングルトン運用（おすすめ：多重起動防止）
        App.ShowSettings();
    }

    private void ApplyFilter(string text)
    {
        var q = (text ?? "").Trim().ToLowerInvariant();

        IEnumerable<AppGroupItem> items = AllItems;
        if (!string.IsNullOrEmpty(q))
        {
            items = AllItems.Where(x =>
                (x.AppName ?? "").ToLowerInvariant().Contains(q) ||
                (x.Description ?? "").ToLowerInvariant().Contains(q));
        }

        FilteredItems.Clear();
        foreach (var it in items) FilteredItems.Add(it);
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        // 非アクティブになったら閉じる（設定により抑制する場合もある）
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            if (App.IsAutoCloseSuppressed())
                return;

            if (!IsForegroundOurProcess())
            {
                CloseAllAtsumareWindows();
            }
            return;
        }

        MakeTopMostOnce();

        if (_focusedOnce) return;
        _focusedOnce = true;

        DispatcherQueue.TryEnqueue(() =>
        {
            FilterBox?.Focus(FocusState.Programmatic);
            FilterBox?.SelectAll();
        });
    }

    // GridView click: このアプリ(PID)の全ウィンドウを指定モニターへ寄せて、Atsumare を閉じる
    private void GridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AppGroupItem item) return;

        var myHwnd = WindowNative.GetWindowHandle(this);
        var targetMon = _targetMonitorForThisWindow != IntPtr.Zero
            ? _targetMonitorForThisWindow
            : MonitorFromWindow(myHwnd, MONITOR_DEFAULTTONEAREST);

        MoveAllWindowsOfProcessToMonitor(item.Pid, targetMon);

        // 最後に閉じる
        DispatcherQueue.TryEnqueue(CloseAllAtsumareWindows);
    }

    // =========================
    // Close all Atsumare windows (safe)
    // =========================
    private static int _closingWindows; // 0/1 (Interlocked でガード)

    private static void CloseAllAtsumareWindows()
    {
        if (Interlocked.Exchange(ref _closingWindows, 1) == 1)
            return;

        try
        {
            foreach (var w in App.OpenWindows.ToArray())
            {
                try
                {
                    if (w == null) continue;
                    if (w.IsClosing) continue;

                    w.IsClosing = true;

                    // UI スレッドで Close（別スレッド Close を避ける）
                    var dq = w.DispatcherQueue;
                    if (dq != null)
                    {
                        dq.TryEnqueue(() =>
                        {
                            try { w.Close(); }
                            catch (Exception ex) { Debug.WriteLine("Window.Close failed: " + ex); }
                        });
                    }
                    else
                    {
                        // 念のため
                        try { w.Close(); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("CloseAll loop failed: " + ex);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _closingWindows, 0);
        }
    }

    // =========================
    // Move windows of a process to a monitor (keep maximize/snap)
    // =========================
    private void MoveAllWindowsOfProcessToMonitor(uint pid, IntPtr targetMonitor)
    {
        if (pid == 0 || targetMonitor == IntPtr.Zero) return;

        var tmi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(targetMonitor, ref tmi)) return;
        var targetWork = tmi.rcWork;

        var hwnds = EnumerateTopLevelWindowsByPid(pid);
        App.LogVerbose($"[Move] pid={pid} targetMon=0x{targetMonitor.ToInt64():X} windows={hwnds.Count}");

        foreach (var hWnd in hwnds)
        {
            // 既に同じモニターならスキップ
            var currentMon = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
            if (currentMon == targetMonitor)
                continue;

            var smi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(currentMon, ref smi))
                continue;

            var srcWork = smi.rcWork;

            var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            bool hasWp = GetWindowPlacement(hWnd, ref wp);
            int showCmd = hasWp ? wp.showCmd : SW_SHOWNORMAL;

            bool wasMin = IsIconic(hWnd);
            bool wasMax = showCmd == SW_SHOWMAXIMIZED;

            if (wasMin)
                ShowWindow(hWnd, SW_RESTORE);

            if (!GetWindowRect(hWnd, out var curRect))
                continue;

            // 最大化は一旦通常に戻して rcNormalPosition を基準に移動
            if (wasMax && hasWp)
            {
                wp.showCmd = SW_SHOWNORMAL;
                SetWindowPlacement(hWnd, ref wp);
                curRect = wp.rcNormalPosition;
            }

            var mapped = MapRectByWorkArea(curRect, srcWork, targetWork);

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
                App.LogLine($"[Move] SetWindowPos FAILED err={err} hwnd=0x{hWnd.ToInt64():X}");
                continue;
            }

            App.LogVerbose($"[Move] hwnd=0x{hWnd.ToInt64():X} -> ({mapped.Left},{mapped.Top}) wasMax={wasMax} wasMin={wasMin}");

            // 状態復元（最大化）
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

    private static List<IntPtr> EnumerateTopLevelWindowsByPid(uint pid)
    {
        var hwnds = new List<IntPtr>();

        var exclude = BuildExcludeSet();

        if (ShouldExcludeByProcessName(pid, exclude))
            return hwnds;

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

        return hwnds;
    }

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

    // =========================
    // Running windows list
    // =========================
    private async Task ReloadRunningWindowsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var windows = new List<(IntPtr hWnd, string title, uint pid)>();
        var exclude = BuildExcludeSet();

        // 列挙（キャンセルされたら早期終了）
        EnumWindows((hWnd, lParam) =>
        {
            if (ct.IsCancellationRequested) return false;

            if (!IsAltTabWindow(hWnd))
                return true;

            int length = GetWindowTextLength(hWnd);
            if (length <= 0) return true;

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(title)) return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (ShouldExcludeByProcessName(pid, exclude))
                return true;

            windows.Add((hWnd, title, pid));
            return true;
        }, IntPtr.Zero);

        ct.ThrowIfCancellationRequested();
        if (IsClosing) return;

        var fg = GetForegroundWindow();

        var groups = windows
            .GroupBy(w => w.pid)
            .Select(g =>
            {
                // foreground の PID ならそれを表示
                var fgItem = g.FirstOrDefault(x => x.hWnd == fg);
                if (fgItem.hWnd != IntPtr.Zero)
                    return fgItem;

                // 最大面積のウィンドウを表示
                (IntPtr hWnd, string title, uint pid) best = default;
                long bestArea = -1;

                foreach (var x in g)
                {
                    if (ct.IsCancellationRequested) break;

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

        ct.ThrowIfCancellationRequested();
        if (IsClosing) return;

        // ★ここから UI/バインド対象を触るので、キャンセル後は触らない
        if (ct.IsCancellationRequested) return;

        AllItems.Clear();

        foreach (var w in groups)
        {
            ct.ThrowIfCancellationRequested();
            if (IsClosing) return;

            var icon = await GetWindowIconAsync(w.hWnd, w.pid);
            ct.ThrowIfCancellationRequested();
            if (IsClosing) return;

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

        App.LogVerbose($"[Reload] {AllItems.Count} apps loaded (from {windows.Count} windows)");

        if (ct.IsCancellationRequested || IsClosing) return;
        ApplyFilter(FilterBox.Text);
    }

    // =========================
    // Icon cache (minimal)
    // =========================
    private static long TryGetProcessStartTicks(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.StartTime.ToUniversalTime().Ticks;
        }
        catch
        {
            // 取得できない場合がある（権限/既に終了など）。0 にして PID のみで近似する
            return 0;
        }
    }

    // =========================
    // Icon helpers
    // =========================
    private async Task<ImageSource?> GetWindowIconAsync(IntPtr hWnd, uint pid)
    {
        var key = (pid, TryGetProcessStartTicks(pid));

        // 既にあれば返す（null もキャッシュして無限リトライを防止）
        if (_iconCache.TryGetValue(key, out var cached))
            return cached;

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
        {
            _iconCache.TryAdd(key, null);
            return null;
        }

        var icon = await HiconToImageSourceAsync(hIcon);
        _iconCache.TryAdd(key, icon);
        return icon;
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
            bi.bmiHeader.biHeight = -height; // top-down
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

    // =========================
    // Window filters / info helpers
    // =========================
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

    private static bool IsForegroundOurProcess()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;

        GetWindowThreadProcessId(fg, out uint fgPid);
        return fgPid == GetCurrentProcessId();
    }

    static HashSet<string> BuildExcludeSet()
    {
        var csv = SettingsStore.Current.ExcludeProcessNamesCsv ?? "";
        return csv
            .Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    static bool ShouldExcludeByProcessName(uint pid, HashSet<string> exclude)
    {
        if (exclude.Count == 0) return false;
        try
        {
            var name = Process.GetProcessById((int)pid).ProcessName; // "chrome" など
            return exclude.Contains(name);
        }
        catch
        {
            return false;
        }
    }

    // =========================
    // Win32 (P/Invoke / structs / constants)
    // =========================
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
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

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private const int WM_GETICON = 0x007F;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const int ICON_SMALL2 = 2;

    private const int GCLP_HICON = -14;
    private const int GCLP_HICONSM = -34;

    private const uint GA_ROOTOWNER = 3;

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSENDCHANGING = 0x0400;

    private const int SW_HIDE = 0;
    private const int SW_RESTORE = 9;
    private const int SW_SHOWNORMAL = 1;
    private const int SW_SHOWMAXIMIZED = 3;
    private const int SW_MAXIMIZE = 3;

    private const int BI_RGB = 0;

    private const int DWMWA_CLOAKED = 14;

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

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder exeName, ref int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPlacement(IntPtr hWnd, [In] ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    private static extern IntPtr GetLastActivePopup(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out BITMAP lpvObject);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr hdc,
        IntPtr hbmp,
        uint uStartScan,
        uint cScanLines,
        byte[] lpvBits,
        ref BITMAPINFO lpbmi,
        uint uUsage);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
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