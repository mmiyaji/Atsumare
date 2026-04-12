namespace Atsumare;

public sealed class AtsumareSettings
{
    // General
    public bool StartMinimizedToTray { get; set; } = true;
    public bool LaunchAtStartup { get; set; } = false;
    public bool CloseButtonMinimizesToTray { get; set; } = true;
    public string UiLanguage { get; set; } = "";
    public bool HasCompletedOnboarding { get; set; } = false;

    // Hotkey (Win32 MOD_* / VK_*)
    // MOD_ALT=0x0001, MOD_CONTROL=0x0002, MOD_SHIFT=0x0004, MOD_WIN=0x0008
    public int HotkeyModifiers { get; set; } = SettingsWindowLogic.DefaultHotkeyModifiers;
    public int HotkeyVirtualKey { get; set; } = SettingsWindowLogic.DefaultHotkeyVirtualKey;

    // App list
    public string ExcludeProcessNamesCsv { get; set; } = "";
    public string PinnedAppKeysCsv { get; set; } = "";
    public string RecentAppKeysCsv { get; set; } = "";
    public bool ShowWindowCountInList { get; set; } = true;
    public bool AutoPinMovedApps { get; set; } = false;
    public bool DisableRecentSorting { get; set; } = false;

    // Move
    public string DefaultTargetMonitorKey { get; set; } = "current";
    public bool PreserveMaximizedOnMove { get; set; } = true;
    public bool FocusMovedAppAfterMove { get; set; } = true;
    public int SettingsMigrationVersion { get; set; } = 0;

    // Debug
    public bool EnableVerboseLog { get; set; } = false;
}
