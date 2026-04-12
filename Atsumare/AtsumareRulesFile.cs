namespace Atsumare;

internal sealed class AtsumareRulesFile
{
    public string FormatVersion { get; set; } = "1";
    public string ExportedAtUtc { get; set; } = "";
    public string DefaultTargetMonitorKey { get; set; } = "current";
    public bool PreserveMaximizedOnMove { get; set; } = true;
    public bool FocusMovedAppAfterMove { get; set; } = false;
    public bool AutoPinMovedApps { get; set; } = false;
    public bool DisableRecentSorting { get; set; } = false;
    public string ExcludeProcessNamesCsv { get; set; } = "";
    public string PinnedAppKeysCsv { get; set; } = "";
    public string RecentAppKeysCsv { get; set; } = "";
}
