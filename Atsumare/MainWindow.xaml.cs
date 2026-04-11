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
using System.Xml.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace Atsumare;

public sealed partial class MainWindow : Window
{
#if DEBUG
    private const bool ShowDeveloperDiagnostics = true;
#else
    private const bool ShowDeveloperDiagnostics = false;
#endif

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

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IconLoadResult> _iconCache
        = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<IconLoadResult>> _iconLoadTasks
        = new();
    private static readonly SemaphoreSlim _sharedReloadGate = new(1, 1);
    private static AppGroupItem[] _latestSnapshot = Array.Empty<AppGroupItem>();
    private static long _latestSnapshotTick;
    private const int DesiredIconSize = 64;
    private const int DisplayIconSize = 56;
    private static readonly TimeSpan MinimumStartupSplashDuration = E2ETestMode.IsEnabled
        ? TimeSpan.FromMilliseconds(1500)
        : TimeSpan.FromMilliseconds(450);

    private readonly Stopwatch _startupSplashStopwatch = Stopwatch.StartNew();
    private bool _initialReloadStarted;
    private int _statusMessageVersion;

    // 笘・ｿｽ蜉・壹え繧｣繝ｳ繝峨え蟇ｿ蜻ｽ縺ｫ邏舌▼縺上く繝｣繝ｳ繧ｻ繝ｫ・郁ｵｷ蜍穂ｸｭ縺ｫ髢峨§縺ｦ繧り誠縺｡縺ｪ縺・ｈ縺・↓縺吶ｋ・・
    private readonly CancellationTokenSource _lifetimeCts = new();

    private void OpenSettings()
    {
        App.ShowSettings();
    }

