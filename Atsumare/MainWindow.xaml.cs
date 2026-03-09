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
using System.Security.Cryptography;
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

    // 縺薙・繧ｦ繧｣繝ｳ繝峨え繧帝哩縺倥ｋ蜃ｦ逅・′騾ｲ陦御ｸｭ縺ｪ繧・true・亥､夐㍾ Close 髦ｲ豁｢逕ｨ・・
    public bool IsClosing { get; internal set; }

    // 縺薙・ MainWindow 縺梧球蠖薙☆繧九ち繝ｼ繧ｲ繝・ヨ繝｢繝九ち繝ｼ・医け繝ｪ繝・け譎ゅ↓縺薙％縺ｸ蟇・○繧具ｼ・
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

    // 笘・ｿｽ蜉・壹え繧｣繝ｳ繝峨え蟇ｿ蜻ｽ縺ｫ邏舌▼縺上く繝｣繝ｳ繧ｻ繝ｫ・郁ｵｷ蜍穂ｸｭ縺ｫ髢峨§縺ｦ繧り誠縺｡縺ｪ縺・ｈ縺・↓縺吶ｋ・・
    private readonly CancellationTokenSource _lifetimeCts = new();

    private void OpenSettings()
    {
        // 譌｢縺ｫ髢九＞縺ｦ縺・ｋ蝣ｴ蜷医・蜑埼擇縺ｸ
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

        try
        {
            ConfigureWindow();
            ConfigureTitleBarColors();
            SetWindowSize(820, 540);
        }
        catch (Exception ex)
        {
            App.LogLine($"[MainWindow] Initial window chrome failed: {ex}");
        }

        try { SystemBackdrop = new MicaBackdrop(); }
        catch { SystemBackdrop = null; }

        try
        {
            HookCloseBehavior();
        }
        catch (Exception ex)
        {
            App.LogLine($"[MainWindow] HookCloseBehavior failed: {ex}");
        }

        // Esc 縺ｧ髢峨§繧具ｼ・ 蜈ｨ繧ｦ繧｣繝ｳ繝峨え繧帝哩縺倥ｋ・・
        this.Content.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                CloseAllAtsumareWindows();
                return;
            }

            // Ctrl + P 縺ｧ險ｭ螳夲ｼ遺ｻ蠢・ｦ√↑繧峨く繝ｼ繧貞､画峩縺励※縺上□縺輔＞・・
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

        // 繝輔か繝ｼ繧ｫ繧ｹ髢｢騾｣
        this.Activated += MainWindow_Activated;

        // 蛻晄悄迥ｶ諷九・蜈ｨ莉ｶ陦ｨ遉ｺ
        ApplyFilter("");

        // 襍ｷ蜍墓凾縺ｫ螳溯｡御ｸｭ繧ｦ繧｣繝ｳ繝峨え縺ｮ荳隕ｧ繧偵Ο繝ｼ繝会ｼ磯哩縺倥ｉ繧後◆繧峨く繝｣繝ｳ繧ｻ繝ｫ・・
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                Breadcrumbs.Add("Reload start");
                await ReloadRunningWindowsAsync(_lifetimeCts.Token);
                Breadcrumbs.Add("Reload end");
            }
            catch (OperationCanceledException)
            {
                // 邨ゆｺ・ｸｭ縺ｮ豁｣蟶ｸ邉ｻ
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ReloadRunningWindowsAsync failed: " + ex);
                App.LogLine($"[MainWindow] ReloadRunningWindowsAsync failed: {ex}");
            }
        });

        // Closed・壼ｯｿ蜻ｽ邨ゆｺ・ｼ医く繝｣繝ｳ繧ｻ繝ｫ竊丹penWindows縺九ｉ髯､蜴ｻ・・
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
        Breadcrumbs.Add("Closing handler entered");
        // AppWindow.Closing 縺ｯ繧ｭ繝｣繝ｳ繧ｻ繝ｫ蜿ｯ閭ｽ・・indow.Closed 縺ｯ荳榊庄・・
        var appWindow = GetAppWindow();
        appWindow.Closing += (_, e) =>
        {
            try
            {
                // CloseAllAtsumareWindows() 邨檎罰縺ｮ Close 縺ｯ騾壹☆
                if (IsClosing)
                    return;

                // 笘・ヨ繝ｬ繧､驕狗畑・郁ｵｷ蜍墓凾繝医Ξ繧､譛蟆丞喧/髢峨§繧銀・繝医Ξ繧､ 縺ｪ縺ｩ・峨↑繧峨碁哩縺倥ｋ・晞國縺吶・
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

                // 笘・壼ｸｸ驕狗畑縺ｪ繧峨悟・繧ｦ繧｣繝ｳ繝峨え繧帝哩縺倥ｋ縲・
                // 縺薙％縺ｧ繧ｭ繝｣繝ｳ繧ｻ繝ｫ・気loseAll 縺ｫ蟇・○繧九→縲∬ｵｷ蜍慕峩蠕後〒繧る哩縺倬・′螳牙ｮ壹＠繧・☆縺・
                e.Cancel = true;

                // 襍ｷ蜍穂ｸｭ縺ｮ髱槫酔譛溘ｒ蜈医↓豁｢繧√ｋ・磯哩縺倥ｋ騾比ｸｭ縺ｮUI譖ｴ譁ｰ繧帝亟豁｢・・
                try { _lifetimeCts.Cancel(); } catch { }

                // UI繧ｹ繝ｬ繝・ラ縺ｧ縺ｾ縺ｨ繧√※髢峨§繧・
                if (!DispatcherQueue.TryEnqueue(() => CloseAllAtsumareWindows()))
                {
                    // 蠢ｵ縺ｮ縺溘ａ逶ｴ蜻ｼ縺ｳ・磯壼ｸｸ縺ｯ縺薙％縺ｫ譚･縺ｾ縺帙ｓ・・
                    CloseAllAtsumareWindows();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("AppWindow.Closing handler failed: " + ex);
                // 縺薙％縺ｧ蜀阪せ繝ｭ繝ｼ縺励↑縺・ｼ・IT 繧帝∩縺代ｋ・・
            }
        };
    }

    // 縲瑚ｵｷ蜍墓凾繝医Ξ繧､譛蟆丞喧縲阪ｒ蜷ｫ繧窶懊ヨ繝ｬ繧､驕狗畑繝輔Λ繧ｰ窶昴ｒ蠎・ａ縺ｫ諡ｾ縺・ｼ医・繝ｭ繝代ユ繧｣蜷阪′驕輔▲縺ｦ繧り誠縺｡縺ｪ縺・ｼ・
    private static bool ShouldCloseToTray()
    {
        // 縺ゅ↑縺溘・ SettingsStore 縺ｮ螳溘・繝ｭ繝代ユ繧｣蜷阪↓蜷医ｏ縺帙※縲∝ｿ・ｦ√↑繧牙呵｣懊ｒ霑ｽ蜉縺励※縺上□縺輔＞
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

        // WinUI3縺ｮWindow縺ｫ Hide() 縺後↑縺・◆繧・Win32 縺ｧ髫縺・
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

    // 陦ｨ遉ｺ蜑阪↓ Win32 縺ｧ菴咲ｽｮ繧貞ｽ薙※縺ｦ縺翫￥・医■繧峨▽縺堺ｽ取ｸ幢ｼ・
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
        // 萓・・壹す繝ｳ繧ｰ繝ｫ繝医Φ驕狗畑・医♀縺吶☆繧・ｼ壼､夐㍾襍ｷ蜍暮亟豁｢・・
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
        try
        {
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
        catch (Exception ex)
        {
            App.LogLine($"[MainWindow] Activated handler failed: {ex}");
        }
    }

    // GridView click: 縺薙・繧｢繝励Μ(PID)縺ｮ蜈ｨ繧ｦ繧｣繝ｳ繝峨え繧呈欠螳壹Δ繝九ち繝ｼ縺ｸ蟇・○縺ｦ縲、tsumare 繧帝哩縺倥ｋ
    private void GridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AppGroupItem item) return;

        try
        {
            var myHwnd = WindowNative.GetWindowHandle(this);
            var targetMon = _targetMonitorForThisWindow != IntPtr.Zero
                ? _targetMonitorForThisWindow
                : MonitorFromWindow(myHwnd, MONITOR_DEFAULTTONEAREST);

            MoveAllWindowsOfProcessToMonitor(item.Pid, targetMon);

            DispatcherQueue.TryEnqueue(CloseAllAtsumareWindows);
        }
        catch (Exception ex)
        {
            App.LogLine($"[MainWindow] ItemClick failed pid={item.Pid}: {ex}");
        }
    }

    // =========================
    // Close all Atsumare windows (safe)
    // =========================
    private static int _closingWindows; // 0/1 (Interlocked 縺ｧ繧ｬ繝ｼ繝・

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

                    // UI 繧ｹ繝ｬ繝・ラ縺ｧ Close・亥挨繧ｹ繝ｬ繝・ラ Close 繧帝∩縺代ｋ・・
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
                        // 蠢ｵ縺ｮ縺溘ａ
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
            try
            {
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
                else if (wasMax)
                {
                    ShowWindow(hWnd, SW_MAXIMIZE);
                }
            }
            catch (Exception ex)
            {
                App.LogLine($"[Move] MoveAllWindowsOfProcessToMonitor failed hwnd=0x{hWnd.ToInt64():X}: {ex}");
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

        // 蛻玲嫌・医く繝｣繝ｳ繧ｻ繝ｫ縺輔ｌ縺溘ｉ譌ｩ譛溽ｵゆｺ・ｼ・
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
                // foreground 縺ｮ PID 縺ｪ繧峨◎繧後ｒ陦ｨ遉ｺ
                var fgItem = g.FirstOrDefault(x => x.hWnd == fg);
                if (fgItem.hWnd != IntPtr.Zero)
                    return fgItem;

                // 譛螟ｧ髱｢遨阪・繧ｦ繧｣繝ｳ繝峨え繧定｡ｨ遉ｺ
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

        // 笘・％縺薙°繧・UI/繝舌う繝ｳ繝牙ｯｾ雎｡繧定ｧｦ繧九・縺ｧ縲√く繝｣繝ｳ繧ｻ繝ｫ蠕後・隗ｦ繧峨↑縺・
        if (ct.IsCancellationRequested) return;

        AllItems.Clear();

        foreach (var w in groups)
        {
            ct.ThrowIfCancellationRequested();
            if (IsClosing) return;

            Breadcrumbs.Add($"Icon start pid={w.pid} hwnd=0x{w.hWnd.ToInt64():X}");
            var icon = await GetWindowIconAsync(w.hWnd, w.pid);
            Breadcrumbs.Add($"Icon end pid={w.pid}");
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
            // 蜿門ｾ励〒縺阪↑縺・ｴ蜷医′縺ゅｋ・域ｨｩ髯・譌｢縺ｫ邨ゆｺ・↑縺ｩ・峨・ 縺ｫ縺励※ PID 縺ｮ縺ｿ縺ｧ霑台ｼｼ縺吶ｋ
            return 0;
        }
    }

    // =========================
    // Icon helpers
    // =========================
    private async Task<ImageSource?> GetWindowIconAsync(IntPtr hWnd, uint pid)
    {
        var key = (pid, TryGetProcessStartTicks(pid));

        // 譌｢縺ｫ縺ゅｌ縺ｰ霑斐☆・・ull 繧ゅく繝｣繝・す繝･縺励※辟｡髯舌Μ繝医Λ繧､繧帝亟豁｢・・
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
            var name = Process.GetProcessById((int)pid).ProcessName; // "chrome" 縺ｪ縺ｩ
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

