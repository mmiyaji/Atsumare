using System.Collections.Generic;

namespace Atsumare;

public sealed class OverlayManager
{
    private readonly List<OverlayWindow> _windows = new();

    public bool IsShown => _windows.Count > 0;

    public void ShowAllMonitors()
    {
        Hide();

        foreach (var m in MonitorEnumerator.GetMonitors())
        {
            var w = new OverlayWindow(m);
            w.RequestCloseAll += Hide;
            _windows.Add(w);
            w.Activate();
        }
    }

    public void Hide()
    {
        foreach (var w in _windows)
        {
            try { w.Close(); } catch { }
        }
        _windows.Clear();
    }

    public void ToggleAllMonitors()
    {
        if (IsShown) Hide();
        else ShowAllMonitors();
    }
}