    public MainWindow()
    {
        InitializeComponent();
        if (this.Content is FrameworkElement root)
            root.Language = AppLanguage.GetEffectiveLanguage(SettingsStore.Current);
        Title = AppStrings.Get("MainWindow.Title");
        AppVersionBadge.Text = AppStrings.Format("MainWindow.AppVersionFormat", AppMetadata.VersionText);
        FilterBox.PlaceholderText = AppStrings.Get("MainWindow.SearchBox.PlaceholderText");
        SettingsButtonText.Text = AppStrings.Get("MainWindow.SettingsText.Text");
        MoveOverlayText.Text = AppStrings.Get("MainWindow.MoveOverlayText.Text");
        StartupSplashText.Text = AppStrings.Get("MainWindow.StartupSplashText.Text");
        EmptyStateTitle.Text = AppStrings.Get("MainWindow.EmptyStateTitle.Text");
        EmptyStateText.Text = AppStrings.Get("MainWindow.EmptyStateText.Text");
        OnboardingTitle.Text = AppStrings.Get("MainWindow.OnboardingTitle.Text");
        OnboardingText.Text = AppStrings.Get("MainWindow.OnboardingText.Text");
        OnboardingSettingsButton.Content = AppStrings.Get("MainWindow.OnboardingOpenSettings.Content");
        OnboardingDismissButton.Content = AppStrings.Get("MainWindow.OnboardingDismiss.Content");
        App.LogLine("[Splash] shown");
        DebugBanner.Visibility = ShowDeveloperDiagnostics ? Visibility.Visible : Visibility.Collapsed;
        DebugRibbon.Visibility = ShowDeveloperDiagnostics ? Visibility.Visible : Visibility.Collapsed;
        DebugRibbonTextBlock.Text = BuildDebugRibbonText();

        try
        {
            ConfigureWindow();
            ConfigureTitleBarColors();
            WindowIconHelper.Apply(this);
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
        if (this.Content is FrameworkElement loadedRoot)
            loadedRoot.Loaded += MainWindow_Loaded;

        // Closed・壼ｯｿ蜻ｽ邨ゆｺ・ｼ医く繝｣繝ｳ繧ｻ繝ｫ竊丹penWindows縺九ｉ髯､蜴ｻ・・
        this.Closed += (_, __) =>
        {
            try { _lifetimeCts.Cancel(); } catch { }
            try { _lifetimeCts.Dispose(); } catch { }
            try { App.OpenWindows.Remove(this); } catch { }
        };
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialReloadStarted)
            return;

        _initialReloadStarted = true;

        try
        {
            // Let the first frame render the splash before starting the expensive window scan.
            await Task.Delay(80);
            Breadcrumbs.Add("Reload start");
            await ReloadRunningWindowsAsync(_lifetimeCts.Token);
            Breadcrumbs.Add("Reload end");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine("ReloadRunningWindowsAsync failed: " + ex);
            App.LogLine($"[MainWindow] ReloadRunningWindowsAsync failed: {ex}");
        }
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
        try { return !E2ETestMode.IsEnabled && SettingsStore.Current.CloseButtonMinimizesToTray; }
        catch { return false; }
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

    internal void ActivateAndFocus()
    {
        try { Activate(); } catch { }

        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(50);
            TryBringToForeground();
            try
            {
                FilterBox?.Focus(FocusState.Programmatic);
                FilterBox?.SelectAll();
            }
            catch { }
        });
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
                (x.Description ?? "").ToLowerInvariant().Contains(q) ||
                (x.SearchText ?? "").ToLowerInvariant().Contains(q));
        }

        FilteredItems.Clear();
        foreach (var it in items) FilteredItems.Add(it);
        EmptyState.Visibility = FilteredItems.Count == 0 && StartupSplash.Visibility != Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
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
    private async void GridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AppGroupItem item) return;

        try
        {
            var myHwnd = WindowNative.GetWindowHandle(this);
            var targetMon = _targetMonitorForThisWindow != IntPtr.Zero
                ? _targetMonitorForThisWindow
                : MonitorFromWindow(myHwnd, MONITOR_DEFAULTTONEAREST);

            var moveResult = new MoveOperationResult();
            foreach (var pid in item.Pids.Distinct())
                MoveAllWindowsOfProcessToMonitor(pid, targetMon, moveResult);

            await RememberRecentGroupAsync(item.GroupKey);

            if (moveResult.MovedWindowCount == 0)
            {
                var message = moveResult.AccessDenied
                    ? AppStrings.Format("MainWindow.MoveFailedPermissionFormat", item.AppName)
                    : AppStrings.Format("MainWindow.MoveFailedGenericFormat", item.AppName);
                ShowStatusMessage(message, isError: true);
                return;
            }

            if (moveResult.AccessDenied)
                ShowStatusMessage(AppStrings.Format("MainWindow.MovePartialPermissionFormat", item.AppName), isError: false);

            DispatcherQueue.TryEnqueue(CloseAllAtsumareWindows);
        }
        catch (Exception ex)
        {
            App.LogLine($"[MainWindow] ItemClick failed pid={item.Pid}: {ex}");
            ShowStatusMessage(AppStrings.Format("MainWindow.MoveFailedGenericFormat", item.AppName), isError: true);
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
    private void MoveAllWindowsOfProcessToMonitor(uint pid, IntPtr targetMonitor, MoveOperationResult moveResult)
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
                    if (err == 5)
                        moveResult.AccessDenied = true;
                    continue;
                }

                App.LogVerbose($"[Move] hwnd=0x{hWnd.ToInt64():X} -> ({mapped.Left},{mapped.Top}) wasMax={wasMax} wasMin={wasMin}");
                moveResult.MovedWindowCount++;

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
        AppGroupItem[] items;

        await _sharedReloadGate.WaitAsync(ct);
        try
        {
            var ageMs = Environment.TickCount64 - Interlocked.Read(ref _latestSnapshotTick);
            if (_latestSnapshot.Length > 0 && ageMs >= 0 && ageMs <= 1500)
            {
                items = _latestSnapshot;
                LogPerf($"[Perf] Reload reused shared snapshot count={items.Length} age_ms={ageMs}");
            }
            else
            {
                items = await BuildRunningWindowSnapshotAsync(ct);
                _latestSnapshot = items;
                Interlocked.Exchange(ref _latestSnapshotTick, Environment.TickCount64);
            }
        }
        finally
        {
            _sharedReloadGate.Release();
        }

        ct.ThrowIfCancellationRequested();
        if (IsClosing) return;

        AllItems.Clear();
        foreach (var item in items)
            AllItems.Add(item);

        if (ct.IsCancellationRequested || IsClosing) return;
        ApplyFilter(FilterBox.Text);
        await HideStartupSplashAsync();
        TryShowOnboarding();
    }

    private async Task<AppGroupItem[]> BuildRunningWindowSnapshotAsync(CancellationToken ct)
    {
        var swTotal = Stopwatch.StartNew();
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
        LogPerf($"[Perf] Reload enum windows={windows.Count} elapsed_ms={swTotal.ElapsedMilliseconds}");

        ct.ThrowIfCancellationRequested();
        if (IsClosing) return Array.Empty<AppGroupItem>();

        var fg = GetForegroundWindow();

        var exePathByPid = windows
            .Select(w => w.pid)
            .Distinct()
            .ToDictionary(pid => pid, TryGetExePath);

        var appNameByPid = windows
            .Select(w => w.pid)
            .Distinct()
            .ToDictionary(
                pid => pid,
                pid =>
                {
                    var fallback = windows.FirstOrDefault(w => w.pid == pid).title;
                    return GetAppDisplayName(pid, fallback);
                });

        var groups = windows
            .GroupBy(w => BuildWindowGroupKey(w, exePathByPid, appNameByPid))
            .Select(g =>
            {
                // foreground 縺ｮ PID 縺ｪ繧峨◎繧後ｒ陦ｨ遉ｺ
                var fgItem = g.FirstOrDefault(x => x.hWnd == fg);
                if (fgItem.hWnd != IntPtr.Zero)
                    return (Representative: fgItem, Members: g.ToList());

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
                    return (Representative: best, Members: g.ToList());

                return (Representative: g.First(), Members: g.ToList());
            })
            .ToList();
        LogPerf($"[Perf] Reload group groups={groups.Count} unique_pids={exePathByPid.Count} elapsed_ms={swTotal.ElapsedMilliseconds}");

        ct.ThrowIfCancellationRequested();
        if (IsClosing) return Array.Empty<AppGroupItem>();

        // 笘・％縺薙°繧・UI/繝舌う繝ｳ繝牙ｯｾ雎｡繧定ｧｦ繧九・縺ｧ縲√く繝｣繝ｳ繧ｻ繝ｫ蠕後・隗ｦ繧峨↑縺・
        if (ct.IsCancellationRequested) return Array.Empty<AppGroupItem>();

        var unresolvedIcons = new List<(AppGroupItem item, IntPtr hWnd, uint pid, string? exePath)>();

        var items = groups.Select(group =>
        {
            var w = group.Representative;
            var exePath = exePathByPid.TryGetValue(w.pid, out var resolvedExePath) ? resolvedExePath : null;
            var iconKey = BuildIconCacheKey(w.pid, exePath);
            var cachedIcon = _iconCache.TryGetValue(iconKey, out var iconResult)
                ? iconResult
                : new IconLoadResult(null, "pending", exePath);
            var appName = appNameByPid[w.pid];
            var item = new AppGroupItem
            {
                GroupKey = BuildPreferenceKey(exePath, appName),
                BaseAppName = appName,
                AppName = BuildItemDisplayName(appName, w.pid, cachedIcon),
                WindowTitle = w.title,
                Icon = cachedIcon.Image,
                Pid = w.pid,
                Pids = group.Members.Select(x => x.pid).Distinct().ToArray(),
                Hwnd = w.hWnd,
                Description = BuildItemDescription(w.pid, cachedIcon),
                SearchText = BuildSearchText(appName, cachedIcon, exePath, w.title)
            };
            if (cachedIcon.Image == null)
                unresolvedIcons.Add((item, w.hWnd, w.pid, exePath));
            return item;
        }).ToArray();

        ApplyPreferenceMetadata(items);
        items = SortItems(items);

        LogPerf($"[Perf] Reload seed_items groups={groups.Count} unresolved_icons={unresolvedIcons.Count} elapsed_ms={swTotal.ElapsedMilliseconds}");

        _ = PopulateMissingIconsAsync(unresolvedIcons, ct);

        ct.ThrowIfCancellationRequested();
        if (IsClosing) return Array.Empty<AppGroupItem>();

        LogPerf($"[Perf] Reload ui_ready count={items.Length} elapsed_ms={swTotal.ElapsedMilliseconds}");
        App.LogVerbose($"[Reload] {items.Length} apps loaded (from {windows.Count} windows)");
        LogPerf($"[Perf] Reload complete filtered={items.Length} elapsed_ms={swTotal.ElapsedMilliseconds}");
        return items;
    }

    private static void LogPerf(string message)
    {
        if (ShowDeveloperDiagnostics || SettingsStore.Current.EnableVerboseLog)
            App.LogLine(message);
    }

    private async Task HideStartupSplashAsync()
    {
        if (StartupSplash.Visibility != Visibility.Visible)
            return;

        var remaining = MinimumStartupSplashDuration - _startupSplashStopwatch.Elapsed;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining);

        AppsGrid.Opacity = 1;
        StartupSplashRing.IsActive = false;
        StartupSplash.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = FilteredItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        App.LogLine("[Splash] hidden");
    }

    private async Task RememberRecentGroupAsync(string groupKey)
    {
        if (string.IsNullOrWhiteSpace(groupKey))
            return;

        SettingsStore.Current.RecentAppKeysCsv = SettingsWindowLogic.TouchRecentKeyCsv(
            SettingsStore.Current.RecentAppKeysCsv,
            groupKey);
        await SettingsStore.SaveAsync();
    }

    private async Task TogglePinAsync(string groupKey)
    {
        var current = SettingsStore.Current.PinnedAppKeysCsv;
        var pinned = SettingsWindowLogic.ParseCsv(current)
            .Contains(groupKey, StringComparer.OrdinalIgnoreCase);

        SettingsStore.Current.PinnedAppKeysCsv = pinned
            ? SettingsWindowLogic.RemoveCsvValue(current, groupKey)
            : SettingsWindowLogic.AddCsvValue(current, groupKey);
        await SettingsStore.SaveAsync();

        ApplyPreferenceMetadata(AllItems);
        var sorted = SortItems(AllItems);
        AllItems.Clear();
        foreach (var item in sorted)
            AllItems.Add(item);
        ApplyFilter(FilterBox.Text);
    }

    private void ShowStatusMessage(string text, bool isError)
    {
        var version = Interlocked.Increment(ref _statusMessageVersion);
        StatusBannerText.Text = text;
        StatusBannerIcon.Glyph = isError ? "\uEA39" : "\uE783";
        StatusBanner.Background = new SolidColorBrush(isError
            ? Color.FromArgb(220, 96, 27, 27)
            : Color.FromArgb(220, 49, 49, 49));
        StatusBanner.BorderBrush = new SolidColorBrush(isError
            ? Color.FromArgb(255, 255, 171, 171)
            : Color.FromArgb(120, 122, 122, 122));
        StatusBanner.Visibility = Visibility.Visible;

        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(3200);
            if (version == _statusMessageVersion)
                StatusBanner.Visibility = Visibility.Collapsed;
        });
    }

    private void TryShowOnboarding()
    {
        if (SettingsStore.Current.HasCompletedOnboarding)
            return;

        OnboardingOverlay.Visibility = Visibility.Visible;
    }

    private async void OnboardingDismissButton_Click(object sender, RoutedEventArgs e)
    {
        OnboardingOverlay.Visibility = Visibility.Collapsed;
        if (SettingsStore.Current.HasCompletedOnboarding)
            return;

        SettingsStore.Current.HasCompletedOnboarding = true;
        await SettingsStore.SaveAsync();
    }

    private async void OnboardingSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OnboardingOverlay.Visibility = Visibility.Collapsed;
        if (!SettingsStore.Current.HasCompletedOnboarding)
        {
            SettingsStore.Current.HasCompletedOnboarding = true;
            await SettingsStore.SaveAsync();
        }

        CloseAllAtsumareWindows();
        App.ShowSettings();
    }

    private async void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string groupKey || string.IsNullOrWhiteSpace(groupKey))
            return;

        await TogglePinAsync(groupKey);
        button.Opacity = ResolvePinButtonOpacity(groupKey, isHovering: true);
    }

    private void PinButton_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string groupKey)
            return;

        button.Opacity = ResolvePinButtonOpacity(groupKey, isHovering: true);
    }

    private void PinButton_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string groupKey)
            return;

        button.Opacity = ResolvePinButtonOpacity(groupKey, isHovering: false);
    }

    private double ResolvePinButtonOpacity(string groupKey, bool isHovering)
    {
        var item = AllItems.FirstOrDefault(x => string.Equals(x.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase));
        if (item?.IsPinned == true)
            return 0.96;

        return isHovering ? 0.62 : 0.16;
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
    private static string BuildItemDisplayName(string appName, uint pid, IconLoadResult iconResult)
    {
        return ShowDeveloperDiagnostics
            ? $"{appName}\n{iconResult.Source}"
            : appName;
    }

    private static string BuildItemDescription(uint pid, IconLoadResult iconResult)
    {
        return ShowDeveloperDiagnostics
            ? $"PID:{pid}"
            : "";
    }

    private static string BuildSearchText(string appName, IconLoadResult iconResult, string? exePath, string windowTitle)
    {
        var fileName = !string.IsNullOrWhiteSpace(exePath) ? Path.GetFileNameWithoutExtension(exePath) : "";
        return string.Join(" ", new[] { appName, fileName, exePath, windowTitle, iconResult.Source }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string BuildPreferenceKey(string? exePath, string appName)
    {
        if (!string.IsNullOrWhiteSpace(exePath))
            return exePath.ToLowerInvariant();

        return $"app:{appName}".ToLowerInvariant();
    }

    private static void ApplyPreferenceMetadata(IEnumerable<AppGroupItem> items)
    {
        var pinned = SettingsWindowLogic.ParseCsv(SettingsStore.Current.PinnedAppKeysCsv)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recent = SettingsWindowLogic.ParseCsv(SettingsStore.Current.RecentAppKeysCsv);
        var recentOrder = recent
            .Select((key, index) => (key, index))
            .ToDictionary(x => x.key, x => x.index, StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            item.IsPinned = pinned.Contains(item.GroupKey);
            item.RecentOrder = recentOrder.TryGetValue(item.GroupKey, out var order)
                ? order
                : int.MaxValue;
        }
    }

    private static AppGroupItem[] SortItems(IEnumerable<AppGroupItem> items) =>
        items.OrderByDescending(x => x.IsPinned)
            .ThenBy(x => x.RecentOrder)
            .ThenBy(x => x.AppName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    private static string BuildWindowGroupKey(
        (IntPtr hWnd, string title, uint pid) window,
        IReadOnlyDictionary<uint, string?> exePathByPid,
        IReadOnlyDictionary<uint, string> appNameByPid)
    {
        var exePath = exePathByPid.TryGetValue(window.pid, out var path) && !string.IsNullOrWhiteSpace(path)
            ? path!
            : $"pid:{window.pid}";
        var appName = appNameByPid.TryGetValue(window.pid, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : window.title;
        return $"{exePath}|{appName}";
    }

    private async Task PopulateMissingIconsAsync(
        IReadOnlyList<(AppGroupItem item, IntPtr hWnd, uint pid, string? exePath)> unresolvedIcons,
        CancellationToken ct)
    {
        foreach (var entry in unresolvedIcons)
        {
            if (ct.IsCancellationRequested || IsClosing)
                break;

            try
            {
                var swItem = Stopwatch.StartNew();
                Breadcrumbs.Add($"Icon start pid={entry.pid} hwnd=0x{entry.hWnd.ToInt64():X}");
                var iconResult = await GetWindowIconAsync(entry.hWnd, entry.pid, entry.exePath);
                Breadcrumbs.Add($"Icon end pid={entry.pid}");

                if (ct.IsCancellationRequested || IsClosing)
                    break;

                var tcs = new TaskCompletionSource<object?>();
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        entry.item.Icon = iconResult.Image;
                        entry.item.AppName = BuildItemDisplayName(entry.item.BaseAppName, entry.pid, iconResult);
                        entry.item.Description = BuildItemDescription(entry.pid, iconResult);
                        entry.item.SearchText = BuildSearchText(entry.item.BaseAppName, iconResult, entry.exePath, entry.item.WindowTitle);
                        tcs.TrySetResult(null);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                });
                await tcs.Task;

                LogPerf($"[Item] pid={entry.pid} hwnd=0x{entry.hWnd.ToInt64():X} exe={iconResult.ExePath ?? "<unknown>"} source={iconResult.Source}");
                LogPerf($"[Perf] Item pid={entry.pid} source={iconResult.Source} elapsed_ms={swItem.ElapsedMilliseconds}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                App.LogLine($"[MainWindow] PopulateMissingIconsAsync pid={entry.pid} failed: {ex}");
            }
        }
    }

    private async Task<IconLoadResult> GetWindowIconAsync(IntPtr hWnd, uint pid, string? exePathHint)
    {
        var key = BuildIconCacheKey(pid, exePathHint);

        if (_iconCache.TryGetValue(key, out var cached))
            return cached;

        var inFlight = _iconLoadTasks.GetOrAdd(key, _ => LoadWindowIconCoreAsync(hWnd, pid, exePathHint, key));
        try
        {
            return await inFlight;
        }
        finally
        {
            if (inFlight.IsCompleted)
                _iconLoadTasks.TryRemove(key, out _);
        }
    }

    private void TryBringToForeground()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
            return;

        ShowWindow(hwnd, SW_RESTORE);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
        SetActiveWindow(hwnd);
        SetFocus(hwnd);
    }

    private static string BuildIconCacheKey(uint pid, string? exePathHint)
    {
        if (!string.IsNullOrWhiteSpace(exePathHint))
            return $"exe:{exePathHint}".ToLowerInvariant();

        return $"pid:{pid}:start:{TryGetProcessStartTicks(pid)}";
    }

    private async Task<IconLoadResult> LoadWindowIconCoreAsync(IntPtr hWnd, uint pid, string? exePathHint, string key)
    {
        if (_iconCache.TryGetValue(key, out var cached))
            return cached;

        ImageSource? icon = null;
        var source = "none";
        IntPtr fallbackHicon = IntPtr.Zero;
        var exePath = exePathHint ?? TryGetExePath(pid);
        var traceIcon = ShouldTraceIcon(exePath);

        LogIconDecision(traceIcon, $"Start pid={pid} exe={exePath ?? "<unknown>"}");

        var preferPackagedAsset = ShouldPreferPackagedAsset(exePath);

        if (!preferPackagedAsset)
        {
            foreach (var candidate in EnumerateWindowIconCandidates(hWnd))
            {
                if (candidate == IntPtr.Zero)
                    continue;

                var iconSize = GetIconDimensions(candidate);
                LogIconDecision(traceIcon, $"Window candidate pid={pid} handle=0x{candidate.ToInt64():X} size={iconSize.width}x{iconSize.height}");
                if (iconSize.width >= DesiredIconSize || iconSize.height >= DesiredIconSize)
                {
                    icon = await HiconToImageSourceAsync(candidate);
                    source = $"window:{iconSize.width}x{iconSize.height}";
                    LogIconDecision(traceIcon, $"Selected window icon pid={pid} size={iconSize.width}x{iconSize.height}");
                    break;
                }

                if (fallbackHicon == IntPtr.Zero)
                    fallbackHicon = candidate;
            }
        }

        if (icon == null && !string.IsNullOrWhiteSpace(exePath))
        {
            var packaged = await ExtractPackagedAppLogoAsync(exePath);
            if (packaged.Image != null)
            {
                icon = packaged.Image;
                source = packaged.Source;
            }
            else
            {
                var extracted = await ExtractHighResolutionIconAsync(exePath);
                if (extracted.Image != null)
                {
                    icon = extracted.Image;
                    source = extracted.Source;
                }
            }
        }

        if (icon == null && preferPackagedAsset)
        {
            foreach (var candidate in EnumerateWindowIconCandidates(hWnd))
            {
                if (candidate == IntPtr.Zero)
                    continue;

                var iconSize = GetIconDimensions(candidate);
                LogIconDecision(traceIcon, $"Late window candidate pid={pid} handle=0x{candidate.ToInt64():X} size={iconSize.width}x{iconSize.height}");
                if (iconSize.width >= DesiredIconSize || iconSize.height >= DesiredIconSize)
                {
                    icon = await HiconToImageSourceAsync(candidate);
                    source = $"late-window:{iconSize.width}x{iconSize.height}";
                    LogIconDecision(traceIcon, $"Selected late window icon pid={pid} size={iconSize.width}x{iconSize.height}");
                    break;
                }

                if (fallbackHicon == IntPtr.Zero)
                    fallbackHicon = candidate;
            }
        }

        if (icon == null && fallbackHicon != IntPtr.Zero)
        {
            icon = await HiconToImageSourceAsync(fallbackHicon);
            source = "window:fallback";
            LogIconDecision(traceIcon, $"Fell back to small window icon pid={pid} handle=0x{fallbackHicon.ToInt64():X}");
        }

        LogIconDecision(traceIcon, $"Result pid={pid} success={icon != null}");
        var result = new IconLoadResult(icon, source, exePath);
        _iconCache.TryAdd(key, result);
        return result;
    }

    private static bool ShouldTraceIcon(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return false;

        var normalized = exePath.Replace('\\', '/');
        return normalized.Contains("/OpenAI.Codex_", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("/Codex.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldPreferPackagedAsset(string? exePath) => FindPackageInstallRoot(exePath ?? "") != null;

    private static void LogIconDecision(bool force, string message)
    {
        if (force)
            App.LogLine($"[Icon] {message}");
        else
            App.LogVerbose($"[Icon] {message}");
    }

    private static IEnumerable<IntPtr> EnumerateWindowIconCandidates(IntPtr hWnd)
    {
        yield return SendMessage(hWnd, WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero);
        yield return SendMessage(hWnd, WM_GETICON, (IntPtr)ICON_SMALL2, IntPtr.Zero);
        yield return SendMessage(hWnd, WM_GETICON, (IntPtr)ICON_SMALL, IntPtr.Zero);
        yield return GetClassLongPtr(hWnd, GCLP_HICON);
        yield return GetClassLongPtr(hWnd, GCLP_HICONSM);
    }

    private static (int width, int height) GetIconDimensions(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero || !GetIconInfo(hIcon, out var ii))
            return (0, 0);

        try
        {
            var hbmp = ii.hbmColor != IntPtr.Zero ? ii.hbmColor : ii.hbmMask;
            if (hbmp == IntPtr.Zero || GetObject(hbmp, Marshal.SizeOf<BITMAP>(), out var bmp) == 0)
                return (0, 0);

            return (bmp.bmWidth, Math.Abs(bmp.bmHeight));
        }
        finally
        {
            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
            if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
        }
    }

    private static async Task<IconLoadResult> ExtractHighResolutionIconAsync(string exePath)
    {
        IntPtr bestIcon = IntPtr.Zero;
        var bestArea = 0;
        string source = "exe:none";

        try
        {
            for (var iconIndex = 0; iconIndex < 32; iconIndex++)
            {
                var candidateIcons = new IntPtr[1];
                try
                {
                    var extracted = PrivateExtractIcons(
                        exePath,
                        iconIndex,
                        256,
                        256,
                        candidateIcons,
                        null,
                        1,
                        0);

                    if (extracted == 0 || candidateIcons[0] == IntPtr.Zero)
                        break;

                    var size = GetIconDimensions(candidateIcons[0]);
                    var area = size.width * size.height;
                    LogIconDecision(ShouldTraceIcon(exePath), $"Extracted exe icon path={exePath} index={iconIndex} size={size.width}x{size.height}");
                    if (area > bestArea)
                    {
                        if (bestIcon != IntPtr.Zero)
                            DestroyIcon(bestIcon);

                        bestIcon = candidateIcons[0];
                        bestArea = area;
                        source = $"exe:index={iconIndex}:{size.width}x{size.height}";
                        candidateIcons[0] = IntPtr.Zero;

                        if (size.width >= 256 || size.height >= 256)
                            break;
                    }
                }
                finally
                {
                    if (candidateIcons[0] != IntPtr.Zero)
                        DestroyIcon(candidateIcons[0]);
                }
            }

            if (bestIcon == IntPtr.Zero)
                return new IconLoadResult(null, source, exePath);

            LogIconDecision(ShouldTraceIcon(exePath), $"Selected exe icon path={exePath} area={bestArea}");
            return new IconLoadResult(await HiconToImageSourceAsync(bestIcon), source, exePath);
        }
        catch
        {
            return new IconLoadResult(null, source, exePath);
        }
        finally
        {
            if (bestIcon != IntPtr.Zero)
                DestroyIcon(bestIcon);
        }
    }

    private static async Task<IconLoadResult> ExtractPackagedAppLogoAsync(string exePath)
    {
        try
        {
            var traceIcon = ShouldTraceIcon(exePath);
            var installRoot = FindPackageInstallRoot(exePath);
            if (installRoot == null)
                return new IconLoadResult(null, "packaged:no-root", exePath);

            var manifestPath = Path.Combine(installRoot, "AppxManifest.xml");
            if (!File.Exists(manifestPath))
                return new IconLoadResult(null, "packaged:no-manifest", exePath);

            LogIconDecision(traceIcon, $"Packaged app manifest={manifestPath}");
            var manifest = XDocument.Load(manifestPath);
            XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
            XNamespace uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

            var square44Logo = manifest.Root?.Element(ns + "Applications")?
                .Element(ns + "Application")?
                .Element(uap + "VisualElements")?
                .Attribute("Square44x44Logo")?.Value;

            var relativeCandidates = new[]
            {
                square44Logo,
                manifest.Root?.Element(ns + "Applications")?
                    .Element(ns + "Application")?
                    .Element(uap + "VisualElements")?
                    .Attribute("Square150x150Logo")?.Value,
                manifest.Root?.Element(ns + "Properties")?
                    .Element(ns + "Logo")?.Value,
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToArray();

            foreach (var relativePath in relativeCandidates)
            {
                var assetPath = ResolveBestPackagedAssetPath(installRoot, relativePath, preferSmallLogo: true);
                LogIconDecision(traceIcon, $"Packaged asset candidate base={relativePath} resolved={assetPath ?? "<none>"}");
                if (!string.IsNullOrWhiteSpace(assetPath))
                {
                    var image = await LoadImageSourceFromFileAsync(assetPath);
                    if (image != null)
                    {
                        LogIconDecision(traceIcon, $"Selected packaged asset path={assetPath}");
                        return new IconLoadResult(image, $"packaged:{Path.GetFileName(assetPath)}", exePath);
                    }
                }
            }
        }
        catch
        {
        }

        return new IconLoadResult(null, "packaged:none", exePath);
    }

    private static string? FindPackageInstallRoot(string exePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(exePath);
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (File.Exists(Path.Combine(dir, "AppxManifest.xml")))
                    return dir;

                dir = Directory.GetParent(dir)?.FullName;
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? ResolveBestPackagedAssetPath(string installRoot, string relativeAssetPath, bool preferSmallLogo = false)
    {
        var normalized = relativeAssetPath.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var primaryPath = Path.Combine(installRoot, normalized);
        var directory = Path.GetDirectoryName(primaryPath);
        var stem = Path.GetFileNameWithoutExtension(primaryPath);
        var extension = Path.GetExtension(primaryPath);

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return File.Exists(primaryPath) ? primaryPath : null;

        var candidates = Directory.GetFiles(directory, $"{stem}*{extension}")
            .OrderByDescending(path => GetPackagedAssetScore(path, preferSmallLogo))
            .ToArray();

        return candidates.FirstOrDefault(File.Exists) ?? (File.Exists(primaryPath) ? primaryPath : null);
    }

    private static int GetPackagedAssetScore(string path, bool preferSmallLogo = false)
    {
        var fileName = Path.GetFileName(path).ToLowerInvariant();
        var score = 0;

        if (fileName.Contains("targetsize-256")) score += 5000;
        if (fileName.Contains("targetsize-96")) score += 300;
        if (fileName.Contains("targetsize-80")) score += 250;
        if (fileName.Contains("targetsize-64")) score += 200;
        if (fileName.Contains("scale-400")) score += 450;
        if (fileName.Contains("scale-200")) score += 350;
        if (fileName.Contains("scale-150")) score += 250;
        if (fileName.Contains("square150x150")) score += 150;
        if (preferSmallLogo && fileName.Contains("square44x44")) score += 2000;
        if (preferSmallLogo && fileName.Contains("square150x150")) score -= 1500;
        if (fileName.Contains("altform-unplated")) score += 75;
        if (fileName.Contains("altform-lightunplated")) score += 50;
        if (fileName == "icon.png") score += 125;

        return score;
    }

    private static async Task<ImageSource?> LoadImageSourceFromFileAsync(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var randomAccessStream = stream.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
            var softwareBitmap = await DecodeScaledBitmapAsync(decoder, DisplayIconSize, DisplayIconSize);
            return await CreateImageSourceAsync(softwareBitmap);
        }
        catch
        {
            return null;
        }
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
            var resized = await ResizeSoftwareBitmapAsync(sb, DisplayIconSize, DisplayIconSize);
            return await CreateImageSourceAsync(resized);
        }
        finally
        {
            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
            if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
        }
    }

    private static async Task<ImageSource?> CreateImageSourceAsync(SoftwareBitmap softwareBitmap)
    {
        var src = new SoftwareBitmapSource();
        await src.SetBitmapAsync(softwareBitmap);
        return src;
    }

    private static async Task<SoftwareBitmap> ResizeSoftwareBitmapAsync(SoftwareBitmap source, int width, int height)
    {
        if (source.PixelWidth == width && source.PixelHeight == height)
            return source;

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(source);
        await encoder.FlushAsync();
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        return await DecodeScaledBitmapAsync(decoder, width, height);
    }

    private static async Task<SoftwareBitmap> DecodeScaledBitmapAsync(BitmapDecoder decoder, int width, int height)
    {
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)width,
            ScaledHeight = (uint)height,
            InterpolationMode = BitmapInterpolationMode.Fant
        };

        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        var softwareBitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        softwareBitmap.CopyFromBuffer(pixelData.DetachPixelData().AsBuffer());
        return softwareBitmap;
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

    private static string BuildDebugRibbonText()
    {
        string version;
        try
        {
            if (Windows.ApplicationModel.Package.Current is { } package)
            {
                var v = package.Id.Version;
                version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            }
            else
            {
                version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            }
        }
        catch
        {
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        }

        string assemblyPath;
        DateTime buildTime;
        try
        {
            assemblyPath = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrWhiteSpace(assemblyPath))
                assemblyPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "<unknown>";

            buildTime = File.Exists(assemblyPath) ? File.GetLastWriteTime(assemblyPath) : DateTime.MinValue;
        }
        catch
        {
            assemblyPath = "<unknown>";
            buildTime = DateTime.MinValue;
        }

        var buildTimeText = buildTime == DateTime.MinValue
            ? "unknown"
            : buildTime.ToString("yyyy-MM-dd HH:mm:ss");

        return ShowDeveloperDiagnostics
            ? $"DEBUG BUILD v{version}\nPID:{Environment.ProcessId}\nBuilt:{buildTimeText}\n{Path.GetFileName(assemblyPath)}"
            : "";
    }

    static HashSet<string> BuildExcludeSet()
    {
        var csv = SettingsStore.Current.ExcludeProcessNamesCsv ?? "";
        var set = csv
            .Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            var selfProcessName = Process.GetCurrentProcess().ProcessName;
            if (!string.IsNullOrWhiteSpace(selfProcessName))
                set.Add(selfProcessName);
        }
        catch
        {
        }

        // UWP/Store app のホストで、一覧に出しても操作対象としての意味が薄い。
        set.Add("ApplicationFrameHost");
        return set;
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

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PrivateExtractIcons(
        string szFileName,
        int nIconIndex,
        int cxIcon,
        int cyIcon,
        IntPtr[] phicon,
        uint[]? piconid,
        uint nIcons,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}

public sealed class AppGroupItem : INotifyPropertyChanged
{
    private string _baseAppName = "";
    private string _appName = "";
    private string _windowTitle = "";
    private ImageSource? _icon;
    private IntPtr _hWnd;
    private uint _pid;
    private IReadOnlyList<uint> _pids = Array.Empty<uint>();
    private string _description = "";
    private string _groupKey = "";
    private string _searchText = "";
    private bool _isPinned;
    private int _recentOrder = int.MaxValue;

    public string BaseAppName
    {
        get => _baseAppName;
        set => SetProperty(ref _baseAppName, value);
    }

    public string AppName
    {
        get => _appName;
        set => SetProperty(ref _appName, value);
    }

    public string WindowTitle
    {
        get => _windowTitle;
        set => SetProperty(ref _windowTitle, value);
    }

    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public IntPtr Hwnd
    {
        get => _hWnd;
        set => SetProperty(ref _hWnd, value);
    }

    public uint Pid
    {
        get => _pid;
        set => SetProperty(ref _pid, value);
    }

    public IReadOnlyList<uint> Pids
    {
        get => _pids;
        set => SetProperty(ref _pids, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string GroupKey
    {
        get => _groupKey;
        set => SetProperty(ref _groupKey, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (SetProperty(ref _isPinned, value))
            {
                OnPropertyChanged(nameof(PinGlyph));
                OnPropertyChanged(nameof(PinOpacity));
            }
        }
    }

    public int RecentOrder
    {
        get => _recentOrder;
        set => SetProperty(ref _recentOrder, value);
    }

    public string PinGlyph => IsPinned ? "★" : "☆";

    public double PinOpacity => IsPinned ? 0.96 : 0.16;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

internal sealed record IconLoadResult(ImageSource? Image, string Source, string? ExePath);

internal sealed class MoveOperationResult
{
    internal int MovedWindowCount { get; set; }
    internal bool AccessDenied { get; set; }
}
