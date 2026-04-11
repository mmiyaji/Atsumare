using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Threading.Tasks;
using WinRT.Interop;
using System.Diagnostics;
using System.Linq;
using System.Collections.ObjectModel;
using Windows.Storage;
using Windows.System;
namespace Atsumare;

public sealed partial class SettingsWindow : Window
{
    private const double MinPaneWidth = 220;
    private const double MaxPaneWidth = 420;

    private bool _isLoading;
    private bool _isClosing;
    private bool _isCapturingHotkey;
    private bool _isResizingPane;
    private bool _isPaneResizeHovering;
    private double _paneWidth = 280;
    private double _paneResizeStartX;
    private double _paneResizeStartWidth;

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

        this.SizeChanged += SettingsWindow_SizeChanged;
        this.Closed += SettingsWindow_Closed;
        Nav.PaneOpened += (_, __) => UpdatePaneResizeHandle(this.Bounds.Width < 720);
        Nav.PaneClosed += (_, __) => UpdatePaneResizeHandle(this.Bounds.Width < 720);

        InitHotkeyKeyCandidates();
        CbHotkeyKey.ItemsSource = _hotkeyKeys;
        Nav.OpenPaneLength = _paneWidth;
        WindowIconHelper.Apply(this);

        SetWindowSizeSafe(980, 640);
        ApplyResponsiveLayout(this.Bounds.Width);
        PopulateAboutInfo();
        ConfigureTitleBarColorsSafe();
        _ = LoadAsync();
    }

    private void PopulateAboutInfo()
    {
        TbAboutAuthor.Text = $"制作者: {AppMetadata.AuthorName}";
        TbAboutCopyright.Text = AppMetadata.CopyrightText;
        TbAboutVersion.Text = $"ビルドバージョン: {AppMetadata.VersionText}";
        TbAboutBuildDate.Text = $"ビルド日時: {AppMetadata.BuildDateText}";

        BtnSupportUrl.Content = AppMetadata.SupportUrl;
        BtnTermsUrl.Content = AppMetadata.TermsOfUseUrl;
        BtnPrivacyUrl.Content = AppMetadata.PrivacyPolicyUrl;
        BtnRepositoryUrl.Content = AppMetadata.RepositoryUrl;
        BtnWindowsAppSdkUrl.Content = AppMetadata.WindowsAppSdkProjectUrl;
        BtnDotNetLicenseUrl.Content = AppMetadata.DotNetRuntimeLicenseUrl;
        BtnWebView2Url.Content = AppMetadata.WebView2LicenseUrl;
    }

    private void InitHotkeyKeyCandidates()
    {
        _hotkeyKeys.Clear();

        _hotkeyKeys.Add(new HotkeyKeyItem { Label = "Space", Vk = 0x20 });
        _hotkeyKeys.Add(new HotkeyKeyItem { Label = "Enter", Vk = 0x0D });
        _hotkeyKeys.Add(new HotkeyKeyItem { Label = "Tab", Vk = 0x09 });
        _hotkeyKeys.Add(new HotkeyKeyItem { Label = "Esc", Vk = 0x1B });

        for (int i = 1; i <= 12; i++)
            _hotkeyKeys.Add(new HotkeyKeyItem { Label = $"F{i}", Vk = 0x70 + (i - 1) });

        for (int c = 'A'; c <= 'Z'; c++)
            _hotkeyKeys.Add(new HotkeyKeyItem { Label = ((char)c).ToString(), Vk = c });

        for (int c = '0'; c <= '9'; c++)
            _hotkeyKeys.Add(new HotkeyKeyItem { Label = ((char)c).ToString(), Vk = c });
    }

    private void ApplyResponsiveLayout(double width)
    {
        var isNarrow = width < 720;

        if (isNarrow)
            Nav.IsPaneOpen = false;
        else
            Nav.OpenPaneLength = _paneWidth;

        UpdatePaneResizeHandle(isNarrow);
    }

    private void UpdatePaneResizeHandle(bool isNarrow)
    {
        PaneResizeHandle.Visibility = !isNarrow && Nav.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        PaneResizeHandle.Margin = new Thickness(Math.Max(0, Nav.OpenPaneLength - (PaneResizeHandle.Width / 2)), 0, 0, 0);
        UpdatePaneResizeVisual();
    }

    private void SettingsWindow_Closed(object sender, WindowEventArgs args)
    {
        _isClosing = true;

        this.SizeChanged -= SettingsWindow_SizeChanged;
        this.Closed -= SettingsWindow_Closed;

        _cachedAppWindow = null;
    }

    private void SettingsWindow_SizeChanged(object sender, WindowSizeChangedEventArgs e)
    {
        if (_isClosing) return;
        ApplyResponsiveLayout(e.Size.Width);
    }

    private void PaneResizeHandle_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPaneResizeHovering = true;
        UpdatePaneResizeVisual();
    }

    private void PaneResizeHandle_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPaneResizeHovering = false;
        UpdatePaneResizeVisual();
    }

    private void PaneResizeHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!Nav.IsPaneOpen)
            return;

        _isResizingPane = true;
        _paneResizeStartX = e.GetCurrentPoint(null).Position.X;
        _paneResizeStartWidth = Nav.OpenPaneLength;
        PaneResizeHandle.CapturePointer(e.Pointer);
        UpdatePaneResizeVisual();
        e.Handled = true;
    }

    private void PaneResizeHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isResizingPane)
            return;

        var currentX = e.GetCurrentPoint(null).Position.X;
        var nextWidth = Math.Clamp(_paneResizeStartWidth + (currentX - _paneResizeStartX), MinPaneWidth, MaxPaneWidth);
        _paneWidth = nextWidth;
        Nav.OpenPaneLength = _paneWidth;
        UpdatePaneResizeHandle(this.Bounds.Width < 720);
        e.Handled = true;
    }

    private void PaneResizeHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isResizingPane)
            return;

        _isResizingPane = false;
        PaneResizeHandle.ReleasePointerCapture(e.Pointer);
        UpdatePaneResizeHandle(this.Bounds.Width < 720);
        e.Handled = true;
    }

    private void UpdatePaneResizeVisual()
    {
        if (PaneResizeHandle.Visibility != Visibility.Visible)
        {
            PaneResizeHover.Opacity = 0;
            PaneResizeGrip.Opacity = 0.35;
            return;
        }

        if (_isResizingPane)
        {
            PaneResizeHover.Opacity = 1;
            PaneResizeGrip.Opacity = 1;
            PaneResizeGrip.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 58, 122, 254));
            return;
        }

        PaneResizeHover.Opacity = _isPaneResizeHovering ? 0.85 : 0;
        PaneResizeGrip.Opacity = _isPaneResizeHovering ? 0.9 : 0.45;
        PaneResizeGrip.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 168, 179, 196));
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

    private async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            var s = await SettingsStore.LoadAsync();

            var ensured = SettingsWindowLogic.EnsureSelfInExcludeCsv(
                s.ExcludeProcessNamesCsv,
                Process.GetCurrentProcess().ProcessName);
            if (!string.Equals(s.ExcludeProcessNamesCsv ?? "", ensured, StringComparison.Ordinal))
            {
                s.ExcludeProcessNamesCsv = ensured;
                await SettingsStore.SaveAsync();
            }

            var startupStatus = await StartupRegistration.GetStatusAsync();

            SwStartMinToTray.IsOn = s.StartMinimizedToTray;
            UpdateStartupToggle(startupStatus);
            SwCloseMinToTray.IsOn = s.CloseButtonMinimizesToTray;
            SwShowOverlay.IsOn = s.ShowMoveOverlay;
            TbExcludeCsv.Text = s.ExcludeProcessNamesCsv ?? "";
            SwVerboseLog.IsOn = s.EnableVerboseLog;
            s.LaunchAtStartup = SwLaunchAtStartup.IsOn;
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
            if (!_isClosing)
                FadeInContent();
        }
    }

    private void FadeInContent()
    {
        if (Nav.Opacity >= 1)
            return;

        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(120))
        };

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        Storyboard.SetTarget(animation, Nav);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Begin();
    }

    private void ApplyHotkeyToUI(AtsumareSettings s)
    {
        CbModAlt.IsChecked = (s.HotkeyModifiers & 0x0001) != 0;
        CbModCtrl.IsChecked = (s.HotkeyModifiers & 0x0002) != 0;
        CbModShift.IsChecked = (s.HotkeyModifiers & 0x0004) != 0;

        var item = EnsureHotkeyKeyCandidate(s.HotkeyVirtualKey);
        if (item != null)
            CbHotkeyKey.SelectedItem = item;
        else
            CbHotkeyKey.SelectedItem = _hotkeyKeys.FirstOrDefault(x => x.Vk == 0x20);
    }

    private void UpdateHotkeyPreview()
    {
        var modifiers = 0;
        if (CbModAlt.IsChecked == true) modifiers |= 0x0001;
        if (CbModCtrl.IsChecked == true) modifiers |= 0x0002;
        if (CbModShift.IsChecked == true) modifiers |= 0x0004;

        TbHotkeyPreview.Text = SettingsWindowLogic.BuildHotkeyPreview(
            modifiers,
            (CbHotkeyKey.SelectedItem as HotkeyKeyItem)?.Label);
    }

    private void SetHotkeyStatus(string text, bool isError = false)
    {
        TbHotkeyStatus.Text = text;
        TbHotkeyStatus.Foreground = isError
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 130, 130))
            : null;
    }

    private HotkeyKeyItem EnsureHotkeyKeyCandidate(int vk)
    {
        var existing = _hotkeyKeys.FirstOrDefault(x => x.Vk == vk);
        if (existing != null)
            return existing;

        var item = new HotkeyKeyItem
        {
            Label = SettingsWindowLogic.GetVirtualKeyLabel(vk),
            Vk = vk
        };
        _hotkeyKeys.Add(item);
        return item;
    }

    private void UpdateCaptureButtonState()
    {
        BtnCaptureHotkey.Content = _isCapturingHotkey ? "入力待ち..." : "ショートカットを記録";
    }

    private async Task ResetHotkeyToDefaultAsync()
    {
        const int defaultModifiers = SettingsWindowLogic.DefaultHotkeyModifiers;
        const int defaultVirtualKey = 0x20;

        _isCapturingHotkey = false;
        UpdateCaptureButtonState();

        _isLoading = true;
        CbModAlt.IsChecked = (defaultModifiers & 0x0001) != 0;
        CbModCtrl.IsChecked = (defaultModifiers & 0x0002) != 0;
        CbModShift.IsChecked = (defaultModifiers & 0x0004) != 0;
        CbHotkeyKey.SelectedItem = EnsureHotkeyKeyCandidate(defaultVirtualKey);
        _isLoading = false;

        UpdateHotkeyPreview();
        await TryApplyHotkeySelectionAsync(defaultModifiers, defaultVirtualKey, normalizeEmptyModifiers: false);
    }

    private async Task TryApplyHotkeySelectionAsync(int modifiers, int vk, bool normalizeEmptyModifiers)
    {
        var effectiveModifiers = normalizeEmptyModifiers
            ? SettingsWindowLogic.NormalizeHotkeyModifiers(modifiers)
            : modifiers;

        if (!SettingsWindowLogic.TryValidateHotkeySelection(effectiveModifiers, vk, out var validationMessage))
        {
            SetHotkeyStatus(validationMessage, isError: true);
            return;
        }

        if (normalizeEmptyModifiers && effectiveModifiers != modifiers)
        {
            _isLoading = true;
            CbModAlt.IsChecked = (effectiveModifiers & 0x0001) != 0;
            CbModCtrl.IsChecked = (effectiveModifiers & 0x0002) != 0;
            CbModShift.IsChecked = (effectiveModifiers & 0x0004) != 0;
            _isLoading = false;
        }

        var current = SettingsStore.Current;
        var isUnchanged = current.HotkeyModifiers == effectiveModifiers && current.HotkeyVirtualKey == vk;
        if (!isUnchanged && !HotkeyHost.CanRegisterHotkey((HotkeyModifiers)effectiveModifiers, (uint)vk, out var errorCode))
        {
            SetHotkeyStatus(BuildHotkeyRegistrationErrorMessage(errorCode), isError: true);
            return;
        }

        current.HotkeyModifiers = effectiveModifiers;
        current.HotkeyVirtualKey = vk;
        UpdateHotkeyPreview();
        await SettingsStore.SaveAsync();
        SetHotkeyStatus("ショートカットを保存しました。");
    }

    private static string BuildHotkeyRegistrationErrorMessage(int errorCode) => errorCode switch
    {
        1409 => "このショートカットは他のアプリまたは Windows が使用中です。",
        _ => $"ショートカットを登録できませんでした。(err={errorCode})"
    };

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

            await TryApplyHotkeySelectionAsync(mods, vk, normalizeEmptyModifiers: true);
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

            if (sender is ToggleSwitch toggle && ReferenceEquals(toggle, SwLaunchAtStartup))
            {
                var startupStatus = await StartupRegistration.SetEnabledAsync(SwLaunchAtStartup.IsOn);
                UpdateStartupToggle(startupStatus);
                s.LaunchAtStartup = startupStatus == StartupRegistrationStatus.Enabled;
            }
            else
            {
                s.LaunchAtStartup = SwLaunchAtStartup.IsOn;
            }
            s.CloseButtonMinimizesToTray = SwCloseMinToTray.IsOn;
            s.ShowMoveOverlay = SwShowOverlay.IsOn;
            s.EnableVerboseLog = SwVerboseLog.IsOn;

            await SettingsStore.SaveAsync();
        }
        catch (Exception ex)
        {
            LogHandledException("AnySetting_Toggled", ex);
        }
        finally
        {
            if (!_isClosing)
                _isLoading = false;
        }
    }

    private async void AnySetting_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _isClosing) return;

        try
        {
            var normalized = SettingsWindowLogic.NormalizeExcludeCsv(TbExcludeCsv.Text);

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

    private async void BtnCaptureHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_isClosing)
            return;

        _isCapturingHotkey = true;
        UpdateCaptureButtonState();
        SetHotkeyStatus("押したショートカットを記録します。Esc でキャンセルできます。");
        BtnCaptureHotkey.Focus(FocusState.Programmatic);
    }

    private async void BtnResetHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_isClosing)
            return;

        try
        {
            await ResetHotkeyToDefaultAsync();
        }
        catch (Exception ex)
        {
            LogHandledException("BtnResetHotkey_Click", ex);
        }
    }

    private async void HotkeyCaptureScope_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_isCapturingHotkey || _isClosing)
            return;

        var vk = (int)e.Key;
        var modifiers = GetPressedHotkeyModifiers();

        if (vk == 0x1B && modifiers == 0)
        {
            _isCapturingHotkey = false;
            UpdateCaptureButtonState();
            SetHotkeyStatus("ショートカット記録をキャンセルしました。");
            e.Handled = true;
            return;
        }

        if (SettingsWindowLogic.IsModifierKey(vk))
        {
            SetHotkeyStatus("修飾キーに続けて、もう 1 つキーを押してください。", isError: true);
            e.Handled = true;
            return;
        }

        _isCapturingHotkey = false;
        UpdateCaptureButtonState();

        _isLoading = true;
        CbModAlt.IsChecked = (modifiers & 0x0001) != 0;
        CbModCtrl.IsChecked = (modifiers & 0x0002) != 0;
        CbModShift.IsChecked = (modifiers & 0x0004) != 0;
        CbHotkeyKey.SelectedItem = EnsureHotkeyKeyCandidate(vk);
        _isLoading = false;

        UpdateHotkeyPreview();
        await TryApplyHotkeySelectionAsync(modifiers, vk, normalizeEmptyModifiers: false);
        e.Handled = true;
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
        PanelLicenses.Visibility = tag == "licenses" ? Visibility.Visible : Visibility.Collapsed;

        PageTitle.Text = item.Content?.ToString() ?? "設定";
        PageSubtitle.Text = tag switch
        {
            "general" => "基本設定を変更します",
            "move" => "移動関連の動作を設定します",
            "applist" => "一覧表示するアプリを調整します",
            "log" => "ログと診断の設定です",
            "ext" => "今後の拡張向け設定です",
            "about" => "アプリ情報",
            "licenses" => "第三者コンポーネントとライセンス情報",
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
        }
    }

    private void LogHandledException(string where, Exception ex)
    {
        Debug.WriteLine($"[SettingsWindow] {where}: {ex}");
        App.LogLine($"[SettingsWindow] {where}: {ex}");
    }

    private void UpdateStartupToggle(StartupRegistrationStatus status)
    {
        _isLoading = true;
        SwLaunchAtStartup.IsOn = status == StartupRegistrationStatus.Enabled;
        SwLaunchAtStartup.IsEnabled = status != StartupRegistrationStatus.Unsupported;
        TbStartupStatus.Text = status switch
        {
            StartupRegistrationStatus.Enabled => "有効です。",
            StartupRegistrationStatus.Disabled => "無効です。",
            StartupRegistrationStatus.DisabledByUser => "Windows 側で無効化されています。スタートアップ アプリから再度有効化してください。",
            StartupRegistrationStatus.DisabledByPolicy => "組織のポリシーで無効化されています。",
            _ => "この配布形態では利用できないか、状態を取得できませんでした。"
        };
        _isLoading = false;
    }

    private int GetPressedHotkeyModifiers()
    {
        var modifiers = 0;
        if (IsVkPressed(0x12)) modifiers |= 0x0001;
        if (IsVkPressed(0x11)) modifiers |= 0x0002;
        if (IsVkPressed(0x10)) modifiers |= 0x0004;
        if (IsVkPressed(0x5B) || IsVkPressed(0x5C)) modifiers |= 0x0008;
        return modifiers;
    }

    private static bool IsVkPressed(int virtualKey) => (GetKeyState(virtualKey) & 0x8000) != 0;

    private static bool IsPackaged()
    {
        try
        {
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
            return System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, "logs");
        }

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

    private async Task OpenUriAsync(string uri)
    {
        try
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
        catch (Exception ex)
        {
            LogHandledException("OpenUriAsync", ex);
        }
    }

    private async void BtnSupportUrl_Click(object sender, RoutedEventArgs e) => await OpenUriAsync(AppMetadata.SupportUrl);
    private async void BtnTermsUrl_Click(object sender, RoutedEventArgs e) => await OpenUriAsync(AppMetadata.TermsOfUseUrl);
    private async void BtnPrivacyUrl_Click(object sender, RoutedEventArgs e) => await OpenUriAsync(AppMetadata.PrivacyPolicyUrl);
    private async void BtnRepositoryUrl_Click(object sender, RoutedEventArgs e) => await OpenUriAsync(AppMetadata.RepositoryUrl);
    private async void BtnWindowsAppSdkUrl_Click(object sender, RoutedEventArgs e) => await OpenUriAsync(AppMetadata.WindowsAppSdkProjectUrl);
    private async void BtnDotNetLicenseUrl_Click(object sender, RoutedEventArgs e) => await OpenUriAsync(AppMetadata.DotNetRuntimeLicenseUrl);
    private async void BtnWebView2Url_Click(object sender, RoutedEventArgs e) => await OpenUriAsync(AppMetadata.WebView2LicenseUrl);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
