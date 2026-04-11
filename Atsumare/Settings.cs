namespace Atsumare;

public sealed class AtsumareSettings
{
    // General
    public bool StartMinimizedToTray { get; set; } = true;
    public bool LaunchAtStartup { get; set; } = false;
    public bool CloseButtonMinimizesToTray { get; set; } = true;
    public string UiLanguage { get; set; } = "";

    // Hotkey (Win32 MOD_* / VK_*)
    // MOD_ALT=0x0001, MOD_CONTROL=0x0002, MOD_SHIFT=0x0004, MOD_WIN=0x0008
    public int HotkeyModifiers { get; set; } = 0x0002 | 0x0001; // Ctrl + Alt
    public int HotkeyVirtualKey { get; set; } = 0x20; // Space

    // App list
    public string ExcludeProcessNamesCsv { get; set; } = "";

    // Debug
    public bool EnableVerboseLog { get; set; } = false;
}
