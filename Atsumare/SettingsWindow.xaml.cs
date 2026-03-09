using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;
using WinRT.Interop;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Windows.Storage;
using Windows.System;
namespace Atsumare;

public sealed partial class SettingsWindow : Window
{
    private bool _isLoading;
    private bool _isClosing;

    private AppWindow? _cachedAppWindow;
    private readonly ObservableCollection<HotkeyKeyItem> _hotkeyKeys = new();

    private sealed class HotkeyKeyItem
    {
        public string Label { get; set; } = "";
        public int Vk { get; set; }
        public override string ToString() => Label;
    }
    public SettingsWindow()
    {
        InitializeComponent();
        Title = "設定";

        try { SystemBackdrop = new MicaBackdrop(); }
        catch { SystemBackdrop = null; }

        Nav.SelectedItem = Nav.MenuItems[0];

        // 繝上Φ繝峨Λ縺ｯ繝｡繧ｽ繝・ラ縺ｫ縺励※縲，losed縺ｧ隗｣髯､縺吶ｋ・亥諺蜷阪Λ繝繝縺ｮ縺ｾ縺ｾ縺縺ｨ隗｣髯､縺ｧ縺阪↑縺・ｼ・
        this.Activated += SettingsWindow_Activated;
        this.SizeChanged += SettingsWindow_SizeChanged;
        this.Closed += SettingsWindow_Closed;

        InitHotkeyKeyCandidates();
        CbHotkeyKey.ItemsSource = _hotkeyKeys;

        SetWindowSizeSafe(980, 640);
        ApplyResponsiveLayout(this.Bounds.Width);
        _ = LoadAsync();
    }
    private void InitHotkeyKeyCandidates()
    {
        _hotkeyKeys.Clear();

        // 繧医￥菴ｿ縺・ｂ縺ｮ
        _hotkeyKeys.Add(new HotkeyKeyItem { Label = "Space", Vk = 0x20 });
        _hotkeyKeys.Add(new HotkeyKeyItem { Label = "Enter", Vk = 0x0D });
        _hotkeyKeys.Add(new HotkeyKeyItem { Label = "Tab", Vk = 0x09 });
        _hotkeyKeys.Add(new HotkeyKeyItem { Label = "Esc", Vk = 0x1B });

        // F1-F12
        for (int i = 1; i <= 12; i++)
            _hotkeyKeys.Add(new HotkeyKeyItem { Label = $"F{i}", Vk = 0x70 + (i - 1) });

        // A-Z
        for (int c = 'A'; c <= 'Z'; c++)
            _hotkeyKeys.Add(new HotkeyKeyItem { Label = ((char)c).ToString(), Vk = c });

        // 0-9
        for (int c = '0'; c <= '9'; c++)
            _hotkeyKeys.Add(new HotkeyKeyItem { Label = ((char)c).ToString(), Vk = c });
    }
    private void ApplyResponsiveLayout(double width)
    {
        var isNarrow = width < 720;

        if (isNarrow)
            Nav.IsPaneOpen = false;

        SettingsSearchBoxWide.Visibility = isNarrow ? Visibility.Collapsed : Visibility.Visible;
        SettingsSearchBoxNarrow.Visibility = isNarrow ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SettingsWindow_Closed(object sender, WindowEventArgs args)
    {
        _isClosing = true;

        // 蠢ｵ縺ｮ縺溘ａ隗｣髯､・磯哩縺倬圀縺ｮ繧､繝吶Φ繝磯｣帙・縺ｧ關ｽ縺｡繧九・繧帝亟縺撰ｼ・
        this.Activated -= SettingsWindow_Activated;
        this.SizeChanged -= SettingsWindow_SizeChanged;
        this.Closed -= SettingsWindow_Closed;

        _cachedAppWindow = null;
    }

    private void SettingsWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_isClosing) return;
        try
        {
            ConfigureTitleBarColorsSafe();
        }
        catch
        {
            // 髢峨§髫帙↓WinRT蛛ｴ縺御ｾ句､悶ｒ謚輔￡繧九％縺ｨ縺後≠繧九◆繧∵升繧・
        }
    }

    private void SettingsWindow_SizeChanged(object sender, WindowSizeChangedEventArgs e)
    {
        if (_isClosing) return;
        ApplyResponsiveLayout(e.Size.Width);
    }

    private void SetWindowSizeSafe(int width, int height)
    {
        try
        {
            var appWindow = TryGetAppWindow();
            appWindow?.Resize(new Windows.Graphics.SizeInt32(width, height));
        }
        catch
        {
        }
    }
    private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isClosing) return;

        if (sender is TextBox tb)
        {
            if (tb == SettingsSearchBoxWide && SettingsSearchBoxNarrow.Text != tb.Text)
                SettingsSearchBoxNarrow.Text = tb.Text;

            else if (tb == SettingsSearchBoxNarrow && SettingsSearchBoxWide.Text != tb.Text)
                SettingsSearchBoxWide.Text = tb.Text;
        }
        // 縺ｩ縺｡繧峨′ sender 縺ｧ繧ょ酔縺俶､懃ｴ｢隱槭ｒ菴ｿ縺・
        var q = (SettingsSearchBoxWide.Text ?? "").Trim().ToLowerInvariant();

        var panel =
            PanelGeneral.Visibility == Visibility.Visible ? PanelGeneral :
            PanelMove.Visibility == Visibility.Visible ? PanelMove :
            PanelAppList.Visibility == Visibility.Visible ? PanelAppList :
            PanelLog.Visibility == Visibility.Visible ? PanelLog :
            PanelExt.Visibility == Visibility.Visible ? PanelExt :
            PanelAbout;

        foreach (var child in panel.Children)
        {
            if (child is not FrameworkElement fe) continue;

            var tag = (fe.Tag as string ?? "").ToLowerInvariant();
            fe.Visibility = string.IsNullOrEmpty(q) || tag.Contains(q)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }
    private async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            var s = await SettingsStore.LoadAsync();

            var ensured = EnsureSelfInExcludeCsv(s.ExcludeProcessNamesCsv);
            if (!string.Equals(s.ExcludeProcessNamesCsv ?? "", ensured, StringComparison.Ordinal))
            {
                s.ExcludeProcessNamesCsv = ensured;
                await SettingsStore.SaveAsync();
            }

            SwStartMinToTray.IsOn = s.StartMinimizedToTray;
            SwCloseMinToTray.IsOn = s.CloseButtonMinimizesToTray;
            SwShowOverlay.IsOn = s.ShowMoveOverlay;
            TbExcludeCsv.Text = s.ExcludeProcessNamesCsv ?? "";
            SwVerboseLog.IsOn = s.EnableVerboseLog;
            ApplyHotkeyToUI(s);
            UpdateHotkeyPreview();
        }
        catch (Exception ex)
        {
            LogHandledException("LoadAsync", ex);
        }
        finally
        {
            _isLoading = false;
        }
    }
    private void ApplyHotkeyToUI(AtsumareSettings s)
    {
        // MOD_* 繧・int 縺ｧ菫晄戟・・in32縺ｨ蜷後§・・
        CbModAlt.IsChecked = (s.HotkeyModifiers & 0x0001) != 0;
        CbModCtrl.IsChecked = (s.HotkeyModifiers & 0x0002) != 0;
        CbModShift.IsChecked = (s.HotkeyModifiers & 0x0004) != 0;
        //CbModWin.IsChecked = (s.HotkeyModifiers & 0x0008) != 0;

        // VK 繧帝∈謚・
        var item = _hotkeyKeys.FirstOrDefault(x => x.Vk == s.HotkeyVirtualKey);
        if (item != null)
            CbHotkeyKey.SelectedItem = item;
        else
            CbHotkeyKey.SelectedItem = _hotkeyKeys.FirstOrDefault(x => x.Vk == 0x20);
    }

    private void UpdateHotkeyPreview()
    {
        var parts = new List<string>();
        if (CbModCtrl.IsChecked == true) parts.Add("Ctrl");
        if (CbModAlt.IsChecked == true) parts.Add("Alt");
        if (CbModShift.IsChecked == true) parts.Add("Shift");
        //if (CbModWin.IsChecked == true) parts.Add("Win");

        var key = (CbHotkeyKey.SelectedItem as HotkeyKeyItem)?.Label ?? "";
        if (!string.IsNullOrEmpty(key)) parts.Add(key);

        TbHotkeyPreview.Text = parts.Count > 0 ? string.Join(" + ", parts) : "";
    }
    private async void Hotkey_Changed(object sender, object e)
    {
        if (_isLoading || _isClosing) return;

        try
        {
            var mods = 0;
            if (CbModAlt.IsChecked == true) mods |= 0x0001;
            if (CbModCtrl.IsChecked == true) mods |= 0x0002;
            if (CbModShift.IsChecked == true) mods |= 0x0004;

            var vk = (CbHotkeyKey.SelectedItem as HotkeyKeyItem)?.Vk ?? 0x20;

            if (mods == 0) mods = 0x0002 | 0x0001;

            var s = SettingsStore.Current;
            s.HotkeyModifiers = mods;
            s.HotkeyVirtualKey = vk;

            UpdateHotkeyPreview();
            await SettingsStore.SaveAsync();
        }
        catch (Exception ex)
        {
            LogHandledException("Hotkey_Changed", ex);
        }
    }
    private async void AnySetting_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _isClosing) return;

        try
        {
            var s = SettingsStore.Current;
            s.StartMinimizedToTray = SwStartMinToTray.IsOn;
            s.CloseButtonMinimizesToTray = SwCloseMinToTray.IsOn;
            s.ShowMoveOverlay = SwShowOverlay.IsOn;
            s.EnableVerboseLog = SwVerboseLog.IsOn;

            await SettingsStore.SaveAsync();
        }
        catch (Exception ex)
        {
            LogHandledException("AnySetting_Toggled", ex);
        }
    }

    private async void AnySetting_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _isClosing) return;

        try
        {
            var normalized = NormalizeExcludeCsv(TbExcludeCsv.Text);

            if (string.IsNullOrWhiteSpace(normalized))
                normalized = Process.GetCurrentProcess().ProcessName;

            if (TbExcludeCsv.Text != normalized)
            {
                _isLoading = true;
                TbExcludeCsv.Text = normalized;
                TbExcludeCsv.SelectionStart = TbExcludeCsv.Text.Length;
                _isLoading = false;
            }

            SettingsStore.Current.ExcludeProcessNamesCsv = normalized;
            await SettingsStore.SaveAsync();
        }
        catch (Exception ex)
        {
            LogHandledException("AnySetting_TextChanged", ex);
        }
        finally
        {
            if (!_isClosing)
                _isLoading = false;
        }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_isClosing) return;
        if (args.SelectedItem is not NavigationViewItem item) return;

        var tag = (item.Tag as string) ?? "";

        PanelGeneral.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        PanelMove.Visibility = tag == "move" ? Visibility.Visible : Visibility.Collapsed;
        PanelAppList.Visibility = tag == "applist" ? Visibility.Visible : Visibility.Collapsed;
        PanelLog.Visibility = tag == "log" ? Visibility.Visible : Visibility.Collapsed;
        PanelExt.Visibility = tag == "ext" ? Visibility.Visible : Visibility.Collapsed;
        PanelAbout.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;

        PageTitle.Text = item.Content?.ToString() ?? "設定";
        SettingsSearchBoxWide.Text = "";
        SettingsSearchBoxNarrow.Text = "";
        PageSubtitle.Text = tag switch
        {
            "general" => "基本設定を変更します",
            "move" => "移動関連の動作を設定します",
            "applist" => "一覧表示するアプリを調整します",
            "log" => "ログと診断の設定です",
            "ext" => "今後の拡張向け設定です",
            "about" => "アプリ情報",
            _ => ""
        };
    }

    private AppWindow? TryGetAppWindow()
    {
        if (_isClosing) return null;
        if (_cachedAppWindow != null) return _cachedAppWindow;

        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero) return null;

            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            _cachedAppWindow = AppWindow.GetFromWindowId(windowId);
            return _cachedAppWindow;
        }
        catch
        {
            return null;
        }
    }

    private void ConfigureTitleBarColorsSafe()
    {
        if (_isClosing) return;

        var appWindow = TryGetAppWindow();
        if (appWindow == null) return;

        try
        {
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
            titleBar.InactiveBackgroundColor = bg;
            titleBar.ButtonInactiveBackgroundColor = bg;
        }
        catch
        {
            // 縺薙％縺瑚誠縺｡繧・☆縺・・縺ｧ謠｡繧・
        }
    }
    private void LogHandledException(string where, Exception ex)
    {
        Debug.WriteLine($"[SettingsWindow] {where}: {ex}");
        App.LogLine($"[SettingsWindow] {where}: {ex}");
    }

    private static string NormalizeExcludeCsv(string? csv)
    {
        var parts = (csv ?? "")
            .Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return string.Join(", ", parts);
    }

    private static string EnsureSelfInExcludeCsv(string? csv)
    {
        var normalized = NormalizeExcludeCsv(csv);
        if (!string.IsNullOrWhiteSpace(normalized)) return normalized;

        // Atsumare.exe -> "Atsumare"
        return Process.GetCurrentProcess().ProcessName;
    }

    private static bool IsPackaged()
    {
        try
        {
            // 繝代ャ繧ｱ繝ｼ繧ｸ縺ｪ繧・Package.Current 縺悟叙蠕励〒縺阪ｋ
            _ = Windows.ApplicationModel.Package.Current;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetLogDir()
    {
        if (IsPackaged())
        {
            // MSIX: LocalState 驟堺ｸ具ｼ・ackages\<PFN>\LocalState・・
            return System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, "logs");
        }

        // 髱朞SIX: 蠕捺擂騾壹ｊ
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Atsumare", "logs");
    }

    private async void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logDir = GetLogDir();
            System.IO.Directory.CreateDirectory(logDir);

            try
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(logDir);
                var ok = await Launcher.LaunchFolderAsync(folder);
                if (ok) return;
            }
            catch
            {
            }

            var ok2 = await Launcher.LaunchFolderPathAsync(logDir);
            if (!ok2)
            {
                Debug.WriteLine($"Failed to open log folder: {logDir}");
            }
        }
        catch (Exception ex)
        {
            LogHandledException("OpenLogFolder_Click", ex);
        }
    }
}

