namespace Atsumare;

public sealed class AtsumareSettings
{
    // General
    public bool StartMinimizedToTray { get; set; } = true;
    public bool CloseButtonMinimizesToTray { get; set; } = true;

    // Move / Overlay
    public bool ShowMoveOverlay { get; set; } = false;

    // App list
    public string ExcludeProcessNamesCsv { get; set; } = ""; // e.g. "explorer,obs64"

    // Debug
    public bool EnableVerboseLog { get; set; } = false;
}