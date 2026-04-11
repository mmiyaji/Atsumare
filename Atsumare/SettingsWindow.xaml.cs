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
    private bool _isPaneCollapsed;
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
        if (this.Content is FrameworkElement root)
            root.Language = AppLanguage.GetEffectiveLanguage(SettingsStore.Current);
        Title = $"Atsumare - {AppStrings.Get("SettingsWindow.Title")}";
        ApplyLocalizedUi();

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
        CenterWindowSafe();
        ApplyResponsiveLayout(this.Bounds.Width);
        PopulateAboutInfo();
        ConfigureTitleBarColorsSafe();
        _ = LoadAsync();
    }

    private void ApplyLocalizedUi()
    {
        var language = AppLanguage.GetEffectiveLanguage(SettingsStore.Current);
        var fontFamily = language == AppLanguage.Japanese
            ? new FontFamily("Yu Gothic UI")
            : new FontFamily("Segoe UI");

        CbUiLanguage.FontFamily = fontFamily;
        CbUiLanguageSystem.FontFamily = fontFamily;
        CbUiLanguageEnglish.FontFamily = fontFamily;
        CbUiLanguageJapanese.FontFamily = fontFamily;

        NavGeneralItem.Content = AppStrings.Get("SettingsWindow.NavGeneral.Content");
        NavMoveItem.Content = AppStrings.Get("SettingsWindow.NavMove.Content");
        NavAppListItem.Content = AppStrings.Get("SettingsWindow.NavAppList.Content");
        NavLogItem.Content = AppStrings.Get("SettingsWindow.NavLog.Content");
        NavExtItem.Content = AppStrings.Get("SettingsWindow.NavExt.Content");
        NavAboutItem.Content = AppStrings.Get("SettingsWindow.NavAbout.Content");
        NavLicensesItem.Content = AppStrings.Get("SettingsWindow.NavLicenses.Content");

        TbLanguageTitle.Text = AppStrings.Get("SettingsWindow.LanguageTitle.Text");
        TbLanguageDesc.Text = AppStrings.Get("SettingsWindow.LanguageDesc.Text");
        TbLanguageLabel.Text = AppStrings.Get("SettingsWindow.LanguageLabel.Text");
        CbUiLanguageSystem.Content = AppStrings.Get("SettingsWindow.UiLanguage.System.Content");
        CbUiLanguageEnglish.Content = AppStrings.Get("SettingsWindow.UiLanguage.English.Content");
        CbUiLanguageJapanese.Content = AppStrings.Get("SettingsWindow.UiLanguage.Japanese.Content");

        TbGeneralStartMinTitle.Text = AppStrings.Get("SettingsWindow.GeneralStartMinTitle.Text");
        TbGeneralStartMinDesc.Text = AppStrings.Get("SettingsWindow.GeneralStartMinDesc.Text");
        TbGeneralCloseMinTitle.Text = AppStrings.Get("SettingsWindow.GeneralCloseMinTitle.Text");
        TbGeneralCloseMinDesc.Text = AppStrings.Get("SettingsWindow.GeneralCloseMinDesc.Text");
        TbGeneralLaunchStartupTitle.Text = AppStrings.Get("SettingsWindow.GeneralLaunchStartupTitle.Text");
        TbGeneralLaunchStartupDesc.Text = AppStrings.Get("SettingsWindow.GeneralLaunchStartupDesc.Text");
        TbHotkeyTitle.Text = AppStrings.Get("SettingsWindow.HotkeyTitle.Text");
        TbHotkeyDesc.Text = AppStrings.Get("SettingsWindow.HotkeyDesc.Text");
        TbHotkeyKeyLabel.Text = AppStrings.Get("SettingsWindow.HotkeyKeyLabel.Text");
        BtnCaptureHotkey.Content = AppStrings.Get("SettingsWindow.BtnCaptureHotkey.Content");
        BtnResetHotkey.Content = AppStrings.Get("SettingsWindow.BtnResetHotkey.Content");
        TbMoveOverlayTitle.Text = AppStrings.Get("SettingsWindow.MoveOverlayTitle.Text");
        TbMoveOverlayDesc.Text = AppStrings.Get("SettingsWindow.MoveOverlayDesc.Text");
        TbExcludeProcessTitle.Text = AppStrings.Get("SettingsWindow.ExcludeProcessTitle.Text");
        TbExcludeProcessDesc.Text = AppStrings.Get("SettingsWindow.ExcludeProcessDesc.Text");
        TbVerboseLogTitle.Text = AppStrings.Get("SettingsWindow.VerboseLogTitle.Text");
        TbVerboseLogDesc.Text = AppStrings.Get("SettingsWindow.VerboseLogDesc.Text");
        TbLogFolderTitle.Text = AppStrings.Get("SettingsWindow.LogFolderTitle.Text");
        TbLogFolderDesc.Text = AppStrings.Get("SettingsWindow.LogFolderDesc.Text");
        BtnOpenLogFolder.Content = AppStrings.Get("SettingsWindow.OpenLogFolderButton.Content");
        TbExtensionsTitle.Text = AppStrings.Get("SettingsWindow.ExtensionsTitle.Text");
        TbExtensionsDesc.Text = AppStrings.Get("SettingsWindow.ExtensionsDesc.Text");
        TbAboutAppDesc.Text = AppStrings.Get("SettingsWindow.AboutAppDesc.Text");
        TbSupportTitle.Text = AppStrings.Get("SettingsWindow.SupportTitle.Text");
        TbSupportDesc.Text = AppStrings.Get("SettingsWindow.SupportDesc.Text");
        TbTermsTitle.Text = AppStrings.Get("SettingsWindow.TermsTitle.Text");
        TbTermsLine1.Text = AppStrings.Get("SettingsWindow.TermsLine1.Text");
        TbTermsLine2.Text = AppStrings.Get("SettingsWindow.TermsLine2.Text");
        TbTermsLine3.Text = AppStrings.Get("SettingsWindow.TermsLine3.Text");
        TbLicenseWinAppSdkTitle.Text = AppStrings.Get("SettingsWindow.LicenseWinAppSdkTitle.Text");
        TbLicenseWinAppSdkCopyright.Text = AppStrings.Get("SettingsWindow.LicenseWinAppSdkCopyright.Text");
        TbLicenseWinAppSdkDesc.Text = AppStrings.Get("SettingsWindow.LicenseWinAppSdkDesc.Text");
        TbLicenseWinAppSdkTerms.Text = AppStrings.Get("SettingsWindow.LicenseWinAppSdkTerms.Text");
        TbLicenseDotNetTitle.Text = AppStrings.Get("SettingsWindow.LicenseDotNetTitle.Text");
        TbLicenseDotNetCopyright.Text = AppStrings.Get("SettingsWindow.LicenseDotNetCopyright.Text");
        TbLicenseDotNetTerms.Text = AppStrings.Get("SettingsWindow.LicenseDotNetTerms.Text");
        TbLicenseWebViewTitle.Text = AppStrings.Get("SettingsWindow.LicenseWebViewTitle.Text");
        TbLicenseWebViewDesc.Text = AppStrings.Get("SettingsWindow.LicenseWebViewDesc.Text");
        TbLicenseWebViewTerms.Text = AppStrings.Get("SettingsWindow.LicenseWebViewTerms.Text");
    }

    private void PopulateAboutInfo()
    {
        TbAboutAuthor.Text = AppStrings.Format("SettingsWindow.AboutAuthorFormat", AppMetadata.AuthorName);
        TbAboutCopyright.Text = AppMetadata.CopyrightText;
        TbAboutVersion.Text = AppStrings.Format("SettingsWindow.AboutVersionFormat", AppMetadata.VersionText);
        TbAboutBuildDate.Text = AppStrings.Format("SettingsWindow.AboutBuildDateFormat", AppMetadata.BuildDateText);

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
        Nav.IsPaneOpen = !_isPaneCollapsed;
        if (Nav.IsPaneOpen)
            Nav.OpenPaneLength = _paneWidth;

        UpdatePaneToggleVisual();
        UpdatePaneResizeHandle(false);
    }

    private void UpdatePaneToggleVisual()
    {
        BtnTogglePaneIcon.Glyph = _isPaneCollapsed ? "\uE700" : "\uE76B";
    }

    private void UpdatePaneResizeHandle(bool isNarrow)
    {
        var isVisible = !isNarrow && Nav.IsPaneOpen;
        PaneResizeHandle.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        PaneResizeHandle.Margin = new Thickness(Math.Max(0, Nav.OpenPaneLength - (PaneResizeHandle.Width / 2)), 0, 0, 0);
        if (!isVisible)
            ResetPaneResizeVisualState();
        UpdatePaneResizeVisual();
    }

    private void ResetPaneResizeVisualState()
    {
        _isResizingPane = false;
        _isPaneResizeHovering = false;
    }

    private void BtnTogglePane_Click(object sender, RoutedEventArgs e)
    {
        if (_isClosing)
            return;

        _isPaneCollapsed = !_isPaneCollapsed;
        ApplyResponsiveLayout(this.Bounds.Width);
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
        _isPaneResizeHovering = false;
        PaneResizeHandle.ReleasePointerCapture(e.Pointer);
        UpdatePaneResizeHandle(this.Bounds.Width < 720);
        e.Handled = true;
    }

    private void PaneResizeHandle_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        ResetPaneResizeVisualState();
        UpdatePaneResizeVisual();
    }

    private void UpdatePaneResizeVisual()
    {
        if (PaneResizeHandle.Visibility != Visibility.Visible)
        {
            PaneResizeHover.Opacity = 0;
            PaneResizeGrip.Opacity = 0;
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
        PaneResizeGrip.Opacity = _isPaneResizeHovering ? 0.9 : 0;
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

    private void CenterWindowSafe()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
                return;

            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = TryGetAppWindow();
            if (appWindow == null)
                return;

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            var size = appWindow.Size;

            var x = workArea.X + Math.Max(0, (workArea.Width - size.Width) / 2);
            var y = workArea.Y + Math.Max(0, (workArea.Height - size.Height) / 2);

            appWindow.Move(new Windows.Graphics.PointInt32(x, y));
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
            SelectUiLanguage(s.UiLanguage);
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

    private void SetLanguageStatus(string text)
    {
        TbLanguageStatus.Text = text;
        TbLanguageStatus.Foreground = null;
    }

    private void SelectUiLanguage(string? languageTag)
    {
        var normalized = AppLanguage.Normalize(languageTag);
        foreach (var item in CbUiLanguage.Items.OfType<ComboBoxItem>())
        {
            var tag = item.Tag as string ?? "";
            if (string.Equals(tag, normalized, StringComparison.OrdinalIgnoreCase))
            {
                CbUiLanguage.SelectedItem = item;
                return;
            }
        }

        CbUiLanguage.SelectedIndex = 0;
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
        BtnCaptureHotkey.Content = _isCapturingHotkey
            ? AppStrings.Get("SettingsWindow.CaptureWaiting")
            : AppStrings.Get("SettingsWindow.CaptureButtonDefault");
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

        if (!SettingsWindowLogic.TryValidateHotkeySelection(effectiveModifiers, vk, out var validationMessageKey))
        {
            SetHotkeyStatus(AppStrings.Get(validationMessageKey), isError: true);
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
        SetHotkeyStatus(AppStrings.Get("SettingsWindow.HotkeySaved"));
    }

    private static string BuildHotkeyRegistrationErrorMessage(int errorCode) => errorCode switch
    {
        1409 => AppStrings.Get("SettingsWindow.HotkeyConflict"),
        _ => AppStrings.Format("SettingsWindow.HotkeyRegistrationFailedFormat", errorCode)
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

    private async void UiLanguage_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _isClosing)
            return;

        try
        {
            var selected = (CbUiLanguage.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            var normalized = AppLanguage.Normalize(selected);
            if (string.Equals(SettingsStore.Current.UiLanguage ?? "", normalized, StringComparison.OrdinalIgnoreCase))
                return;

            SettingsStore.Current.UiLanguage = normalized;
            await SettingsStore.SaveAsync();
            AppLanguage.Apply(SettingsStore.Current);
            SetLanguageStatus(AppStrings.Get("SettingsWindow.LanguageSaved"));
            await Task.Delay(150);
            App.RestartApplication(GetSelectedSectionTag());
        }
        catch (Exception ex)
        {
            LogHandledException("UiLanguage_Changed", ex);
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
        SetHotkeyStatus(AppStrings.Get("SettingsWindow.HotkeyCapturePrompt"));
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
            SetHotkeyStatus(AppStrings.Get("SettingsWindow.HotkeyCaptureCanceled"));
            e.Handled = true;
            return;
        }

        if (SettingsWindowLogic.IsModifierKey(vk))
        {
            SetHotkeyStatus(AppStrings.Get("SettingsWindow.HotkeyCaptureNeedSecondKey"), isError: true);
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

        ApplySection(item);
    }

    private void ApplySection(NavigationViewItem item)
    {
        var tag = (item.Tag as string) ?? "";

        PanelGeneral.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        PanelMove.Visibility = tag == "move" ? Visibility.Visible : Visibility.Collapsed;
        PanelAppList.Visibility = tag == "applist" ? Visibility.Visible : Visibility.Collapsed;
        PanelLog.Visibility = tag == "log" ? Visibility.Visible : Visibility.Collapsed;
        PanelExt.Visibility = tag == "ext" ? Visibility.Visible : Visibility.Collapsed;
        PanelAbout.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;
        PanelLicenses.Visibility = tag == "licenses" ? Visibility.Visible : Visibility.Collapsed;

        PageTitle.Text = item.Content?.ToString() ?? AppStrings.Get("SettingsWindow.PageTitleDefault");
        PageSubtitle.Text = tag switch
        {
            "general" => AppStrings.Get("SettingsWindow.PageSubtitle.General"),
            "move" => AppStrings.Get("SettingsWindow.PageSubtitle.Move"),
            "applist" => AppStrings.Get("SettingsWindow.PageSubtitle.AppList"),
            "log" => AppStrings.Get("SettingsWindow.PageSubtitle.Log"),
            "ext" => AppStrings.Get("SettingsWindow.PageSubtitle.Ext"),
            "about" => AppStrings.Get("SettingsWindow.PageSubtitle.About"),
            "licenses" => AppStrings.Get("SettingsWindow.PageSubtitle.Licenses"),
            _ => ""
        };
    }

    public void SelectSectionByTag(string sectionTag)
    {
        var item = Nav.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(x => string.Equals(x.Tag as string, sectionTag, StringComparison.OrdinalIgnoreCase));
        if (item == null)
            return;

        Nav.SelectedItem = item;
        ApplySection(item);
    }

    public string? GetSelectedSectionTag()
    {
        return (Nav.SelectedItem as NavigationViewItem)?.Tag as string;
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
            StartupRegistrationStatus.Enabled => AppStrings.Get("SettingsWindow.StartupStatus.Enabled"),
            StartupRegistrationStatus.Disabled => AppStrings.Get("SettingsWindow.StartupStatus.Disabled"),
            StartupRegistrationStatus.DisabledByUser => AppStrings.Get("SettingsWindow.StartupStatus.DisabledByUser"),
            StartupRegistrationStatus.DisabledByPolicy => AppStrings.Get("SettingsWindow.StartupStatus.DisabledByPolicy"),
            _ => AppStrings.Get("SettingsWindow.StartupStatus.Unsupported")
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
