using System.Collections.Generic;

namespace Atsumare;

public sealed class OverlayManager
{
    private readonly List<DimmerWindow> _dimmers = new();
    private readonly List<OverlayWindow> _windows = new();

    public bool IsShown => _windows.Count > 0;

    public void ShowAllMonitors()
    {
        Hide();

        //byte dimAlpha = 160; // 0-255（好みで調整）

        foreach (var m in MonitorEnumerator.GetMonitors())
        {
            var dim = new DimmerWindow(m, 160);
            _dimmers.Add(dim);
            dim.Activate();
        }

        foreach (var m in MonitorEnumerator.GetMonitors())
        {
            var w = new OverlayWindow(m);
            w.RequestCloseAll += Hide;
            _windows.Add(w);
            w.Activate(); // こっちが上に来る
        }

    }


    public void Hide()
    {
        foreach (var w in _windows) { try { w.Close(); } catch { } }
        _windows.Clear();

        foreach (var d in _dimmers) { try { d.Close(); } catch { } }
        _dimmers.Clear();
    }


    public void ToggleAllMonitors()
    {
        if (IsShown) Hide();
        else ShowAllMonitors();
    }
}
